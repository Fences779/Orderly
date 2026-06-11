const crypto = require('crypto')
const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const ALLOWED_OPENIDS_ENV_NAME = 'ORDERLY_ALLOWED_OPENIDS'
const OPENID_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_OPENID_WORKSPACE_IDS'
const AUTH_ALLOW_ALL_DEV_ENV_NAME = 'ORDERLY_AUTH_ALLOW_ALL_DEV'
const AUTO_SCAN_ENV_NAME = 'ORDERLY_ENABLE_FOLLOWUP_AUTO_SCAN'
const AUTO_SCAN_SECRET_ENV_NAME = 'ORDERLY_FOLLOWUP_AUTO_SCAN_SECRET'
const AUTO_SCAN_WORKSPACE_ID_ENV_NAME = 'ORDERLY_FOLLOWUP_AUTO_SCAN_WORKSPACE_ID'
const MAX_EVENT_BYTES = 65536
const MAX_WORKSPACE_ID_LENGTH = 128
const MAX_DOC_ID_LENGTH = 128
const MIN_AUTO_SCAN_SECRET_LENGTH = 24
const MAX_AUTO_SCAN_SECRET_LENGTH = 4096
const USER_AUTH_MODES = ['inventoryManagementDashboard', 'cashflowHealthDashboard', 'taskAction', 'manualCreate', 'templateSave', 'templateUse', 'skuSave', 'inventoryMovementSave', 'cashflowSave']
const MANUAL_TASK_FIELDS = ['dealId', 'customerId', 'customerName', 'dueAt', 'priorityScore', 'templateId', 'suggestedText']
const TEMPLATE_FIELDS = ['_id', 'title', 'name', 'scene', 'sceneType', 'content', 'variables', 'enabled', 'tags', 'sortOrder']
const SKU_FIELDS = ['_id', 'name', 'title', 'category', 'basePrice', 'costPrice', 'safetyStock', 'stockUnit', 'unit', 'stockLocation', 'inventoryRemark', 'remark', 'reorderEnabled', 'lastRestockedAt', 'tags', 'adjustableFields', 'enabled', 'sortOrder']
const INVENTORY_MOVEMENT_FIELDS = ['skuId', 'skuName', 'movementType', 'type', 'quantity', 'unitCost', 'totalCost', 'relatedOrderId', 'relatedOrderNo', 'note', 'occurredAt']
const CASHFLOW_FIELDS = ['_id', 'direction', 'amount', 'category', 'paymentMethod', 'channel', 'status', 'relatedOrderId', 'orderId', 'relatedOrderNo', 'orderNo', 'relatedQuoteId', 'quoteId', 'relatedSkuId', 'skuId', 'counterpartyName', 'counterparty', 'note', 'occurredAt']
const TASK_ACTIONS = ['complete', 'skip', 'delay']
const CASHFLOW_DIRECTIONS = ['income', 'expense']
const CASHFLOW_STATUSES = ['pending', 'confirmed', 'cancelled']
const INVENTORY_MOVEMENT_TYPES = ['in', 'out', 'adjust', 'reserve', 'release']
const MAX_CASHFLOW_AMOUNT = 100000000
const MAX_INVENTORY_QUANTITY = 1000000
const MAX_SHORT_TEXT_LENGTH = 128
const MAX_NOTE_TEXT_LENGTH = 512
const MAX_TEMPLATE_CONTENT_LENGTH = 2000
const MAX_TAGS = 20
const MAX_DASHBOARD_PAGE_SIZE = 100
const INVENTORY_DASHBOARD_SORT_FIELDS = ['materialName', 'category', 'currentStockQty', 'sold7dQty', 'sold7dRatio', 'sold30dQty', 'sold30dRatio', 'consumed7dQty', 'consumed30dQty', 'safeStockSuggestedQty', 'status', 'unitCost', 'lastRestockedAt', 'supplierName']
const INVENTORY_DASHBOARD_STATUSES = ['all', 'fast_selling', 'low_selling', 'normal', 'low_stock', 'disabled']

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
  if (value == null || typeof value === 'object') return ''
  return String(value).replace(/[\u0000-\u001f\u007f]/g, ' ').trim()
}

