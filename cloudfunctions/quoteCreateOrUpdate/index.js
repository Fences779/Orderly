const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const ALLOWED_OPENIDS_ENV_NAME = 'ORDERLY_ALLOWED_OPENIDS'
const AUTH_ALLOW_ALL_DEV_ENV_NAME = 'ORDERLY_AUTH_ALLOW_ALL_DEV'
const MAX_EVENT_BYTES = 65536
const QUOTE_FIELDS = ['_id', 'dealId', 'quoteNo', 'quoteStatus', 'validUntil', 'quoteNote', 'sentAt', 'respondedAt', 'items', 'baseAmount', 'customFee', 'laborFee', 'shippingFee', 'discountAmount', 'depositRequired']
const QUOTE_ITEM_FIELDS = ['name', 'qty', 'price', 'note', 'skuId', 'materialCode', 'unit']

function now() {
  return new Date().toISOString()
}

function requireOperatorId() {
  const operatorId = cloud.getWXContext().OPENID || ''
  if (!operatorId) return { ok: false, code: 'unauthorized', message: '未授权调用。' }
  if (!isAllowedOperatorId(operatorId)) return { ok: false, code: 'forbidden', message: '无权访问。' }
  return { ok: true, operatorId }
}

function isAllowedOperatorId(operatorId) {
  const allowed = normalizeList(process.env[ALLOWED_OPENIDS_ENV_NAME])
  return allowed.indexOf(operatorId) >= 0 || isAuthAllowAllDevEnabled()
}

function isAuthAllowAllDevEnabled() {
  const runtime = String(process.env.ORDERLY_RUNTIME_ENV || process.env.NODE_ENV || '').trim().toLowerCase()
  return process.env[AUTH_ALLOW_ALL_DEV_ENV_NAME] === '1' && ['development', 'dev', 'test', 'local'].indexOf(runtime) >= 0
}

function rejectOversizedEvent(event) {
  const bytes = Buffer.byteLength(JSON.stringify(event || {}), 'utf8')
  return bytes > MAX_EVENT_BYTES ? { ok: false, code: 'payload_too_large', message: '请求体过大。' } : null
}

function normalizeList(value) {
  return String(value || '')
    .split(/[,\s，、;；]+/)
    .map((item) => item.trim())
    .filter(Boolean)
}

function normalizeText(value) {
  return value == null ? '' : String(value).trim()
}

function pickFields(source, allowedFields) {
  const result = {}
  const input = source || {}
  allowedFields.forEach((field) => {
    if (Object.prototype.hasOwnProperty.call(input, field)) result[field] = input[field]
  })
  return result
}

function resolveWorkspaceId(event) {
  const workspaceId = String((event && event.workspaceId) || '').trim() || DEFAULT_WORKSPACE_ID
  const configured = normalizeList(process.env[ALLOWED_WORKSPACE_IDS_ENV_NAME])
  const allowed = configured.length ? configured : [DEFAULT_WORKSPACE_ID]
  if (allowed.indexOf(workspaceId) < 0) return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  return { ok: true, workspaceId }
}

function addHours(hours) {
  const date = new Date()
  date.setHours(date.getHours() + hours)
  return date.toISOString()
}

function quoteNo() {
  const date = new Date()
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return 'Q' + y + m + d + '-' + Math.random().toString(36).slice(2, 6).toUpperCase()
}

function money(value) {
  const num = Number(value || 0)
  return Number.isFinite(num) && num >= 0 ? num : 0
}

function normalizeQuoteItems(value) {
  const items = Array.isArray(value) ? value : []
  return items.map((item) => {
    const input = pickFields(item, QUOTE_ITEM_FIELDS)
    return {
      name: normalizeText(input.name),
      qty: money(input.qty || 1),
      price: money(input.price),
      note: normalizeText(input.note),
      skuId: normalizeText(input.skuId),
      materialCode: normalizeText(input.materialCode),
      unit: normalizeText(input.unit)
    }
  }).filter((item) => item.name || item.price > 0)
}

function calculate(input) {
  input = pickFields(input, QUOTE_FIELDS)
  const items = normalizeQuoteItems(input.items)
  const itemBase = items.reduce((sum, item) => sum + money(item.qty || 1) * money(item.price), 0)
  const baseAmount = money(input.baseAmount || itemBase)
  const customFee = money(input.customFee)
  const laborFee = money(input.laborFee)
  const shippingFee = money(input.shippingFee)
  const discountAmount = money(input.discountAmount)
  return Object.assign({}, input, {
    items,
    baseAmount,
    customFee,
    laborFee,
    shippingFee,
    discountAmount,
    depositRequired: money(input.depositRequired),
    totalAmount: Math.max(0, baseAmount + customFee + laborFee + shippingFee - discountAmount)
  })
}

const REDACTED_LOG_VALUE = '[redacted]'
const SAFE_LOG_TEXT_KEYS = ['_id', 'workspaceId', 'customerId', 'dealId', 'quoteId', 'latestQuoteId', 'linkedCustomerId', 'linkedDealId', 'dealStage', 'quoteStatus', 'confirmStatus', 'taskStatus', 'resultType', 'triggerType', 'actionType', 'priorityLevel', 'urgencyLevel', 'sourceEntry', 'platform', 'createdAt', 'updatedAt', 'sentAt', 'respondedAt', 'validUntil', 'lastContactAt', 'lastPurchaseAt', 'deadlineAt', 'nextFollowupAt', 'archivedAt', 'createdBy', 'updatedBy']

