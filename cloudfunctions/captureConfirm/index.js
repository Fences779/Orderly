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

function money(value) {
  const num = Number(value || 0)
  return Number.isFinite(num) && num >= 0 ? num : 0
}

async function addLog(workspaceId, entityType, entityId, actionType, beforeData, afterData, note, operatorId) {
  await db.collection('activity_logs').add({
    data: { workspaceId, entityType, entityId, actionType, beforeData, afterData, note, operatorId, createdAt: now() }
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

exports.main = async (event) => {
  const auth = requireOperatorId()
  if (!auth.ok) return auth

  event = event || {}
  const workspaceId = event.workspaceId || 'default'
  const operatorId = auth.operatorId
  const form = event.form || {}
  if (!event.captureId) return { ok: false, message: '缺少 captureId' }
  if (!form.customerName || !form.demandSummary) return { ok: false, message: '客户名和需求摘要必填' }

  const capture = (await db.collection('captures').doc(event.captureId).get()).data
  if (!capture) return { ok: false, message: 'capture 不存在' }
  if (capture.confirmStatus === 'confirmed' && capture.linkedDealId) {
    return { ok: true, customerId: capture.linkedCustomerId, dealId: capture.linkedDealId, message: 'capture 已确认' }
  }

  let customer
  if (event.customerMode === 'existing' && event.selectedCustomerId) {
    customer = (await db.collection('customers').doc(event.selectedCustomerId).get()).data
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