function limitText(value, maxLength) {
  return normalizeText(value).slice(0, maxLength)
}

function normalizeWorkspaceId(value) {
  if (value == null || typeof value === 'object') return ''
  const id = String(value).replace(/[\u0000-\u001f\u007f]/g, '').trim()
  return id.length <= MAX_WORKSPACE_ID_LENGTH && /^[A-Za-z0-9_.:-]+$/.test(id) ? id : ''
}

function normalizeWorkspaceArray(value) {
  return normalizeArray(value).map(normalizeWorkspaceId).filter(Boolean)
}

function normalizeDocId(value) {
  if (value == null || typeof value === 'object') return ''
  const id = String(value).replace(/[\u0000-\u001f\u007f]/g, '').trim()
  return id.length <= MAX_DOC_ID_LENGTH && /^[A-Za-z0-9_-]+$/.test(id) ? id : ''
}

function normalizeAutoScanSecret(value) {
  if (value == null || typeof value === 'object') return ''
  const secret = String(value).trim()
  if (!secret || secret.length > MAX_AUTO_SCAN_SECRET_LENGTH) return ''
  return /[\u0000-\u001f\u007f]/.test(secret) ? '' : secret
}

function isStrongAutoScanSecret(secret) {
  const lowered = secret.toLowerCase()
  return secret.length >= MIN_AUTO_SCAN_SECRET_LENGTH &&
    ['replace-me', 'changeme', 'change-me', 'test', 'token', 'password', 'secret'].indexOf(lowered) < 0
}

function hasTextValue(value) {
  return value != null && String(value).trim() !== ''
}

function normalizeLimitedArray(value, maxItems = MAX_TAGS) {
  const source = Array.isArray(value)
    ? value
    : String(value || '').split(/[,\s，、;；|/]+/)
  return Array.from(new Set(source.map((item) => limitText(item, MAX_SHORT_TEXT_LENGTH)).filter(Boolean))).slice(0, maxItems)
}

function normalizeBoundedNumber(value, minValue, maxValue) {
  const number = normalizeNumber(value)
  if (number < minValue) return minValue
  if (number > maxValue) return maxValue
  return number
}

function normalizeInteger(value, fallback, minValue, maxValue) {
  const number = Number(value)
  if (!Number.isFinite(number)) return fallback
  return Math.min(maxValue, Math.max(minValue, Math.floor(number)))
}

function normalizeAllowedText(value, allowedValues, fallback) {
  const text = normalizeText(value || fallback)
  return allowedValues.indexOf(text) >= 0 ? text : fallback
}

function pickFields(source, allowedFields) {
  const result = {}
  const input = source || {}
  allowedFields.forEach((field) => {
    if (Object.prototype.hasOwnProperty.call(input, field)) result[field] = input[field]
  })
  return result
}

function requireOperatorId() {
  const operatorId = cloud.getWXContext().OPENID || ''
  if (!operatorId) return { ok: false, code: 'unauthorized', message: '未授权调用。' }
  if (!isAllowedOperatorId(operatorId)) return { ok: false, code: 'forbidden', message: '无权访问。' }
  return { ok: true, operatorId }
}

