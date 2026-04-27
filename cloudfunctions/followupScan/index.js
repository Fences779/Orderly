const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()

function now() {
  return new Date().toISOString()
}

function addDaysFrom(value, days) {
  const date = value ? new Date(value) : new Date()
  date.setDate(date.getDate() + days)
  return date.toISOString()
}

function hoursSince(value) {
  return (Date.now() - new Date(value || Date.now()).getTime()) / 3600000
}

function normalizeArray(value) {
  if (!value) return []
  if (Array.isArray(value)) return value
  return String(value).split(/[,\s，、/]+/).map((item) => item.trim()).filter(Boolean)
}

function priority(triggerType, deal, customer, dueAt) {
  let score = triggerType === 'quote_no_reply' ? 40 : (triggerType === 'post_delivery' ? 30 : (triggerType === 'repurchase' ? 35 : 28))
  const overdueHours = hoursSince(dueAt)
  if (overdueHours > 0) score += 10
  if (overdueHours > 72) score += 10
  if (deal.urgencyLevel === 'high') score += 20
  if ((customer.totalSpent || 0) >= 500 || (customer.totalOrders || 0) > 1) score += 15
  if (customer.lastContactAt && hoursSince(customer.lastContactAt) < 72) score -= 20
  return Math.max(0, score)
}

async function log(workspaceId, entityType, entityId, actionType, note, operatorId) {
  await db.collection('activity_logs').add({
    data: { workspaceId, entityType, entityId, actionType, beforeData: {}, afterData: {}, note, operatorId, createdAt: now() }
  })
}

async function upsertById(collection, data) {
  if (data._id) {
    const id = data._id
    delete data._id
    data.updatedAt = now()
    await db.collection(collection).doc(id).update({ data })
    return Object.assign({}, data, { _id: id })
  }
  data.createdAt = now()
  data.updatedAt = now()
  const added = await db.collection(collection).add({ data })
  return Object.assign({}, data, { _id: added._id })
}

async function createTaskIfMissing(workspaceId, task) {
  const existed = await db.collection('followup_tasks').where({ workspaceId, dedupeKey: task.dedupeKey }).limit(1).get()
  if (existed.data && existed.data.length) return null
  const added = await db.collection('followup_tasks').add({ data: task })
  return Object.assign({}, task, { _id: added._id })
}

async function taskAction(event, operatorId) {
  const task = (await db.collection('followup_tasks').doc(event.taskId).get()).data
  const patch = { updatedAt: now(), updatedBy: operatorId }
  if (event.action === 'complete') Object.assign(patch, { taskStatus: 'completed', completedAt: now(), resultType: 'done' })
  if (event.action === 'skip') Object.assign(patch, { taskStatus: 'skipped', completedAt: now(), resultType: 'skipped' })
  if (event.action === 'delay') Object.assign(patch, { dueAt: event.payload && event.payload.dueAt ? event.payload.dueAt : addDaysFrom(new Date(), 1) })
  await db.collection('followup_tasks').doc(event.taskId).update({ data: patch })
  if (task) {
    await log(task.workspaceId || event.workspaceId || 'default', 'deal', task.dealId, 'followup_' + event.action, '跟进任务' + event.action, operatorId)
  }
  return { ok: true }
}