function isSafeLogTextKey(key) {
  return SAFE_LOG_TEXT_KEYS.indexOf(key) >= 0 || /Id$/.test(key || '') || /At$/.test(key || '')
}

function sanitizeLogData(value, key) {
  if (value == null) return value
  if (Array.isArray(value)) return { redactedArrayLength: value.length }
  if (typeof value === 'object') {
    return Object.keys(value).reduce((result, field) => {
      result[field] = sanitizeLogData(value[field], field)
      return result
    }, {})
  }
  if (typeof value === 'string') return isSafeLogTextKey(key) ? value : REDACTED_LOG_VALUE
  return value
}

async function addLog(workspaceId, entityType, entityId, actionType, beforeData, afterData, note, operatorId) {
  await db.collection('activity_logs').add({
    data: { workspaceId, entityType, entityId, actionType, beforeData: sanitizeLogData(beforeData), afterData: sanitizeLogData(afterData), note, operatorId, createdAt: now() }
  })
}

async function createQuoteTask(workspaceId, quote, deal, customer) {
  const dedupeKey = deal._id + ':quote_no_reply:' + quote._id
  const existed = await db.collection('followup_tasks').where({ workspaceId, dedupeKey }).limit(1).get()
  if (existed.data && existed.data.length) return null
  const score = 40 + (deal.urgencyLevel === 'high' ? 20 : 0) + ((customer.totalSpent || 0) > 500 ? 15 : 0)
  const task = {
    workspaceId,
    dealId: deal._id,
    customerId: deal.customerId,
    customerName: customer.name || '',
    triggerType: 'quote_no_reply',
    triggerAt: now(),
    dueAt: addHours(24),
    priorityScore: score,
    templateId: '',
    suggestedText: (customer.name || '您好') + '，我刚才那版报价不用急着定；如果想调整预算、材质或数量，我可以直接帮你改一版。',
    taskStatus: 'pending',
    completedAt: '',
    resultType: '',
    dedupeKey,
    createdAt: now(),
    updatedAt: now(),
    createdBy: 'system',
    updatedBy: 'system'
  }
  const added = await db.collection('followup_tasks').add({ data: task })
  return Object.assign({}, task, { _id: added._id })
}

function logInternalError(scope, err) {
  const name = err && err.name ? String(err.name).slice(0, 64) : 'Error'
  console.error(scope, { name })
}

async function handleRequest(event) {
  const auth = requireOperatorId()
  if (!auth.ok) return auth

  event = event || {}
  const oversized = rejectOversizedEvent(event)
  if (oversized) return oversized

  const workspace = resolveWorkspaceId(event)
  if (!workspace.ok) return workspace
  const workspaceId = workspace.workspaceId
  const operatorId = auth.operatorId
  const input = calculate(event.quote || {})
  const action = event.action || 'draft'
  if (!input.dealId) return { ok: false, message: '报价必须关联 dealId' }
  if (!input.items.length && !input.baseAmount) return { ok: false, message: '报价项或基础金额不能为空' }

  const deal = (await db.collection('deals').doc(input.dealId).get()).data
  if (!deal || deal.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: 'deal 不存在。' }
  const customerDoc = (await db.collection('customers').doc(deal.customerId).get()).data || {}
  const customer = customerDoc.workspaceId === workspaceId ? customerDoc : {}
  const quote = Object.assign({
    workspaceId,
    quoteNo: quoteNo(),
    quoteStatus: 'draft',
    validUntil: '',
    quoteNote: '',
    sentAt: '',
    respondedAt: '',
    createdBy: operatorId,
    updatedBy: operatorId
  }, input, {
    workspaceId,
    updatedAt: now(),
    updatedBy: operatorId
  })

  if (action === 'send') {
    quote.quoteStatus = 'sent'
    quote.sentAt = quote.sentAt || now()
  }
  if (action === 'status') {
    quote.respondedAt = now()
  }

  let before = {}
  if (quote._id) {
    before = (await db.collection('quotes').doc(quote._id).get()).data || {}
    if (before.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: '报价不存在。' }
    const id = quote._id
    delete quote._id
    delete quote.createdAt
    delete quote.createdBy
    await db.collection('quotes').doc(id).update({ data: quote })
    quote._id = id
  } else {
    quote.createdAt = now()
    const added = await db.collection('quotes').add({ data: quote })
    quote._id = added._id
  }

  await db.collection('deals').doc(input.dealId).update({ data: { latestQuoteId: quote._id, updatedAt: now(), updatedBy: operatorId } })
  let task = null
  if (action === 'send') {
    if (deal.dealStage === 'quote_preparing') {
      await db.collection('deals').doc(input.dealId).update({ data: { dealStage: 'quote_sent', updatedAt: now(), updatedBy: operatorId } })
      await addLog(workspaceId, 'deal', input.dealId, 'stage_update', { dealStage: deal.dealStage }, { dealStage: 'quote_sent' }, '报价发送自动推进', operatorId)
    }
    task = await createQuoteTask(workspaceId, quote, Object.assign({}, deal, { _id: input.dealId }), customer)
  }

  await addLog(workspaceId, 'quote', quote._id, action === 'send' ? 'quote_sent' : 'quote_save', before, quote, action === 'send' ? '报价发送' : '报价保存', operatorId)
  return { ok: true, quote, task }
}

exports.main = async (event) => {
  try {
    return await handleRequest(event)
  } catch (err) {
    logInternalError('quoteCreateOrUpdate failed', err)
    return { ok: false, code: 'internal_error', message: '报价保存失败。' }
  }
}
