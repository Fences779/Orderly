const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()

const WON_STAGES = ['scheduled', 'in_production', 'ready_to_ship', 'shipped', 'received', 'completed', 'repurchase_due']
const OPEN_STAGES = ['paid_pending_confirm', 'scheduled', 'in_production', 'ready_to_ship', 'exception']

function startOfPeriod(period) {
  const date = new Date()
  if (period === 'today') {
    date.setHours(0, 0, 0, 0)
  } else if (period === '7d') {
    date.setDate(date.getDate() - 6)
    date.setHours(0, 0, 0, 0)
  } else {
    date.setDate(date.getDate() - 29)
    date.setHours(0, 0, 0, 0)
  }
  return date.toISOString()
}

function requireOperatorId() {
  const operatorId = cloud.getWXContext().OPENID || ''
  if (!operatorId) return { ok: false, code: 'unauthorized', message: '未授权调用。' }
  return { ok: true, operatorId }
}

async function safeList(collection, where) {
  try {
    return (await db.collection(collection).where(where).limit(1000).get()).data || []
  } catch (err) {
    return []
  }
}

function inPeriod(value, start) {
  return value && String(value) >= start
}

function uniqueCount(rows, field) {
  const set = {}
  rows.forEach((row) => { if (row[field]) set[row[field]] = true })
  return Object.keys(set).length
}

function groupCount(rows, getter) {
  const map = {}
  rows.forEach((row) => {
    const key = getter(row) || '未填'
    map[key] = (map[key] || 0) + 1
  })
  return Object.keys(map).map((key) => ({ label: key, count: map[key] })).sort((a, b) => b.count - a.count)
}

function normalizeNumber(value) {
  const number = Number(value || 0)
  return Number.isFinite(number) ? number : 0
}

function dateKey(value) {
  const date = value ? new Date(value) : new Date()
  if (Number.isNaN(date.getTime())) return ''
  return date.toISOString().slice(0, 10)
}

function buildRecentBusinessTrendItems(deals, cashflows) {
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  const items = []
  for (let offset = 6; offset >= 0; offset -= 1) {
    const date = new Date(today)
    date.setDate(today.getDate() - offset)
    const key = date.toISOString().slice(0, 10)
    const orderCount = deals.filter((deal) => dateKey(deal.createdAt) === key).length
    const revenueAmount = cashflows
      .filter((entry) => entry.direction !== 'expense' && dateKey(entry.occurredAt || entry.createdAt) === key)
      .reduce((sum, entry) => sum + normalizeNumber(entry.amount), 0)
    items.push({ dateKey: key, label: key.slice(5), orderCount, revenueAmount })
  }
  return items
}

function buildPressureItem(status, label, count, targetCount) {
  return {
    fulfillmentStatus: status,
    label,
    count,
    targetCount,
    ratio: targetCount > 0 ? count / targetCount : 0
  }
}

function buildWorkbenchDashboard(deals, skus, cashflows, nowValue) {
  const todayKey = dateKey(nowValue)
  const yesterday = new Date()
  yesterday.setDate(yesterday.getDate() - 1)
  const yesterdayKey = dateKey(yesterday)
  const countByStage = groupCount(deals, (deal) => deal.fulfillmentStatus || deal.orderStatus || deal.dealStage)
    .reduce((map, item) => Object.assign(map, { [item.label]: item.count }), {})
  const todayOrderCount = deals.filter((deal) => dateKey(deal.createdAt) === todayKey).length
  const yesterdayOrderCount = deals.filter((deal) => dateKey(deal.createdAt) === yesterdayKey).length
  const revenueOn = (key) => cashflows
    .filter((entry) => entry.direction !== 'expense' && dateKey(entry.occurredAt || entry.createdAt) === key)
    .reduce((sum, entry) => sum + normalizeNumber(entry.amount), 0)
  const pendingMakeCount = countByStage.pending_make || countByStage.in_production || 0
  const readyToShipCount = countByStage.ready_to_ship || 0
  const exceptionOrderCount = countByStage.exception || 0
  const unfinishedOrderCount = deals.filter((deal) => OPEN_STAGES.indexOf(deal.fulfillmentStatus || deal.orderStatus || deal.dealStage) >= 0).length
  const lowStockCount = skus.filter((sku) => normalizeNumber(sku.safetyStock) > 0 && normalizeNumber(sku.stockOnHand) - normalizeNumber(sku.stockReserved) <= normalizeNumber(sku.safetyStock)).length
  const income = cashflows.filter((entry) => entry.direction !== 'expense').reduce((sum, entry) => sum + normalizeNumber(entry.amount), 0)
  const expense = cashflows.filter((entry) => entry.direction === 'expense').reduce((sum, entry) => sum + normalizeNumber(entry.amount), 0)
  const net = income - expense
  const cashFlowScore = Math.max(0, Math.min(100, 70 + (net >= 0 ? 15 : -20)))
  return {
    todayOrderCount,
    todayOrderCountDelta: todayOrderCount - yesterdayOrderCount,
    todayRevenueAmount: revenueOn(todayKey),
    todayRevenueAmountDelta: revenueOn(todayKey) - revenueOn(yesterdayKey),
    pendingMakeCount,
    pendingMakeDelta: 0,
    readyToShipCount,
    readyToShipDelta: 0,
    exceptionOrderCount,
    exceptionOrderDelta: 0,
    unfinishedOrderCount,
    lastSyncedAt: nowValue,
    recentBusinessTrendItems: buildRecentBusinessTrendItems(deals, cashflows),
    fulfillmentPressureItems: [
      buildPressureItem('pending_make', '待制作', pendingMakeCount, unfinishedOrderCount),
      buildPressureItem('ready_to_ship', '待发货', readyToShipCount, unfinishedOrderCount)
    ],
    inventoryHealthStatus: lowStockCount > 0 ? 'warning' : 'healthy',
    inventoryHealthSummary: lowStockCount > 0 ? lowStockCount + ' 个物料低于安全库存' : '库存风险在可控范围内',
    inventoryWarningCount: lowStockCount,
    cashFlowScore,
    cashFlowStatus: cashFlowScore >= 80 ? 'healthy' : (cashFlowScore >= 60 ? 'watch' : 'risk'),
    cashFlowDelta: 0
  }
}