exports.main = async (event) => {
  const workspaceId = event.workspaceId || 'default'
  const operatorId = cloud.getWXContext().OPENID || 'anonymous'
  if (event.mode === 'taskAction') return taskAction(event, operatorId)
  if (event.mode === 'manualCreate') {
    const task = Object.assign({
      workspaceId,
      triggerType: 'manual',
      triggerAt: now(),
      dueAt: addDaysFrom(new Date(), 1),
      priorityScore: 28,
      templateId: '',
      taskStatus: 'pending',
      completedAt: '',
      resultType: '',
      createdBy: operatorId,
      updatedBy: operatorId
    }, event.task || {}, {
      workspaceId,
      dedupeKey: 'manual:' + Date.now() + ':' + Math.random().toString(36).slice(2),
      createdAt: now(),
      updatedAt: now()
    })
    const added = await db.collection('followup_tasks').add({ data: task })
    await log(workspaceId, 'deal', task.dealId, 'followup_manual_create', '手动跟进任务创建', operatorId)
    return { ok: true, task: Object.assign({}, task, { _id: added._id }) }
  }
  if (event.mode === 'templateSave') {
    const template = Object.assign({ workspaceId, enabled: true, useCount: 0 }, event.template || {}, { workspaceId })
    const saved = await upsertById('message_templates', template)
    return { ok: true, template: saved }
  }
  if (event.mode === 'templateUse') {
    if (!event.templateId) return { ok: false, message: '缺少 templateId' }
    const _ = db.command
    await db.collection('message_templates').doc(event.templateId).update({ data: { useCount: _.inc(1), updatedAt: now() } })
    return { ok: true }
  }
  if (event.mode === 'skuSave') {
    const rawSku = event.sku || {}
    let baseSku = {}
    if (rawSku._id) {
      try {
        baseSku = (await db.collection('sku_catalog').doc(rawSku._id).get()).data || {}
      } catch (err) {}
    }
    const mergedSku = Object.assign({}, baseSku, rawSku)
    const sku = Object.assign({ workspaceId, enabled: true, sortOrder: 10 }, mergedSku, {
      workspaceId,
      basePrice: Number(mergedSku.basePrice || 0),
      costPrice: Number(mergedSku.costPrice || 0),
      tags: normalizeArray(mergedSku.tags),
      adjustableFields: normalizeArray(mergedSku.adjustableFields)
    })
    const saved = await upsertById('sku_catalog', sku)
    return { ok: true, sku: saved }
  }

  const deals = (await db.collection('deals').where({ workspaceId }).limit(1000).get()).data || []
  const customers = (await db.collection('customers').where({ workspaceId }).limit(1000).get()).data || []
  const customerMap = {}
  customers.forEach((customer) => { customerMap[customer._id] = customer })
  let created = 0

  for (const deal of deals) {
    const customer = customerMap[deal.customerId] || {}
    if (deal.dealStage === 'quote_sent' && deal.latestQuoteId) {
      const quote = (await db.collection('quotes').doc(deal.latestQuoteId).get().catch(() => ({ data: null }))).data
      if (quote && quote.sentAt && hoursSince(quote.sentAt) >= 24) {
        const dueAt = addDaysFrom(quote.sentAt, 1)
        const task = {
          workspaceId,
          dealId: deal._id,
          customerId: deal.customerId,
          customerName: customer.name || '',
          triggerType: 'quote_no_reply',
          triggerAt: quote.sentAt,
          dueAt,
          priorityScore: priority('quote_no_reply', deal, customer, dueAt),
          templateId: '',
          suggestedText: (customer.name || '您好') + '，之前那版报价不用急着定；如果想调整预算、材质或数量，我可以直接帮你改一版。',
          taskStatus: 'pending',
          completedAt: '',
          resultType: '',
          dedupeKey: deal._id + ':quote_no_reply:' + quote._id,
          createdAt: now(),
          updatedAt: now(),
          createdBy: 'system',
          updatedBy: 'system'
        }
        if (await createTaskIfMissing(workspaceId, task)) created += 1
      }
    }
    if (deal.dealStage === 'received') {
      const dueAt = addDaysFrom(deal.updatedAt, 3)
      const task = {
        workspaceId,
        dealId: deal._id,
        customerId: deal.customerId,
        customerName: customer.name || '',
        triggerType: 'post_delivery',
        triggerAt: deal.updatedAt,
        dueAt,
        priorityScore: priority('post_delivery', deal, customer, dueAt),
        templateId: '',
        suggestedText: (customer.name || '您好') + '，收到后佩戴感觉怎么样？如果尺寸或颜色有偏差，我可以帮你看怎么调整。',
        taskStatus: 'pending',
        completedAt: '',
        resultType: '',
        dedupeKey: deal._id + ':post_delivery:' + dueAt.slice(0, 10),
        createdAt: now(),
        updatedAt: now(),
        createdBy: 'system',
        updatedBy: 'system'
      }
      if (await createTaskIfMissing(workspaceId, task)) created += 1
    }
    if (deal.dealStage === 'completed' || deal.dealStage === 'repurchase_due') {
      const dueAt = addDaysFrom(deal.lastInteractionAt || deal.updatedAt, 30)
      const task = {
        workspaceId,
        dealId: deal._id,
        customerId: deal.customerId,
        customerName: customer.name || '',
        triggerType: 'repurchase',
        triggerAt: deal.updatedAt,
        dueAt,
        priorityScore: priority('repurchase', deal, customer, dueAt),
        templateId: '',
        suggestedText: (customer.name || '您好') + '，上次那款风格最近有新材料，如果想做同风格升级版，我可以按历史档案给你配。',
        taskStatus: 'pending',
        completedAt: '',
        resultType: '',
        dedupeKey: deal._id + ':repurchase:' + dueAt.slice(0, 10),
        createdAt: now(),
        updatedAt: now(),
        createdBy: 'system',
        updatedBy: 'system'
      }
      if (await createTaskIfMissing(workspaceId, task)) created += 1
    }
  }

  return { ok: true, created }
}
