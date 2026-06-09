const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const ALLOWED_OPENIDS_ENV_NAME = 'ORDERLY_ALLOWED_OPENIDS'
const MAX_EVENT_BYTES = 65536
const CUSTOMER_FIELDS = ['_id', 'name', 'platform', 'externalUid', 'contactHandle', 'sourceChannel', 'profileTags', 'preferenceNotes', 'tabooNotes', 'riskNotes', 'totalOrders', 'totalSpent', 'lastContactAt', 'lastPurchaseAt']

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
  return allowed.length === 0 || allowed.indexOf(operatorId) >= 0
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
  const configured = normalizeArray(process.env[ALLOWED_WORKSPACE_IDS_ENV_NAME])
  const allowed = configured.length ? configured : [DEFAULT_WORKSPACE_ID]
  if (allowed.indexOf(workspaceId) < 0) return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  return { ok: true, workspaceId }
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

async function log(workspaceId, entityId, actionType, beforeData, afterData, note, operatorId) {
  await db.collection('activity_logs').add({
    data: { workspaceId, entityType: 'customer', entityId, actionType, beforeData: sanitizeLogData(beforeData), afterData: sanitizeLogData(afterData), note, operatorId, createdAt: now() }
  })
}

exports.main = async (event) => {
  const auth = requireOperatorId()
  if (!auth.ok) return auth

  event = event || {}
  const oversized = rejectOversizedEvent(event)
  if (oversized) return oversized

  const workspace = resolveWorkspaceId(event)
  if (!workspace.ok) return workspace
  const workspaceId = workspace.workspaceId
  const operatorId = auth.operatorId
  const input = pickFields(event.customer, CUSTOMER_FIELDS)
  if (!input.name) return { ok: false, message: '客户名不能为空' }
  const data = Object.assign({
    workspaceId,
    platform: 'wechat',
    externalUid: '',
    contactHandle: '',
    sourceChannel: '',
    profileTags: [],
    preferenceNotes: '',
    tabooNotes: '',
    riskNotes: '',
    totalOrders: 0,
    totalSpent: 0,
    lastContactAt: now(),
    lastPurchaseAt: '',
    createdBy: operatorId,
    updatedBy: operatorId
  }, input, {
    workspaceId,
    profileTags: normalizeArray(input.profileTags),
    updatedAt: now(),
    updatedBy: operatorId
  })

  let existing = null
  if (input._id) {
    try {
      existing = (await db.collection('customers').doc(input._id).get()).data
    } catch (err) {}
    if (existing && existing.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: '客户不存在。' }
  }
  if (!existing && input.platform && input.externalUid) {
    const matched = await db.collection('customers').where({ workspaceId, platform: input.platform, externalUid: input.externalUid }).limit(1).get()
    existing = matched.data && matched.data[0]
  }
  if (!existing && input.contactHandle) {
    const matched = await db.collection('customers').where({ workspaceId, contactHandle: input.contactHandle }).limit(1).get()
    existing = matched.data && matched.data[0]
  }

  if (existing) {
    const id = existing._id
    delete data._id
    delete data.createdAt
    delete data.createdBy
    await db.collection('customers').doc(id).update({ data })
    const customer = Object.assign({}, existing, data, { _id: id })
    await log(workspaceId, id, 'customer_update', existing, customer, '客户档案更新', operatorId)
    return { ok: true, customer }
  }

  data.createdAt = now()
  const added = await db.collection('customers').add({ data })
  const customer = Object.assign({}, data, { _id: added._id })
  await log(workspaceId, added._id, 'customer_create', {}, customer, '客户建档', operatorId)
  return { ok: true, customer }
}
