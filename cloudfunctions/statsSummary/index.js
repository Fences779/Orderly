const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const ALLOWED_OPENIDS_ENV_NAME = 'ORDERLY_ALLOWED_OPENIDS'
const OPENID_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_OPENID_WORKSPACE_IDS'
const AUTH_ALLOW_ALL_DEV_ENV_NAME = 'ORDERLY_AUTH_ALLOW_ALL_DEV'
const MAX_EVENT_BYTES = 65536
const MAX_WORKSPACE_ID_LENGTH = 128
const MAX_LABEL_TEXT_LENGTH = 128
const MAX_TEMPLATE_TOP_ITEMS = 5

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
  if (!isAllowedOperatorId(operatorId)) return { ok: false, code: 'forbidden', message: '无权访问。' }
  return { ok: true, operatorId }
}

function isAllowedOperatorId(operatorId) {
  const allowed = normalizeList(process.env[ALLOWED_OPENIDS_ENV_NAME])
  return allowed.indexOf(operatorId) >= 0 || isAuthAllowAllDevEnabled()
}

function isAuthAllowAllDevEnabled() {
  const runtime = String(process.env.ORDERLY_RUNTIME_ENV || process.env.NODE_ENV || '').trim().toLowerCase()
  return process.env[AUTH_ALLOW_ALL_DEV_ENV_NAME] === '1' && ['development', 'dev', 'test', 'local'].indexOf(runtime) >= 0
}

function rejectOversizedEvent(event) {
  const bytes = Buffer.byteLength(JSON.stringify(event || {}), 'utf8')
  return bytes > MAX_EVENT_BYTES ? { ok: false, code: 'payload_too_large', message: '请求体过大。' } : null
}

function rejectPollutedEvent(event) {
  return hasUnsafeObjectKey(event, 0) ? { ok: false, code: 'invalid_payload', message: '请求参数非法。' } : null
}

function hasUnsafeObjectKey(value, depth) {
  if (depth > 32) return true
  if (value == null || typeof value !== 'object') return false
  return Object.keys(value).some((key) => {
    if (key === '__proto__' || key === 'constructor' || key === 'prototype') return true
    return hasUnsafeObjectKey(value[key], depth + 1)
  })
}

function normalizeList(value) {
  return String(value || '')
    .split(/[,\s，、;；]+/)
    .map((item) => item.trim())
    .filter(Boolean)
}

function normalizeWorkspaceId(value) {
  if (value == null || typeof value === 'object') return ''
  const id = String(value).replace(/[\u0000-\u001f\u007f]/g, '').trim()
  return id.length <= MAX_WORKSPACE_ID_LENGTH && /^[A-Za-z0-9_.:-]+$/.test(id) ? id : ''
}

function normalizeWorkspaceList(value) {
  return normalizeList(value).map(normalizeWorkspaceId).filter(Boolean)
}

function resolveWorkspaceId(event, operatorId) {
  const rawWorkspaceId = event && event.workspaceId
  const requestedWorkspaceId = rawWorkspaceId == null ? '' : String(rawWorkspaceId).trim()
  const workspaceId = requestedWorkspaceId ? normalizeWorkspaceId(rawWorkspaceId) : DEFAULT_WORKSPACE_ID
  if (!workspaceId) return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  const configured = normalizeWorkspaceList(process.env[ALLOWED_WORKSPACE_IDS_ENV_NAME])
  const allowed = Array.from(new Set(configured.length ? configured : [DEFAULT_WORKSPACE_ID]))
  if (allowed.indexOf(workspaceId) < 0) return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  const binding = validateWorkspaceBinding(operatorId, workspaceId, allowed)
  if (!binding.ok) return binding
  return { ok: true, workspaceId }
}

function validateWorkspaceBinding(operatorId, workspaceId, allowedWorkspaceIds) {
  const boundWorkspaceIds = resolveOperatorWorkspaceIds(operatorId)
  if (boundWorkspaceIds.length > 0) {
    if (boundWorkspaceIds.indexOf('*') >= 0 || boundWorkspaceIds.indexOf(workspaceId) >= 0) return { ok: true }
    return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  }

  if (allowedWorkspaceIds.length > 1) {
    return { ok: false, code: 'workspace_binding_required', message: '工作区权限未配置。' }
  }

  return { ok: true }
}

