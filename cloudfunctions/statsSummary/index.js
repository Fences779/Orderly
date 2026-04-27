const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()

const WON_STAGES = ['scheduled', 'in_production', 'ready_to_ship', 'shipped', 'received', 'completed', 'repurchase_due']

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

exports.main = async (event) => {
  const workspaceId = event.workspaceId || 'default'
  const period = event.period || '7d'
  const start = startOfPeriod(period)
  const now = new Date().toISOString()
  const deals = await safeList('deals', { workspaceId })
  const quotes = await safeList('quotes', { workspaceId })
  const tasks = await safeList('followup_tasks', { workspaceId })
  const templates = await safeList('message_templates', { workspaceId })
  const logs = await safeList('activity_logs', { workspaceId, entityType: 'deal' })

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
    platformDistribution: platformGroups.map((row) => ({ platform: row.label, count: row.count, percent: Math.round(row.count / maxPlatform * 100) })),
    templateTop: templates.sort((a, b) => (b.useCount || 0) - (a.useCount || 0)).slice(0, 5),
    riskReasons
  }
}
