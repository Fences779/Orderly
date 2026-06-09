const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()

function now() {
  return new Date().toISOString()
}

function requireOperatorId() {
  const operatorId = cloud.getWXContext().OPENID || ''
  if (!operatorId) return { ok: false, code: 'unauthorized', message: '未授权调用。' }
  return { ok: true, operatorId }
}

function normalizeArray(value) {
  if (!value) return []
  if (Array.isArray(value)) return value
  return String(value).split(/[,\s，、/]+/).map((item) => item.trim()).filter(Boolean)
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
  const workspaceId = event.workspaceId || 'default'
  const operatorId = auth.operatorId
  const input = event.customer || {}
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
