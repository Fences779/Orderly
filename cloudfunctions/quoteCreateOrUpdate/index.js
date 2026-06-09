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

function calculate(input) {
  const items = Array.isArray(input.items) ? input.items : []
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

async function addLog(workspaceId, entityType, entityId, actionType, beforeData, afterData, note, operatorId) {
  await db.collection('activity_logs').add({
    data: { workspaceId, entityType, entityId, actionType, beforeData, afterData, note, operatorId, createdAt: now() }
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

exports.main = async (event) => {
  const auth = requireOperatorId()
  if (!auth.ok) return auth

  event = event || {}
  const workspaceId = event.workspaceId || 'default'
  const operatorId = auth.operatorId
  const input = calculate(event.quote || {})
  const action = event.action || 'draft'
  if (!input.dealId) return { ok: false, message: '报价必须关联 dealId' }
  if (!input.items.length && !input.baseAmount) return { ok: false, message: '报价项或基础金额不能为空' }

  const deal = (await db.collection('deals').doc(input.dealId).get()).data
  if (!deal) return { ok: false, message: 'deal 不存在' }
  const customer = (await db.collection('customers').doc(deal.customerId).get()).data || {}
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
