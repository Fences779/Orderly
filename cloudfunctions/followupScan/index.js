const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const AUTO_SCAN_ENV_NAME = 'ORDERLY_ENABLE_FOLLOWUP_AUTO_SCAN'
const USER_AUTH_MODES = ['inventoryManagementDashboard', 'cashflowHealthDashboard', 'taskAction', 'manualCreate', 'templateSave', 'templateUse', 'skuSave', 'inventoryMovementSave', 'cashflowSave']

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

function normalizeNumber(value) {
  const number = Number(value || 0)
  return Number.isFinite(number) ? number : 0
}

function normalizeText(value) {
  return value == null ? '' : String(value).trim()
}

function requireOperatorId() {
  const operatorId = cloud.getWXContext().OPENID || ''
  if (!operatorId) return { ok: false, code: 'unauthorized', message: '未授权调用。' }
  return { ok: true, operatorId }
}

function resolveWorkspaceId(event) {
  const workspaceId = normalizeText(event && event.workspaceId) || DEFAULT_WORKSPACE_ID
  const configured = normalizeArray(process.env[ALLOWED_WORKSPACE_IDS_ENV_NAME])
  const allowed = configured.length ? configured : [DEFAULT_WORKSPACE_ID]
  if (allowed.indexOf(workspaceId) < 0) return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  return { ok: true, workspaceId }
}

function isAutoScanEnabled() {
  return process.env[AUTO_SCAN_ENV_NAME] === '1'
}

function normalizeDirection(value) {
  const direction = normalizeText(value).toLowerCase()
  return direction === 'expense' ? 'expense' : 'income'
}

async function safeList(collection, where) {
  try {
    return (await db.collection(collection).where(where).limit(1000).get()).data || []
  } catch (err) {
    return []
  }
}

function dateKey(value) {
  const date = value ? new Date(value) : new Date()
  if (Number.isNaN(date.getTime())) return ''
  return date.toISOString().slice(0, 10)
}

function timestamp(value) {
  const date = value ? new Date(value) : new Date()
  return Number.isNaN(date.getTime()) ? 0 : date.getTime()
}

function buildAvailability(status, sourceType, reason) {
  return { status, sourceType, reason }
}

function buildInventoryStatus(sku) {
  if (sku.enabled === false) return { status: 'disabled', label: '停用' }
  const stock = normalizeNumber(sku.stockOnHand)
  const reserved = normalizeNumber(sku.stockReserved)
  const safety = normalizeNumber(sku.safetyStock)
  if (safety > 0 && stock - reserved <= safety) return { status: 'low_stock', label: '低库存' }
  return { status: 'normal', label: '正常' }
}

function compareValue(a, b, sortBy, direction) {
  const left = a[sortBy]
  const right = b[sortBy]
  const result = typeof left === 'string' || typeof right === 'string'
    ? String(left || '').localeCompare(String(right || ''), 'zh-Hans-CN')
    : normalizeNumber(left) - normalizeNumber(right)
  return direction === 'asc' ? result : -result
}

