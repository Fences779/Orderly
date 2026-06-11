const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const ALLOWED_OPENIDS_ENV_NAME = 'ORDERLY_ALLOWED_OPENIDS'
const OPENID_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_OPENID_WORKSPACE_IDS'
const MAX_EVENT_BYTES = 65536
const MAX_WORKSPACE_ID_LENGTH = 128
const MAX_DOC_ID_LENGTH = 128
const MAX_QUOTE_ITEMS = 50
const MAX_SHORT_TEXT_LENGTH = 128
const MAX_NOTE_TEXT_LENGTH = 512
const MAX_MONEY_AMOUNT = 100000000
const MAX_TOTAL_AMOUNT = 100000000
const MAX_QUANTITY = 1000000
const ACTIONS = ['draft', 'send', 'status']
const QUOTE_STATUSES = ['draft', 'sent', 'accepted', 'rejected']
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
  return allowed.indexOf(operatorId) >= 0
}

function rejectOversizedEvent(event) {
  const bytes = Buffer.byteLength(JSON.stringify(event || {}), 'utf8')
  return bytes > MAX_EVENT_BYTES ? { ok: false, code: 'payload_too_large', message: '请求体过大。' } : null
}

function rejectPollutedEvent(event) {
  return hasUnsafeObjectKey(event, 0) ? { ok: false, code: 'invalid_payload', message: '请求参数非法。' } : null
}

function hasUnsafeObjectKey(value, depth) {
  if (depth > 32) return true
  if (value == null || typeof value !== 'object') return false
  return Object.keys(value).some((key) => {
    if (key === '__proto__' || key === 'constructor' || key === 'prototype') return true
    return hasUnsafeObjectKey(value[key], depth + 1)
  })
}

function normalizeList(value) {
  return String(value || '')
    .split(/[,\s，、;；]+/)
    .map((item) => item.trim())
    .filter(Boolean)
}

function hasTextValue(value) {
  return value != null && typeof value !== 'object' && String(value).trim() !== ''
}

function normalizeText(value) {
  if (value == null || typeof value === 'object') return ''
  return String(value).replace(/[\u0000-\u001f\u007f]/g, ' ').trim()
}

function limitText(value, maxLength) {
  return normalizeText(value).slice(0, maxLength)
}

function normalizeWorkspaceId(value) {
  if (value == null || typeof value === 'object') return ''
  const id = String(value).replace(/[\u0000-\u001f\u007f]/g, '').trim()
  return id.length <= MAX_WORKSPACE_ID_LENGTH && /^[A-Za-z0-9_.:-]+$/.test(id) ? id : ''
}

function normalizeWorkspaceList(value) {
  return normalizeList(value).map(normalizeWorkspaceId).filter(Boolean)
}

function normalizeDocId(value) {
  if (value == null || typeof value === 'object') return ''
  const id = String(value).replace(/[\u0000-\u001f\u007f]/g, '').trim()
  return id.length <= MAX_DOC_ID_LENGTH && /^[A-Za-z0-9_-]+$/.test(id) ? id : ''
}

function normalizeAction(value) {
  const action = normalizeText(value || 'draft')
  return ACTIONS.indexOf(action) >= 0 ? action : ''
}

function normalizeQuoteStatus(value) {
  const status = normalizeText(value)
  return QUOTE_STATUSES.indexOf(status) >= 0 ? status : ''
}

function pickFields(source, allowedFields) {
  const result = {}
  const input = source || {}
  allowedFields.forEach((field) => {
    if (Object.prototype.hasOwnProperty.call(input, field)) result[field] = input[field]
  })
  return result
}

function resolveWorkspaceId(event, operatorId) {
  const rawWorkspaceId = event && event.workspaceId
  const requestedWorkspaceId = rawWorkspaceId == null ? '' : String(rawWorkspaceId).trim()
  const workspaceId = requestedWorkspaceId ? normalizeWorkspaceId(rawWorkspaceId) : DEFAULT_WORKSPACE_ID
  if (!workspaceId) return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  const configured = normalizeWorkspaceList(process.env[ALLOWED_WORKSPACE_IDS_ENV_NAME])
  const allowed = Array.from(new Set(configured.length ? configured : [DEFAULT_WORKSPACE_ID]))
  if (allowed.indexOf(workspaceId) < 0) return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  const binding = validateWorkspaceBinding(operatorId, workspaceId, allowed)
  if (!binding.ok) return binding
  return { ok: true, workspaceId }
}

