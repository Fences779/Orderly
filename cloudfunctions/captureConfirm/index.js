const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const ALLOWED_OPENIDS_ENV_NAME = 'ORDERLY_ALLOWED_OPENIDS'
const AUTH_ALLOW_ALL_DEV_ENV_NAME = 'ORDERLY_AUTH_ALLOW_ALL_DEV'
const MAX_EVENT_BYTES = 65536

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
  const allowed = normalizeArray(process.env[ALLOWED_OPENIDS_ENV_NAME])
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

function normalizeArray(value) {
  if (!value) return []
  if (Array.isArray(value)) return value
  return String(value).split(/[,\s，、/]+/).map((item) => item.trim()).filter(Boolean)
}

function resolveWorkspaceId(event) {
  const workspaceId = String((event && event.workspaceId) || '').trim() || DEFAULT_WORKSPACE_ID
  const configured = normalizeArray(process.env[ALLOWED_WORKSPACE_IDS_ENV_NAME])
  const allowed = configured.length ? configured : [DEFAULT_WORKSPACE_ID]
  if (allowed.indexOf(workspaceId) < 0) return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  return { ok: true, workspaceId }
}

function money(value) {
  const num = Number(value || 0)
  return Number.isFinite(num) && num >= 0 ? num : 0
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

async function createCustomer(workspaceId, form, operatorId) {
  const customer = {
    workspaceId,
    name: form.customerName,
    platform: form.platform || 'wechat',
    externalUid: '',
    contactHandle: form.contactHandle || '',
    sourceChannel: 'capture',
    profileTags: [],
    preferenceNotes: '',
    tabooNotes: '',
    riskNotes: '',
    totalOrders: 0,
    totalSpent: 0,
    lastContactAt: now(),
    lastPurchaseAt: '',
    createdAt: now(),
    updatedAt: now(),
    createdBy: operatorId,
    updatedBy: operatorId
  }
  const added = await db.collection('customers').add({ data: customer })
  const full = Object.assign({}, customer, { _id: added._id })
  await addLog(workspaceId, 'customer', added._id, 'customer_create', {}, full, 'capture 确认建档', operatorId)
  return full
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
  const form = event.form || {}
  if (!event.captureId) return { ok: false, message: '缺少 captureId' }
  if (!form.customerName || !form.demandSummary) return { ok: false, message: '客户名和需求摘要必填' }

  const capture = (await db.collection('captures').doc(event.captureId).get()).data
  if (!capture || capture.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: 'capture 不存在。' }
  if (capture.confirmStatus === 'confirmed' && capture.linkedDealId) {
    return { ok: true, customerId: capture.linkedCustomerId, dealId: capture.linkedDealId, message: 'capture 已确认' }
  }

  let customer
  if (event.customerMode === 'existing' && event.selectedCustomerId) {
    customer = (await db.collection('customers').doc(event.selectedCustomerId).get()).data
    if (!customer || customer.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: '客户不存在。' }
    await db.collection('customers').doc(event.selectedCustomerId).update({ data: { lastContactAt: now(), updatedAt: now(), updatedBy: operatorId } })
  } else {
    customer = await createCustomer(workspaceId, form, operatorId)
  }

  const deal = {
    workspaceId,
    customerId: customer._id,
    title: form.title || form.demandSummary.slice(0, 24),
    sourceEntry: form.platform || customer.platform || 'wechat',
    dealStage: form.dealStage || 'new_inquiry',
    priorityLevel: form.urgencyLevel === 'high' ? 'high' : 'medium',
    intentCategory: form.intentCategory || '',
    demandSummary: form.demandSummary,
    styleTags: normalizeArray(form.styleTags),
    materialTags: normalizeArray(form.materialTags),
    sizeSpec: form.sizeSpec || '',
    colorPref: form.colorPref || '',
    budgetMin: money(form.budgetMin),
    budgetMax: money(form.budgetMax),
    deadlineAt: form.deadlineAt || '',
    urgencyLevel: form.urgencyLevel || 'low',
    riskFlags: normalizeArray(form.riskFlags),
    latestQuoteId: '',
    nextFollowupAt: '',
    lastInteractionAt: now(),
    followupCount: 0,
    lossReason: '',
    archivedAt: '',
    createdAt: now(),
    updatedAt: now(),
    createdBy: operatorId,
    updatedBy: operatorId
  }
  const addedDeal = await db.collection('deals').add({ data: deal })
  const dealId = addedDeal._id
  await db.collection('captures').doc(event.captureId).update({
    data: {
      parserResult: capture.parserResult,
      confirmStatus: 'confirmed',
      linkedCustomerId: customer._id,
      linkedDealId: dealId,
      updatedAt: now()
    }
  })
  await addLog(workspaceId, 'capture', event.captureId, 'capture_confirm', capture, { linkedCustomerId: customer._id, linkedDealId: dealId }, 'capture 确认入库', operatorId)
  await addLog(workspaceId, 'deal', dealId, 'deal_create', {}, Object.assign({}, deal, { _id: dealId }), 'capture 确认创建 deal', operatorId)

  return { ok: true, customerId: customer._id, dealId }
}

exports.main = async (event) => {
  try {
    return await handleRequest(event)
  } catch (err) {
    logInternalError('captureConfirm failed', err)
    return { ok: false, code: 'internal_error', message: '入库确认失败。' }
  }
}