function resolveOperatorWorkspaceIds(operatorId) {
  if (!operatorId) return []
  const raw = String(process.env[OPENID_WORKSPACE_IDS_ENV_NAME] || '').trim()
  if (!raw) return []

  if (raw[0] === '{') {
    try {
      const parsed = JSON.parse(raw)
      return normalizeWorkspaceBindingValue(parsed && parsed[operatorId])
    } catch (err) {
      return []
    }
  }

  const entries = raw.split(/[;；\r\n]+/).map((item) => item.trim()).filter(Boolean)
  for (const entry of entries) {
    const separatorIndex = entry.indexOf('=') >= 0 ? entry.indexOf('=') : entry.indexOf(':')
    if (separatorIndex <= 0) continue
    const key = entry.slice(0, separatorIndex).trim()
    if (key === operatorId) return normalizeWorkspaceBindingValue(entry.slice(separatorIndex + 1))
  }

  return []
}

function normalizeWorkspaceBindingValue(value) {
  if (!value) return []
  const values = Array.isArray(value)
    ? value.map((item) => String(item).trim())
    : String(value).split(/[,\s，、|/]+/).map((item) => item.trim())
  return values
    .map((item) => (item === '*' ? '*' : normalizeWorkspaceId(item)))
    .filter(Boolean)
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
  const set = Object.create(null)
  rows.forEach((row) => { if (row[field]) set[row[field]] = true })
  return Object.keys(set).length
}

function normalizeText(value) {
  if (value == null || typeof value === 'object') return ''
  return String(value).replace(/[\u0000-\u001f\u007f]/g, ' ').trim()
}

function limitText(value, maxLength = MAX_LABEL_TEXT_LENGTH) {
  return normalizeText(value).slice(0, maxLength)
}

function groupCount(rows, getter) {
  const map = Object.create(null)
  rows.forEach((row) => {
    const key = limitText(getter(row)) || '未填'
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

function buildTemplateTop(templates) {
  return templates
    .slice()
    .sort((a, b) => normalizeNumber(b.useCount) - normalizeNumber(a.useCount))
    .slice(0, MAX_TEMPLATE_TOP_ITEMS)
    .map((template) => ({
      _id: limitText(template._id),
      title: limitText(template.title || template.name),
      useCount: normalizeNumber(template.useCount)
    }))
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

function logInternalError(scope, err) {
  const name = err && err.name ? String(err.name).slice(0, 64) : 'Error'
  console.error(scope, { name })
}

async function handleRequest(event) {
  const auth = requireOperatorId()
  if (!auth.ok) return auth

  event = event || {}
  const oversized = rejectOversizedEvent(event)
  if (oversized) return oversized
  const polluted = rejectPollutedEvent(event)
  if (polluted) return polluted

  const workspace = resolveWorkspaceId(event, auth.operatorId)
  if (!workspace.ok) return workspace
  const workspaceId = workspace.workspaceId
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
  const wonLogDealIds = Object.create(null)
  logs.filter((log) => inPeriod(log.createdAt, start) && log.actionType === 'stage_update' && log.afterData && WON_STAGES.indexOf(log.afterData.dealStage) >= 0)
    .forEach((log) => { wonLogDealIds[log.entityId] = true })
  deals.filter((deal) => inPeriod(deal.updatedAt, start) && WON_STAGES.indexOf(deal.dealStage) >= 0)
    .forEach((deal) => { wonLogDealIds[deal._id] = true })

  const lostLogDealIds = Object.create(null)
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
    templateTop: buildTemplateTop(templates),
    riskReasons
  }
}

exports.main = async (event) => {
  try {
    return await handleRequest(event)
  } catch (err) {
    logInternalError('statsSummary failed', err)
    return { ok: false, code: 'internal_error', message: '统计读取失败。' }
  }
}