function validateWorkspaceBinding(operatorId, workspaceId, allowedWorkspaceIds) {
  const boundWorkspaceIds = resolveOperatorWorkspaceIds(operatorId)
  if (boundWorkspaceIds.length > 0) {
    if (boundWorkspaceIds.indexOf('*') >= 0 || boundWorkspaceIds.indexOf(workspaceId) >= 0) return { ok: true }
    return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  }

  return { ok: false, code: 'workspace_binding_required', message: '工作区权限未配置。' }
}

function resolveOperatorWorkspaceIds(operatorId) {
  if (!operatorId) return []
  const raw = String(process.env[OPENID_WORKSPACE_IDS_ENV_NAME] || '').trim()
  if (!raw) return []

  if (raw[0] === '{') {
    try {
      const parsed = JSON.parse(raw)
      return normalizeWorkspaceBindingValue(parsed && parsed[operatorId])
    } catch (err) {
      return []
    }
  }

  const entries = raw.split(/[;；\r\n]+/).map((item) => item.trim()).filter(Boolean)
  for (const entry of entries) {
    const separatorIndex = entry.indexOf('=') >= 0 ? entry.indexOf('=') : entry.indexOf(':')
    if (separatorIndex <= 0) continue
    const key = entry.slice(0, separatorIndex).trim()
    if (key === operatorId) return normalizeWorkspaceBindingValue(entry.slice(separatorIndex + 1))
  }

  return []
}

function normalizeWorkspaceBindingValue(value) {
  if (!value) return []
  const values = Array.isArray(value)
    ? value.map((item) => String(item).trim())
    : String(value).split(/[,\s，、|/]+/).map((item) => item.trim())
  return values
    .map((item) => (item === '*' ? '*' : normalizeWorkspaceId(item)))
    .filter(Boolean)
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
  if (!Number.isFinite(num) || num < 0) return 0
  return Math.min(num, MAX_MONEY_AMOUNT)
}

function quantity(value) {
  const num = Number(value || 0)
  if (!Number.isFinite(num) || num < 0) return 0
  return Math.min(num, MAX_QUANTITY)
}

function normalizeQuoteItems(value) {
  const items = Array.isArray(value) ? value.slice(0, MAX_QUOTE_ITEMS) : []
  return items.map((item) => {
    const input = pickFields(item, QUOTE_ITEM_FIELDS)
    return {
      name: limitText(input.name, MAX_SHORT_TEXT_LENGTH),
      qty: quantity(input.qty || 1),
      price: money(input.price),
      note: limitText(input.note, MAX_NOTE_TEXT_LENGTH),
      skuId: normalizeDocId(input.skuId),
      materialCode: limitText(input.materialCode, MAX_SHORT_TEXT_LENGTH),
      unit: limitText(input.unit, 32)
    }
  }).filter((item) => item.name || item.price > 0)
}

async function hasWorkspaceDoc(collection, id, workspaceId) {
  const docId = normalizeDocId(id)
  if (!docId) return false
  try {
    const doc = (await db.collection(collection).doc(docId).get()).data
    return !!doc && doc.workspaceId === workspaceId
  } catch (err) {
    return false
  }
}