exports.main = async (event) => {
  const auth = requireOperatorId()
  if (!auth.ok) return auth

  event = event || {}
  const workspaceId = event.workspaceId || 'default'
  const period = event.period || '7d'
  const start = startOfPeriod(period)
  const now = new Date().toISOString()
  const deals = await safeList('deals', { workspaceId })
  const quotes = await safeList('quotes', { workspaceId })
  const tasks = await safeList('followup_tasks', { workspaceId })
  const templates = await safeList('message_templates', { workspaceId })
  const logs = await safeList('activity_logs', { workspaceId, entityType: 'deal' })
  const skus = await safeList('sku_catalog', { workspaceId })
  const cashflows = await safeList('cashflow_entries', { workspaceId })

  const periodDeals = deals.filter((deal) => inPeriod(deal.createdAt, start))
  const periodQuotes = quotes.filter((quote) => inPeriod(quote.createdAt, start) || inPeriod(quote.sentAt, start))
  const wonLogDealIds = {}
  logs.filter((log) => inPeriod(log.createdAt, start) && log.actionType === 'stage_update' && log.afterData && WON_STAGES.indexOf(log.afterData.dealStage) >= 0)
    .forEach((log) => { wonLogDealIds[log.entityId] = true })
  deals.filter((deal) => inPeriod(deal.updatedAt, start) && WON_STAGES.indexOf(deal.dealStage) >= 0)
    .forEach((deal) => { wonLogDealIds[deal._id] = true })

  const lostLogDealIds = {}
  logs.filter((log) => inPeriod(log.createdAt, start) && log.actionType === 'stage_update' && log.afterData && log.afterData.dealStage === 'lost')
    .forEach((log) => { lostLogDealIds[log.entityId] = true })
  deals.filter((deal) => inPeriod(deal.updatedAt, start) && deal.dealStage === 'lost')
    .forEach((deal) => { lostLogDealIds[deal._id] = true })

  const pendingDue = tasks.filter((task) => task.taskStatus === 'pending' && (!task.dueAt || task.dueAt <= now))
  const repurchaseDue = tasks.filter((task) => task.taskStatus === 'pending' && task.triggerType === 'repurchase')
  const winRate = periodDeals.length ? Object.keys(wonLogDealIds).length / periodDeals.length : 0
  const platformGroups = groupCount(periodDeals, (deal) => deal.sourceEntry)
  const maxPlatform = Math.max(1, ...platformGroups.map((row) => row.count))
  const riskReasons = groupCount(deals.filter((deal) => deal.dealStage === 'lost' || (deal.riskFlags && deal.riskFlags.length)), (deal) => {
    if (deal.lossReason) return deal.lossReason
    return Array.isArray(deal.riskFlags) && deal.riskFlags.length ? deal.riskFlags[0] : ''
  }).slice(0, 6)

  return {
    ok: true,
    period,
    summary: {
      newDeals: periodDeals.length,
      quotedDeals: uniqueCount(periodQuotes, 'dealId'),
      wonDeals: Object.keys(wonLogDealIds).length,
      winRate,
      winRateText: Math.round(winRate * 100) + '%',
      lostDeals: Object.keys(lostLogDealIds).length,
      pendingFollowups: pendingDue.length,
      repurchaseDue: repurchaseDue.length
    },
    workbenchDashboard: buildWorkbenchDashboard(deals, skus, cashflows, now),
    platformDistribution: platformGroups.map((row) => ({ platform: row.label, count: row.count, percent: Math.round(row.count / maxPlatform * 100) })),
    templateTop: templates.sort((a, b) => (b.useCount || 0) - (a.useCount || 0)).slice(0, 5),
    riskReasons
  }
}