function isAllowedOperatorId(operatorId) {
  const allowed = normalizeArray(process.env[ALLOWED_OPENIDS_ENV_NAME])
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

function resolveWorkspaceId(event, operatorId) {
  const rawWorkspaceId = event && event.workspaceId
  const requestedWorkspaceId = rawWorkspaceId == null ? '' : String(rawWorkspaceId).trim()
  const workspaceId = requestedWorkspaceId ? normalizeWorkspaceId(rawWorkspaceId) : DEFAULT_WORKSPACE_ID
  if (!workspaceId) return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  const configured = normalizeWorkspaceArray(process.env[ALLOWED_WORKSPACE_IDS_ENV_NAME])
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

function isAutoScanEnabled() {
  return process.env[AUTO_SCAN_ENV_NAME] === '1'
}

function resolveAutoScanWorkspaceId() {
  return normalizeWorkspaceId(process.env[AUTO_SCAN_WORKSPACE_ID_ENV_NAME]) || DEFAULT_WORKSPACE_ID
}

function timingSafeTextEquals(left, right) {
  const leftBytes = Buffer.from(left, 'utf8')
  const rightBytes = Buffer.from(right, 'utf8')
  if (leftBytes.length !== rightBytes.length) return false
  return crypto.timingSafeEqual(leftBytes, rightBytes)
}

function isAutoScanSecretValid(event) {
  const configuredSecret = normalizeAutoScanSecret(process.env[AUTO_SCAN_SECRET_ENV_NAME])
  if (!configuredSecret || !isStrongAutoScanSecret(configuredSecret)) return false
  const providedSecret = normalizeAutoScanSecret(event && event.autoScanSecret)
  return providedSecret ? timingSafeTextEquals(providedSecret, configuredSecret) : false
}

function requireScanTriggerAccess(event) {
  if (cloud.getWXContext().OPENID) return requireOperatorId()
  if (!isAutoScanEnabled()) return { ok: false, code: 'auto_scan_disabled', message: '自动跟进扫描未启用。' }
  if (!isAutoScanSecretValid(event)) return { ok: false, code: 'forbidden', message: '无权访问。' }
  return { ok: true, operatorId: 'system', workspaceId: resolveAutoScanWorkspaceId() }
}

function normalizeDirection(value) {
  const direction = normalizeText(value).toLowerCase()
  if (!direction) return 'income'
  return CASHFLOW_DIRECTIONS.indexOf(direction) >= 0 ? direction : ''
}

function normalizeCashflowStatus(value) {
  const status = normalizeText(value || 'confirmed').toLowerCase()
  return CASHFLOW_STATUSES.indexOf(status) >= 0 ? status : ''
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

function normalizeIsoDate(value, fallbackValue) {
  const date = value ? new Date(value) : new Date(fallbackValue || Date.now())
  return Number.isNaN(date.getTime()) ? new Date(fallbackValue || Date.now()).toISOString() : date.toISOString()
}

function normalizeOptionalIsoDate(value) {
  const text = normalizeText(value)
  if (!text) return ''
  const date = new Date(text)
  return Number.isNaN(date.getTime()) ? '' : date.toISOString()
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
  const page = normalizeInteger(request.page, 1, 1, 100000)
  const pageSize = normalizeInteger(request.pageSize, 10, 1, MAX_DASHBOARD_PAGE_SIZE)
  const keyword = limitText(request.keyword, MAX_SHORT_TEXT_LENGTH).toLowerCase()
  const category = limitText(request.category, MAX_SHORT_TEXT_LENGTH)
  const status = normalizeAllowedText(request.status, INVENTORY_DASHBOARD_STATUSES, 'all')
  const sortBy = normalizeAllowedText(request.sortBy, INVENTORY_DASHBOARD_SORT_FIELDS, 'sold30dRatio')
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
  return INVENTORY_MOVEMENT_TYPES.indexOf(type) >= 0 ? type : ''
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

async function getWorkspaceDoc(collection, id, workspaceId) {
  const docId = normalizeDocId(id)
  if (!docId) return null
  try {
    const data = (await db.collection(collection).doc(docId).get()).data
    return data && data.workspaceId === workspaceId ? data : null
  } catch (err) {
    return null
  }
}

async function upsertById(collection, data, workspaceId) {
  if (data._id) {
    const id = normalizeDocId(data._id)
    if (!id) return null
    const existing = await getWorkspaceDoc(collection, id, workspaceId)
    if (!existing) return null
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
  const taskId = normalizeDocId(event.taskId)
  const action = normalizeText(event.action)
  if (TASK_ACTIONS.indexOf(action) < 0) return { ok: false, code: 'invalid_action', message: '非法跟进任务动作。' }

  const task = await getWorkspaceDoc('followup_tasks', taskId, workspaceId)
  if (!task) return { ok: false, code: 'not_found', message: '跟进任务不存在。' }
  const patch = { updatedAt: now(), updatedBy: operatorId }
  if (action === 'complete') Object.assign(patch, { taskStatus: 'completed', completedAt: now(), resultType: 'done' })
  if (action === 'skip') Object.assign(patch, { taskStatus: 'skipped', completedAt: now(), resultType: 'skipped' })
  if (action === 'delay') {
    const fallbackDueAt = addDaysFrom(new Date(), 1)
    Object.assign(patch, { dueAt: normalizeIsoDate(event.payload && event.payload.dueAt, fallbackDueAt) })
  }
  await db.collection('followup_tasks').doc(taskId).update({ data: patch })
  await log(workspaceId, 'deal', task.dealId, 'followup_' + action, '跟进任务' + action, operatorId)
  return { ok: true }
}

function logInternalError(scope, err) {
  const name = err && err.name ? String(err.name).slice(0, 64) : 'Error'
  console.error(scope, { name })
}

async function handleRequest(event) {
  event = event || {}
  const oversized = rejectOversizedEvent(event)
  if (oversized) return oversized
  const polluted = rejectPollutedEvent(event)
  if (polluted) return polluted

  const mode = normalizeText(event.mode)
  let operatorId = ''
  let trustedWorkspaceEvent = event
  if (USER_AUTH_MODES.indexOf(mode) >= 0) {
    const auth = requireOperatorId()
    if (!auth.ok) return auth
    operatorId = auth.operatorId
  } else if (!mode) {
    const scanAuth = requireScanTriggerAccess(event)
    if (!scanAuth.ok) return scanAuth
    operatorId = scanAuth.operatorId
    if (scanAuth.workspaceId) trustedWorkspaceEvent = { workspaceId: scanAuth.workspaceId }
  } else {
    return { ok: false, code: 'unsupported_mode', message: '不支持的 mode。' }
  }

  const workspace = resolveWorkspaceId(trustedWorkspaceEvent, operatorId)
  if (!workspace.ok) return workspace
  const workspaceId = workspace.workspaceId

  if (mode === 'inventoryManagementDashboard') return inventoryManagementDashboard(event, workspaceId)
  if (mode === 'cashflowHealthDashboard') return cashflowHealthDashboard(event, workspaceId)
  if (mode === 'taskAction') return taskAction(event, operatorId, workspaceId)
  if (mode === 'manualCreate') {
    const input = pickFields(event.task, MANUAL_TASK_FIELDS)
    const dealId = normalizeDocId(input.dealId)
    const customerId = normalizeDocId(input.customerId)
    const templateId = normalizeDocId(input.templateId)
    if (hasTextValue(input.dealId) && !dealId) return { ok: false, code: 'invalid_deal_id', message: '非法 dealId。' }
    if (hasTextValue(input.customerId) && !customerId) return { ok: false, code: 'invalid_customer_id', message: '非法客户 ID。' }
    if (hasTextValue(input.templateId) && !templateId) return { ok: false, code: 'invalid_template_id', message: '非法模板 ID。' }
    if (dealId && !(await getWorkspaceDoc('deals', dealId, workspaceId))) {
      return { ok: false, code: 'not_found', message: 'deal 不存在。' }
    }
    if (customerId && !(await getWorkspaceDoc('customers', customerId, workspaceId))) {
      return { ok: false, code: 'not_found', message: '客户不存在。' }
    }

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
    }, input, {
      workspaceId,
      dealId,
      customerId,
      customerName: limitText(input.customerName, MAX_SHORT_TEXT_LENGTH),
      dueAt: normalizeIsoDate(input.dueAt, addDaysFrom(new Date(), 1)),
      priorityScore: normalizeBoundedNumber(input.priorityScore || 28, 0, 100),
      templateId,
      suggestedText: limitText(input.suggestedText, MAX_NOTE_TEXT_LENGTH),
      dedupeKey: 'manual:' + Date.now() + ':' + Math.random().toString(36).slice(2),
      createdAt: now(),
      updatedAt: now()
    })
    const added = await db.collection('followup_tasks').add({ data: task })
    await log(workspaceId, 'deal', task.dealId, 'followup_manual_create', '手动跟进任务创建', operatorId)
    return { ok: true, task: Object.assign({}, task, { _id: added._id }) }
  }
  if (mode === 'templateSave') {
    const input = pickFields(event.template, TEMPLATE_FIELDS)
    const template = Object.assign({ workspaceId, enabled: true, useCount: 0 }, input, {
      workspaceId,
      title: limitText(input.title || input.name, MAX_SHORT_TEXT_LENGTH),
      name: limitText(input.name || input.title, MAX_SHORT_TEXT_LENGTH),
      scene: limitText(input.scene, MAX_SHORT_TEXT_LENGTH),
      sceneType: limitText(input.sceneType, MAX_SHORT_TEXT_LENGTH),
      content: limitText(input.content, MAX_TEMPLATE_CONTENT_LENGTH),
      variables: normalizeLimitedArray(input.variables),
      enabled: input.enabled !== false,
      tags: normalizeLimitedArray(input.tags),
      sortOrder: normalizeBoundedNumber(input.sortOrder || 10, 0, 1000000)
    })
    const saved = await upsertById('message_templates', template, workspaceId)
    if (!saved) return { ok: false, code: 'not_found', message: '模板不存在。' }
    return { ok: true, template: saved }
  }
  if (mode === 'templateUse') {
    const templateId = normalizeDocId(event.templateId)
    if (!templateId) return { ok: false, message: '缺少 templateId' }
    const template = await getWorkspaceDoc('message_templates', templateId, workspaceId)
    if (!template) return { ok: false, code: 'not_found', message: '模板不存在。' }
    const _ = db.command
    await db.collection('message_templates').doc(templateId).update({ data: { useCount: _.inc(1), updatedAt: now() } })
    return { ok: true }
  }
  if (mode === 'skuSave') {
    const rawSku = pickFields(event.sku, SKU_FIELDS)
    const rawSkuId = normalizeDocId(rawSku._id)
    let baseSku = {}
    if (rawSku._id) {
      if (!rawSkuId) return { ok: false, code: 'invalid_sku_id', message: '非法 SKU ID。' }
      baseSku = await getWorkspaceDoc('sku_catalog', rawSkuId, workspaceId)
      if (!baseSku) return { ok: false, code: 'not_found', message: 'SKU 不存在。' }
    }
    const mergedSku = Object.assign({}, baseSku, rawSku)
    const sku = Object.assign({ workspaceId, enabled: true, sortOrder: 10 }, mergedSku, {
      workspaceId,
      name: limitText(mergedSku.name || mergedSku.title, MAX_SHORT_TEXT_LENGTH),
      title: limitText(mergedSku.title || mergedSku.name, MAX_SHORT_TEXT_LENGTH),
      category: limitText(mergedSku.category, MAX_SHORT_TEXT_LENGTH),
      basePrice: normalizeBoundedNumber(mergedSku.basePrice, 0, MAX_CASHFLOW_AMOUNT),
      costPrice: normalizeBoundedNumber(mergedSku.costPrice, 0, MAX_CASHFLOW_AMOUNT),
      purchasePrice: normalizeBoundedNumber(mergedSku.purchasePrice, 0, MAX_CASHFLOW_AMOUNT),
      stockOnHand: normalizeBoundedNumber(mergedSku.stockOnHand, 0, MAX_INVENTORY_QUANTITY),
      stockReserved: normalizeBoundedNumber(mergedSku.stockReserved, 0, MAX_INVENTORY_QUANTITY),
      safetyStock: normalizeBoundedNumber(mergedSku.safetyStock, 0, MAX_INVENTORY_QUANTITY),
      stockUnit: limitText(mergedSku.stockUnit || mergedSku.unit || '件', 32),
      stockLocation: limitText(mergedSku.stockLocation, MAX_SHORT_TEXT_LENGTH),
      supplierName: limitText(mergedSku.supplierName, MAX_SHORT_TEXT_LENGTH),
      inventoryRemark: limitText(mergedSku.inventoryRemark || mergedSku.remark, MAX_NOTE_TEXT_LENGTH),
      reorderEnabled: mergedSku.reorderEnabled === true,
      lastRestockedAt: normalizeOptionalIsoDate(mergedSku.lastRestockedAt),
      tags: normalizeLimitedArray(mergedSku.tags),
      adjustableFields: normalizeLimitedArray(mergedSku.adjustableFields),
      sortOrder: normalizeBoundedNumber(mergedSku.sortOrder || 10, 0, 1000000)
    })
    delete sku.unit
    delete sku.remark
    const saved = await upsertById('sku_catalog', sku, workspaceId)
    if (!saved) return { ok: false, code: 'not_found', message: 'SKU 不存在。' }
    return { ok: true, sku: saved }
  }
  if (mode === 'inventoryMovementSave') {
    const movementInput = pickFields(event.movement, INVENTORY_MOVEMENT_FIELDS)
    const movementType = normalizeMovementType(movementInput.movementType || movementInput.type)
    const quantity = normalizeNumber(movementInput.quantity)
    const unitCost = normalizeNumber(movementInput.unitCost)
    const skuId = normalizeDocId(movementInput.skuId)
    if (!skuId) return { ok: false, message: '缺少 skuId' }
    if (!movementType) return { ok: false, code: 'invalid_movement_type', message: '非法库存流水类型。' }
    if (!quantity) return { ok: false, message: '库存流水数量不能为空' }
    if (Math.abs(quantity) > MAX_INVENTORY_QUANTITY) return { ok: false, code: 'invalid_inventory_quantity', message: '库存流水数量超出允许范围。' }
    if (unitCost < 0 || unitCost > MAX_CASHFLOW_AMOUNT) return { ok: false, code: 'invalid_inventory_unit_cost', message: '库存流水单价超出允许范围。' }
    const totalCost = normalizeNumber(movementInput.totalCost) || Math.abs(quantity) * unitCost
    if (totalCost < 0 || totalCost > MAX_CASHFLOW_AMOUNT) return { ok: false, code: 'invalid_inventory_total_cost', message: '库存流水总价超出允许范围。' }
    const relatedOrderId = normalizeDocId(movementInput.relatedOrderId)
    if (hasTextValue(movementInput.relatedOrderId) && !relatedOrderId) return { ok: false, code: 'invalid_related_order_id', message: '非法关联订单 ID。' }

    const sku = await getWorkspaceDoc('sku_catalog', skuId, workspaceId)
    if (!sku) return { ok: false, code: 'not_found', message: 'SKU 不存在。' }
    if (relatedOrderId && !(await getWorkspaceDoc('deals', relatedOrderId, workspaceId))) {
      return { ok: false, code: 'not_found', message: '关联订单不存在。' }
    }

    const signedQuantity = movementType === 'out' || movementType === 'reserve' ? -Math.abs(quantity) : quantity
    const movement = Object.assign({}, movementInput, {
      workspaceId,
      skuId,
      skuName: limitText(movementInput.skuName || sku.name, MAX_SHORT_TEXT_LENGTH),
      movementType,
      quantity,
      unitCost,
      totalCost,
      relatedOrderId,
      relatedOrderNo: limitText(movementInput.relatedOrderNo, MAX_SHORT_TEXT_LENGTH),
      operatorId,
      note: limitText(movementInput.note, MAX_NOTE_TEXT_LENGTH),
      occurredAt: normalizeIsoDate(movementInput.occurredAt, now()),
      createdAt: now(),
      updatedAt: now(),
      createdBy: operatorId,
      updatedBy: operatorId
    })
    delete movement.type
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
    const input = pickFields(event.entry, CASHFLOW_FIELDS)
    const amount = normalizeNumber(input.amount)
    const direction = normalizeDirection(input.direction)
    const status = normalizeCashflowStatus(input.status || 'confirmed')
    if (!amount) return { ok: false, message: '现金流金额不能为空' }
    if (amount < 0 || amount > MAX_CASHFLOW_AMOUNT) return { ok: false, code: 'invalid_cashflow_amount', message: '现金流金额超出允许范围。' }
    if (!direction) return { ok: false, code: 'invalid_cashflow_direction', message: '非法现金流方向。' }
    if (!status) return { ok: false, code: 'invalid_cashflow_status', message: '非法现金流状态。' }
    const rawRelatedOrderId = hasTextValue(input.relatedOrderId) ? input.relatedOrderId : input.orderId
    const rawRelatedQuoteId = hasTextValue(input.relatedQuoteId) ? input.relatedQuoteId : input.quoteId
    const rawRelatedSkuId = hasTextValue(input.relatedSkuId) ? input.relatedSkuId : input.skuId
    const relatedOrderId = normalizeDocId(rawRelatedOrderId)
    const relatedQuoteId = normalizeDocId(rawRelatedQuoteId)
    const relatedSkuId = normalizeDocId(rawRelatedSkuId)
    if (hasTextValue(rawRelatedOrderId) && !relatedOrderId) return { ok: false, code: 'invalid_related_order_id', message: '非法关联订单 ID。' }
    if (hasTextValue(rawRelatedQuoteId) && !relatedQuoteId) return { ok: false, code: 'invalid_related_quote_id', message: '非法关联报价 ID。' }
    if (hasTextValue(rawRelatedSkuId) && !relatedSkuId) return { ok: false, code: 'invalid_related_sku_id', message: '非法关联 SKU ID。' }
    if (relatedOrderId && !(await getWorkspaceDoc('deals', relatedOrderId, workspaceId))) {
      return { ok: false, code: 'not_found', message: '关联订单不存在。' }
    }
    if (relatedQuoteId && !(await getWorkspaceDoc('quotes', relatedQuoteId, workspaceId))) {
      return { ok: false, code: 'not_found', message: '关联报价不存在。' }
    }
    if (relatedSkuId && !(await getWorkspaceDoc('sku_catalog', relatedSkuId, workspaceId))) {
      return { ok: false, code: 'not_found', message: '关联 SKU 不存在。' }
    }
    const entry = Object.assign({}, input, {
      workspaceId,
      direction,
      amount,
      category: limitText(input.category, MAX_SHORT_TEXT_LENGTH),
      paymentMethod: limitText(input.paymentMethod || input.channel, MAX_SHORT_TEXT_LENGTH),
      status,
      relatedOrderId,
      relatedOrderNo: limitText(input.relatedOrderNo || input.orderNo, MAX_SHORT_TEXT_LENGTH),
      relatedQuoteId,
      relatedSkuId,
      counterpartyName: limitText(input.counterpartyName || input.counterparty, MAX_SHORT_TEXT_LENGTH),
      operatorId,
      note: limitText(input.note, MAX_NOTE_TEXT_LENGTH),
      occurredAt: normalizeIsoDate(input.occurredAt, now()),
      createdBy: operatorId,
      updatedBy: operatorId
    })
    delete entry.channel
    delete entry.orderId
    delete entry.orderNo
    delete entry.quoteId
    delete entry.skuId
    delete entry.counterparty
    const saved = await upsertById('cashflow_entries', entry, workspaceId)
    if (!saved) return { ok: false, code: 'not_found', message: '现金流记录不存在。' }
    return { ok: true, entry: saved }
  }

  const deals = (await db.collection('deals').where({ workspaceId }).limit(1000).get()).data || []
  const customers = (await db.collection('customers').where({ workspaceId }).limit(1000).get()).data || []
  const customerMap = {}
  customers.forEach((customer) => { customerMap[customer._id] = customer })
  let created = 0

  for (const deal of deals) {
    const customer = customerMap[deal.customerId] || {}
    if (deal.dealStage === 'quote_sent' && deal.latestQuoteId) {
      const quote = await getWorkspaceDoc('quotes', deal.latestQuoteId, workspaceId)
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

exports.main = async (event) => {
  try {
    return await handleRequest(event)
  } catch (err) {
    logInternalError('followupScan failed', err)
    return { ok: false, code: 'internal_error', message: '跟进处理失败。' }
  }
}
