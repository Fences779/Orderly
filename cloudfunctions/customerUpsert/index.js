const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()

function now() {
  return new Date().toISOString()
}

function normalizeArray(value) {
  if (!value) return []
  if (Array.isArray(value)) return value
  return String(value).split(/[,\s，、/]+/).map((item) => item.trim()).filter(Boolean)
}

async function log(workspaceId, entityId, actionType, beforeData, afterData, note, operatorId) {
  await db.collection('activity_logs').add({
    data: { workspaceId, entityType: 'customer', entityId, actionType, beforeData, afterData, note, operatorId, createdAt: now() }
  })
}

exports.main = async (event) => {
  const workspaceId = event.workspaceId || 'default'
  const operatorId = cloud.getWXContext().OPENID || 'anonymous'
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
