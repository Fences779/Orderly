const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()

const TRANSITIONS = {
  new_inquiry: ['needs_clarification', 'quote_preparing', 'lost'],
  needs_clarification: ['quote_preparing', 'dormant', 'lost'],
  quote_preparing: ['quote_sent'],
  quote_sent: ['waiting_deposit', 'quote_preparing', 'dormant', 'lost'],
  waiting_deposit: ['scheduled', 'lost'],
  scheduled: ['in_production'],
  in_production: ['ready_to_ship'],
  ready_to_ship: ['shipped'],
  shipped: ['received'],
  received: ['completed'],
  completed: ['repurchase_due', 'dormant'],
  repurchase_due: [],
  dormant: ['new_inquiry', 'needs_clarification', 'quote_preparing'],
  lost: []
}

function now() {
  return new Date().toISOString()
}

function requireOperatorId() {
  const operatorId = cloud.getWXContext().OPENID || ''
  if (!operatorId) return { ok: false, code: 'unauthorized', message: '未授权调用。' }
  return { ok: true, operatorId }
}

function addDays(days) {
  const date = new Date()
  date.setDate(date.getDate() + days)
  return date.toISOString()
}

function canTransition(fromStage, toStage) {
  if (fromStage === toStage) return true
  return (TRANSITIONS[fromStage] || []).indexOf(toStage) >= 0
}

function priority(triggerType, deal, customer) {
  let score = triggerType === 'post_delivery' ? 30 : 35
  if (deal.urgencyLevel === 'high') score += 20
  if ((customer.totalSpent || 0) >= 500 || (customer.totalOrders || 0) > 1) score += 15
  return score
}

async function addLog(workspaceId, entityId, beforeData, afterData, note, operatorId) {
  await db.collection('activity_logs').add({
    data: { workspaceId, entityType: 'deal', entityId, actionType: 'stage_update', beforeData, afterData, note, operatorId, createdAt: now() }
  })
}

async function createTaskIfMissing(workspaceId, deal, customer, triggerType, dueAt, suggestedText) {
  const dedupeKey = deal._id + ':' + triggerType + ':' + dueAt.slice(0, 10)
  const existed = await db.collection('followup_tasks').where({ workspaceId, dedupeKey }).limit(1).get()
  if (existed.data && existed.data.length) return null
  const task = {
    workspaceId,
    dealId: deal._id,
    customerId: deal.customerId,
    customerName: customer.name || '',
    triggerType,
    triggerAt: now(),
    dueAt,
    priorityScore: priority(triggerType, deal, customer),
    templateId: '',
    suggestedText,
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

exports.main = async (event) => {
  const auth = requireOperatorId()
  if (!auth.ok) return auth

  event = event || {}
  const workspaceId = event.workspaceId || 'default'
  const operatorId = auth.operatorId
  const { dealId, toStage } = event
  if (!dealId || !toStage) return { ok: false, message: '缺少 dealId 或 toStage' }
  const deal = (await db.collection('deals').doc(dealId).get()).data
  if (!deal) return { ok: false, message: 'deal 不存在' }
  if (!canTransition(deal.dealStage, toStage)) return { ok: false, message: '非法状态流转：' + deal.dealStage + ' -> ' + toStage }

  const before = { dealStage: deal.dealStage, nextFollowupAt: deal.nextFollowupAt }
  const patch = { dealStage: toStage, updatedAt: now(), updatedBy: operatorId, lastInteractionAt: now() }
  if (toStage === 'lost') patch.lossReason = event.lossReason || deal.lossReason || '未填写'
  if (toStage === 'received') patch.nextFollowupAt = addDays(3)
  if (toStage === 'completed') patch.nextFollowupAt = addDays(30)
  await db.collection('deals').doc(dealId).update({ data: patch })

  if (deal.dealStage === 'quote_sent' && toStage !== 'quote_sent') {
    const tasks = await db.collection('followup_tasks').where({ workspaceId, dealId, triggerType: 'quote_no_reply', taskStatus: 'pending' }).get()
    for (const task of tasks.data || []) {
      await db.collection('followup_tasks').doc(task._id).update({ data: { taskStatus: 'completed', completedAt: now(), resultType: 'auto_closed', updatedAt: now() } })
    }
  }

  const customer = (await db.collection('customers').doc(deal.customerId).get()).data || {}
  const updatedDeal = Object.assign({}, deal, patch)
  let task = null
  if (toStage === 'received') {
    task = await createTaskIfMissing(workspaceId, updatedDeal, customer, 'post_delivery', addDays(3), (customer.name || '您好') + '，收到后佩戴感觉怎么样？尺寸、颜色如果有偏差可以直接告诉我。')
  }
  if (toStage === 'completed') {
    task = await createTaskIfMissing(workspaceId, updatedDeal, customer, 'repurchase', addDays(30), (customer.name || '您好') + '，上次这单已经完成一段时间了，如果想做同风格升级版，我可以按历史档案直接给你配。')
  }

  await addLog(workspaceId, dealId, before, { dealStage: toStage }, event.note || '状态变更', operatorId)
  return { ok: true, deal: updatedDeal, task }
}