async function validateQuoteItemSkuIds(workspaceId, items) {
  const seen = Object.create(null)
  for (const item of items || []) {
    if (!item.skuId || seen[item.skuId]) continue
    seen[item.skuId] = true
    if (!(await hasWorkspaceDoc('sku_catalog', item.skuId, workspaceId))) return false
  }
  return true
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
    _id: normalizeDocId(input._id),
    dealId: normalizeDocId(input.dealId),
    quoteNo: limitText(input.quoteNo, MAX_SHORT_TEXT_LENGTH),
    validUntil: limitText(input.validUntil, 64),
    quoteNote: limitText(input.quoteNote, MAX_NOTE_TEXT_LENGTH),
    sentAt: limitText(input.sentAt, 64),
    respondedAt: limitText(input.respondedAt, 64),
    items,
    baseAmount,
    customFee,
    laborFee,
    shippingFee,
    discountAmount,
    quoteStatus: normalizeQuoteStatus(input.quoteStatus) || 'draft',
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
  const dealId = normalizeDocId(deal._id)
  const quoteId = normalizeDocId(quote._id)
  const customerId = normalizeDocId(deal.customerId)
  if (!dealId || !quoteId) return null
  const dedupeKey = dealId + ':quote_no_reply:' + quoteId
  const existed = await db.collection('followup_tasks').where({ workspaceId, dedupeKey }).limit(1).get()
  if (existed.data && existed.data.length) return null
  const score = 40 + (deal.urgencyLevel === 'high' ? 20 : 0) + ((customer.totalSpent || 0) > 500 ? 15 : 0)
  const task = {
    workspaceId,
    dealId,
    customerId,
    customerName: limitText(customer.name, MAX_SHORT_TEXT_LENGTH),
    triggerType: 'quote_no_reply',
    triggerAt: now(),
    dueAt: addHours(24),
    priorityScore: score,
    templateId: '',
    suggestedText: limitText((customer.name || '您好') + '，我刚才那版报价不用急着定；如果想调整预算、材质或数量，我可以直接帮你改一版。', MAX_NOTE_TEXT_LENGTH),
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
  const polluted = rejectPollutedEvent(event)
  if (polluted) return polluted

  const workspace = resolveWorkspaceId(event, auth.operatorId)
  if (!workspace.ok) return workspace
  const workspaceId = workspace.workspaceId
  const operatorId = auth.operatorId
  const action = normalizeAction(event.action || 'draft')
  if (!action) return { ok: false, code: 'invalid_action', message: '非法报价动作。' }

  const rawQuote = event.quote || {}
  if (Array.isArray(rawQuote.items) && rawQuote.items.length > MAX_QUOTE_ITEMS) {
    return { ok: false, code: 'too_many_quote_items', message: `报价项不能超过 ${MAX_QUOTE_ITEMS} 条。` }
  }

  const rawQuoteStatus = normalizeText(rawQuote.quoteStatus)
  if (rawQuoteStatus && !normalizeQuoteStatus(rawQuoteStatus)) {
    return { ok: false, code: 'invalid_quote_status', message: '非法报价状态。' }
  }
  const rawItems = Array.isArray(rawQuote.items) ? rawQuote.items : []
  const invalidRawSku = rawItems.some((item) => hasTextValue(item && item.skuId) && !normalizeDocId(item.skuId))
  if (invalidRawSku) return { ok: false, code: 'invalid_sku_id', message: '非法报价项 SKU ID。' }

  const input = calculate(rawQuote)
  if (rawQuote._id && !input._id) return { ok: false, code: 'invalid_quote_id', message: '非法报价 ID。' }
  if (rawQuote.dealId && !input.dealId) return { ok: false, code: 'invalid_deal_id', message: '非法 dealId。' }
  if (!input.dealId) return { ok: false, message: '报价必须关联 dealId' }
  if (!input.items.length && !input.baseAmount) return { ok: false, message: '报价项或基础金额不能为空' }
  if (input.totalAmount > MAX_TOTAL_AMOUNT) return { ok: false, code: 'invalid_quote_total', message: '报价金额超出允许范围。' }
  if (input.depositRequired > input.totalAmount && input.totalAmount > 0) return { ok: false, code: 'invalid_deposit_amount', message: '定金不能高于报价总额。' }

  let deal = null
  try {
    deal = (await db.collection('deals').doc(input.dealId).get()).data
  } catch (err) {
    return { ok: false, code: 'not_found', message: 'deal 不存在。' }
  }
  if (!deal || deal.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: 'deal 不存在。' }
  if (!(await validateQuoteItemSkuIds(workspaceId, input.items))) {
    return { ok: false, code: 'not_found', message: '报价项 SKU 不存在。' }
  }
  const customerId = normalizeDocId(deal.customerId)
  let customer = {}
  if (customerId) {
    try {
      const customerDoc = (await db.collection('customers').doc(customerId).get()).data || {}
      customer = customerDoc.workspaceId === workspaceId ? customerDoc : {}
    } catch (err) {
      customer = {}
    }
  }
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
    const id = quote._id
    try {
      before = (await db.collection('quotes').doc(id).get()).data || {}
    } catch (err) {
      return { ok: false, code: 'not_found', message: '报价不存在。' }
    }
    if (before.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: '报价不存在。' }
    if (!quote.quoteNo) quote.quoteNo = limitText(before.quoteNo, MAX_SHORT_TEXT_LENGTH) || quoteNo()
    delete quote._id
    delete quote.createdAt
    delete quote.createdBy
    await db.collection('quotes').doc(id).update({ data: quote })
    quote._id = id
  } else {
    if (!quote.quoteNo) quote.quoteNo = quoteNo()
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