async function inventoryManagementDashboard(event, workspaceId) {
  const request = event.payload || event
  const page = Math.max(1, Number(request.page || 1))
  const pageSize = Math.max(1, Number(request.pageSize || 10))
  const keyword = normalizeText(request.keyword).toLowerCase()
  const category = normalizeText(request.category)
  const status = normalizeText(request.status || 'all')
  const sortBy = normalizeText(request.sortBy || 'sold30dRatio')
  const sortDirection = normalizeText(request.sortDirection || 'desc') === 'asc' ? 'asc' : 'desc'
  const skus = await safeList('sku_catalog', { workspaceId })
  const movements = await safeList('inventory_movements', { workspaceId })

  const movementTotals = movements.reduce((map, movement) => {
    const skuId = normalizeText(movement.skuId)
    if (!skuId) return map
    const occurredAt = timestamp(movement.occurredAt || movement.createdAt)
    const ageDays = (Date.now() - occurredAt) / 86400000
    const quantity = Math.abs(normalizeNumber(movement.quantity))
    const bucket = map[skuId] || { consumed7dQty: 0, consumed30dQty: 0 }
    if (ageDays <= 7) bucket.consumed7dQty += quantity
    if (ageDays <= 30) bucket.consumed30dQty += quantity
    map[skuId] = bucket
    return map
  }, {})

  let items = skus.map((sku) => {
    const skuStatus = buildInventoryStatus(sku)
    const materialId = normalizeText(sku._id || sku.skuId || sku.id)
    const totals = movementTotals[materialId] || { consumed7dQty: 0, consumed30dQty: 0 }
    const currentStockQty = normalizeNumber(sku.stockOnHand) - normalizeNumber(sku.stockReserved)
    const sold7dQty = totals.consumed7dQty
    const sold30dQty = totals.consumed30dQty
    return {
      materialId,
      materialName: normalizeText(sku.name || sku.title),
      category: normalizeText(sku.category),
      currentStockQty,
      stockUnit: normalizeText(sku.stockUnit || sku.unit || '件'),
      sold7dQty,
      sold7dRatio: currentStockQty > 0 ? sold7dQty / currentStockQty : 0,
      sold30dQty,
      sold30dRatio: currentStockQty > 0 ? sold30dQty / currentStockQty : 0,
      consumed7dQty: totals.consumed7dQty,
      consumed30dQty: totals.consumed30dQty,
      safeStockSuggestedQty: normalizeNumber(sku.safetyStock),
      status: skuStatus.status,
      statusLabel: skuStatus.label,
      unitCost: normalizeNumber(sku.purchasePrice) || normalizeNumber(sku.costPrice),
      lastRestockedAt: timestamp(sku.lastRestockedAt),
      supplierName: normalizeText(sku.supplierName || sku.supplier),
      remark: normalizeText(sku.inventoryRemark || sku.remark)
    }
  })

  items = items.filter((item) => {
    if (keyword && (item.materialName + item.category + item.supplierName).toLowerCase().indexOf(keyword) < 0) return false
    if (category && item.category !== category) return false
    if (status && status !== 'all' && item.status !== status) return false
    return true
  }).sort((a, b) => compareValue(a, b, sortBy, sortDirection))

  const total = items.length
  const pagedItems = items.slice((page - 1) * pageSize, page * pageSize)
  const lowStockCount = items.filter((item) => item.status === 'low_stock').length
  const fastSellingCount = items.filter((item) => item.sold30dRatio >= 0.5).length
  const lowSellingCount = items.filter((item) => item.sold30dQty === 0).length
  const avg = (values) => values.length ? values.reduce((sum, value) => sum + value, 0) / values.length : null
  return {
    ok: true,
    updatedAt: Date.now(),
    dataAvailability: {
      inventorySource: buildAvailability(skus.length ? 'available' : 'unavailable', 'sku_catalog', skus.length ? '' : '暂无 SKU 库存数据'),
      materialConsumption: buildAvailability(movements.length ? 'available' : 'compat', 'inventory_movements', movements.length ? '' : '暂无库存消耗流水，动销字段按 0 返回')
    },
    summary: {
      avgOrderMaterialUsage: avg(items.map((item) => item.consumed30dQty).filter((value) => value > 0)),
      avgMaterialUnitCost: avg(items.map((item) => item.unitCost).filter((value) => value > 0)),
      avgBraceletSalePrice: avg(skus.map((sku) => normalizeNumber(sku.basePrice)).filter((value) => value > 0)),
      avgBraceletCostPrice: avg(items.map((item) => item.unitCost).filter((value) => value > 0)),
      grossMarginRate: null,
      lowStockCount,
      fastSellingCount,
      lowSellingCount,
      inventoryHealthStatus: lowStockCount > 0 ? 'warning' : 'healthy',
      inventoryHealthSummary: lowStockCount > 0 ? lowStockCount + ' 个物料低于安全库存' : '库存风险在可控范围内',
      inventoryWarningCount: lowStockCount
    },
    filterOptions: {
      categories: Array.from(new Set(skus.map((sku) => normalizeText(sku.category)).filter(Boolean))),
      statuses: [
        { value: 'all', label: '全部状态' },
        { value: 'fast_selling', label: '动销偏快' },
        { value: 'low_selling', label: '低动销' },
        { value: 'normal', label: '正常' },
        { value: 'low_stock', label: '低库存' },
        { value: 'disabled', label: '停用' }
      ],
      defaultSortBy: 'sold30dRatio',
      defaultSortDirection: 'desc'
    },
    pageInfo: { page, pageSize, total, totalPages: Math.ceil(total / pageSize) },
    items: pagedItems
  }
}

function buildBreakdown(entries, direction) {
  const rows = entries.filter((entry) => entry.direction === direction)
  const totalAmount = rows.reduce((sum, entry) => sum + normalizeNumber(entry.amount), 0)
  const groups = rows.reduce((map, entry) => {
    const category = normalizeText(entry.category) || '未分类'
    map[category] = (map[category] || 0) + normalizeNumber(entry.amount)
    return map
  }, {})
  return {
    totalAmount,
    items: Object.keys(groups).map((category) => ({
      category,
      amount: groups[category],
      percent: totalAmount > 0 ? groups[category] / totalAmount : 0
    }))
  }
}

async function cashflowHealthDashboard(event, workspaceId) {
  const entries = await safeList('cashflow_entries', { workspaceId })
  const incomeBreakdown = buildBreakdown(entries, 'income')
  const expenseBreakdown = buildBreakdown(entries, 'expense')
  const netAmount = incomeBreakdown.totalAmount - expenseBreakdown.totalAmount
  const avgDailyExpense7d = entries
    .filter((entry) => entry.direction === 'expense' && (Date.now() - timestamp(entry.occurredAt || entry.createdAt)) / 86400000 <= 7)
    .reduce((sum, entry) => sum + normalizeNumber(entry.amount), 0) / 7
  const availableCashAmount = netAmount
  const supportDays = avgDailyExpense7d > 0 ? Math.floor(Math.max(0, availableCashAmount) / avgDailyExpense7d) : null
  const score = Math.max(0, Math.min(100, 70 + (netAmount >= 0 ? 15 : -20) + (supportDays == null || supportDays >= 14 ? 5 : -10)))
  const trendItems = []
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  for (let offset = 6; offset >= 0; offset -= 1) {
    const date = new Date(today)
    date.setDate(today.getDate() - offset)
    const key = date.toISOString().slice(0, 10)
    const dayEntries = entries.filter((entry) => dateKey(entry.occurredAt || entry.createdAt) === key)
    const incomeAmount = dayEntries.filter((entry) => entry.direction !== 'expense').reduce((sum, entry) => sum + normalizeNumber(entry.amount), 0)
    const expenseAmount = dayEntries.filter((entry) => entry.direction === 'expense').reduce((sum, entry) => sum + normalizeNumber(entry.amount), 0)
    trendItems.push({ date: key, incomeAmount, expenseAmount, netCashflowAmount: incomeAmount - expenseAmount })
  }
  return {
    ok: true,
    range: (event.payload && event.payload.range) || event.range || '30d',
    startAt: timestamp(trendItems[0] && trendItems[0].date),
    endAt: Date.now(),
    updatedAt: Date.now(),
    dataAvailability: {
      cashBalance: buildAvailability(entries.length ? 'compat' : 'unavailable', 'cashflow_entries', entries.length ? '按现金流明细汇总现金余额' : '暂无现金流明细'),
      receivable: buildAvailability('compat', 'cashflow_entries', '暂无独立应收账款表，按 0 返回'),
      payable: buildAvailability('compat', 'cashflow_entries', '暂无独立应付账款表，按 0 返回')
    },
    summary: {
      cashFlowHealthScore: score,
      cashFlowHealthLevel: score >= 80 ? 'healthy' : (score >= 60 ? 'watch' : 'risk'),
      cashFlowHealthSummary: score >= 80 ? '现金流健康' : (score >= 60 ? '现金流需关注' : '现金流存在风险'),
      cashBalanceAmount: netAmount,
      availableCashAmount,
      receivableAmount: 0,
      payableAmount: 0,
      avgDailyExpense7d,
      supportDays
    },
    trendItems,
    incomeBreakdown,
    expenseBreakdown,
    upcomingCashItems: [
      { type: 'receivable', label: '待收款', amount: 0, count: 0, note: '暂无独立应收数据' },
      { type: 'payable', label: '待付款', amount: 0, count: 0, note: '暂无独立应付数据' }
    ],
    advice: {
      healthTitle: score >= 80 ? '现金流稳定' : '关注现金流波动',
      healthDescription: netAmount >= 0 ? '当前收入覆盖支出。' : '当前支出高于收入，需要控制补货和运营支出。',
      restockSuggestionAmount: Math.max(0, availableCashAmount * 0.3),
      riskHint: score >= 60 ? '' : '建议优先回款并减少非必要采购。',
      nextFocus: ['确认待收款', '控制采购支出', '复核现金流明细']
    }
  }
}

function normalizeMovementType(value) {
  const type = normalizeText(value).toLowerCase()
  if (['in', 'out', 'adjust', 'reserve', 'release'].indexOf(type) >= 0) return type
  return 'adjust'
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

async function taskAction(event, operatorId, workspaceId) {
  const task = (await db.collection('followup_tasks').doc(event.taskId).get()).data
  if (!task || task.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: '跟进任务不存在。' }
  const patch = { updatedAt: now(), updatedBy: operatorId }
  if (event.action === 'complete') Object.assign(patch, { taskStatus: 'completed', completedAt: now(), resultType: 'done' })
  if (event.action === 'skip') Object.assign(patch, { taskStatus: 'skipped', completedAt: now(), resultType: 'skipped' })
  if (event.action === 'delay') Object.assign(patch, { dueAt: event.payload && event.payload.dueAt ? event.payload.dueAt : addDaysFrom(new Date(), 1) })
  await db.collection('followup_tasks').doc(event.taskId).update({ data: patch })
  await log(workspaceId, 'deal', task.dealId, 'followup_' + event.action, '跟进任务' + event.action, operatorId)
  return { ok: true }
}

exports.main = async (event) => {
  event = event || {}
  const mode = normalizeText(event.mode)
  const workspace = resolveWorkspaceId(event)
  if (!workspace.ok) return workspace
  const workspaceId = workspace.workspaceId
  let operatorId = ''
  if (USER_AUTH_MODES.indexOf(mode) >= 0) {
    const auth = requireOperatorId()
    if (!auth.ok) return auth
    operatorId = auth.operatorId
  }

  if (mode === 'inventoryManagementDashboard') return inventoryManagementDashboard(event, workspaceId)
  if (mode === 'cashflowHealthDashboard') return cashflowHealthDashboard(event, workspaceId)
  if (mode === 'taskAction') return taskAction(event, operatorId, workspaceId)
  if (mode === 'manualCreate') {
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
  if (mode === 'templateSave') {
    const template = Object.assign({ workspaceId, enabled: true, useCount: 0 }, event.template || {}, { workspaceId })
    const saved = await upsertById('message_templates', template)
    return { ok: true, template: saved }
  }
  if (mode === 'templateUse') {
    if (!event.templateId) return { ok: false, message: '缺少 templateId' }
    const _ = db.command
    await db.collection('message_templates').doc(event.templateId).update({ data: { useCount: _.inc(1), updatedAt: now() } })
    return { ok: true }
  }
  if (mode === 'skuSave') {
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
      basePrice: normalizeNumber(mergedSku.basePrice),
      costPrice: normalizeNumber(mergedSku.costPrice),
      purchasePrice: normalizeNumber(mergedSku.purchasePrice),
      stockOnHand: normalizeNumber(mergedSku.stockOnHand),
      stockReserved: normalizeNumber(mergedSku.stockReserved),
      safetyStock: normalizeNumber(mergedSku.safetyStock),
      stockUnit: normalizeText(mergedSku.stockUnit || '件'),
      stockLocation: normalizeText(mergedSku.stockLocation),
      supplierName: normalizeText(mergedSku.supplierName),
      inventoryRemark: normalizeText(mergedSku.inventoryRemark),
      reorderEnabled: mergedSku.reorderEnabled === true,
      lastRestockedAt: normalizeText(mergedSku.lastRestockedAt),
      tags: normalizeArray(mergedSku.tags),
      adjustableFields: normalizeArray(mergedSku.adjustableFields)
    })
    const saved = await upsertById('sku_catalog', sku)
    return { ok: true, sku: saved }
  }
  if (mode === 'inventoryMovementSave') {
    const movementInput = event.movement || {}
    const movementType = normalizeMovementType(movementInput.movementType || movementInput.type)
    const quantity = normalizeNumber(movementInput.quantity)
    const unitCost = normalizeNumber(movementInput.unitCost)
    const skuId = normalizeText(movementInput.skuId)
    if (!skuId) return { ok: false, message: '缺少 skuId' }
    if (!quantity) return { ok: false, message: '库存流水数量不能为空' }

    let sku = {}
    try {
      sku = (await db.collection('sku_catalog').doc(skuId).get()).data || {}
    } catch (err) {}

    const signedQuantity = movementType === 'out' || movementType === 'reserve' ? -Math.abs(quantity) : quantity
    const movement = Object.assign({}, movementInput, {
      workspaceId,
      skuId,
      skuName: normalizeText(movementInput.skuName || sku.name),
      movementType,
      quantity,
      unitCost,
      totalCost: normalizeNumber(movementInput.totalCost) || Math.abs(quantity) * unitCost,
      relatedOrderId: normalizeText(movementInput.relatedOrderId),
      relatedOrderNo: normalizeText(movementInput.relatedOrderNo),
      operatorId,
      note: normalizeText(movementInput.note),
      occurredAt: normalizeText(movementInput.occurredAt) || now(),
      createdAt: now(),
      updatedAt: now(),
      createdBy: operatorId,
      updatedBy: operatorId
    })
    const added = await db.collection('inventory_movements').add({ data: movement })
    const _ = db.command
    const stockPatch = { updatedAt: now(), updatedBy: operatorId }
    if (movementType === 'reserve') {
      stockPatch.stockReserved = _.inc(Math.abs(quantity))
    } else if (movementType === 'release') {
      stockPatch.stockReserved = _.inc(-Math.abs(quantity))
    } else {
      stockPatch.stockOnHand = _.inc(signedQuantity)
    }
    if (movementType === 'in') stockPatch.lastRestockedAt = movement.occurredAt
    await db.collection('sku_catalog').doc(skuId).update({ data: stockPatch })
    return { ok: true, movement: Object.assign({}, movement, { _id: added._id }) }
  }
  if (mode === 'cashflowSave') {
    const input = event.entry || {}
    const amount = normalizeNumber(input.amount)
    if (!amount) return { ok: false, message: '现金流金额不能为空' }
    const entry = Object.assign({}, input, {
      workspaceId,
      direction: normalizeDirection(input.direction),
      amount,
      category: normalizeText(input.category),
      paymentMethod: normalizeText(input.paymentMethod || input.channel),
      status: normalizeText(input.status || 'confirmed'),
      relatedOrderId: normalizeText(input.relatedOrderId || input.orderId),
      relatedOrderNo: normalizeText(input.relatedOrderNo || input.orderNo),
      relatedQuoteId: normalizeText(input.relatedQuoteId || input.quoteId),
      relatedSkuId: normalizeText(input.relatedSkuId || input.skuId),
      counterpartyName: normalizeText(input.counterpartyName || input.counterparty),
      operatorId,
      note: normalizeText(input.note),
      occurredAt: normalizeText(input.occurredAt) || now(),
      createdBy: operatorId,
      updatedBy: operatorId
    })
    const saved = await upsertById('cashflow_entries', entry)
    return { ok: true, entry: saved }
  }

  if (mode) return { ok: false, code: 'unsupported_mode', message: '不支持的 mode。' }
  if (!cloud.getWXContext().OPENID && !isAutoScanEnabled()) {
    return { ok: false, code: 'auto_scan_disabled', message: '自动跟进扫描未启用。' }
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
