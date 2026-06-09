const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const ALLOWED_OPENIDS_ENV_NAME = 'ORDERLY_ALLOWED_OPENIDS'
const OPENID_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_OPENID_WORKSPACE_IDS'
const AUTH_ALLOW_ALL_DEV_ENV_NAME = 'ORDERLY_AUTH_ALLOW_ALL_DEV'
const MAX_LIMIT = 200
const MAX_EVENT_BYTES = 65536
const COLLECTIONS = {
  customers: 'customers',
  deals: 'deals',
  quotes: 'quotes',
  sku_catalog: 'sku_catalog',
  inventory_movements: 'inventory_movements',
  cashflow_entries: 'cashflow_entries',
  followup_tasks: 'followup_tasks',
  message_templates: 'message_templates',
  captures: 'captures',
  activity_logs: 'activity_logs'
}
const QUERY_FIELDS = {
  customers: [],
  deals: [],
  quotes: ['dealId', 'quoteStatus'],
  sku_catalog: ['enabled', 'category'],
  inventory_movements: ['skuId', 'movementType', 'relatedOrderId'],
  cashflow_entries: ['direction', 'status', 'category', 'relatedOrderId', 'relatedQuoteId', 'relatedSkuId'],
  followup_tasks: ['dealId', 'customerId', 'taskStatus', 'triggerType', 'resultType'],
  message_templates: ['sceneType', 'enabled'],
  captures: ['confirmStatus', 'linkedCustomerId', 'linkedDealId'],
  activity_logs: ['entityId', 'entityType', 'actionType']
}
const ORDER_FIELDS = {
  customers: ['createdAt', 'updatedAt', 'lastContactAt', 'lastPurchaseAt'],
  deals: ['createdAt', 'updatedAt', 'lastInteractionAt', 'nextFollowupAt'],
  quotes: ['createdAt', 'updatedAt', 'sentAt', 'validUntil'],
  sku_catalog: ['createdAt', 'updatedAt', 'sortOrder', 'lastRestockedAt'],
  inventory_movements: ['createdAt', 'updatedAt', 'occurredAt'],
  cashflow_entries: ['createdAt', 'updatedAt', 'occurredAt'],
  followup_tasks: ['createdAt', 'updatedAt', 'dueAt', 'priorityScore'],
  message_templates: ['createdAt', 'updatedAt', 'sortOrder'],
  captures: ['createdAt', 'updatedAt'],
  activity_logs: ['createdAt']
}
const RESPONSE_FIELDS = {
  customers: ['_id', 'name', 'platform', 'externalUid', 'contactHandle', 'sourceChannel', 'profileTags', 'preferenceNotes', 'tabooNotes', 'riskNotes', 'totalOrders', 'totalSpent', 'lastContactAt', 'lastPurchaseAt', 'createdAt', 'updatedAt'],
  deals: ['_id', 'customerId', 'title', 'sourceEntry', 'dealStage', 'priorityLevel', 'intentCategory', 'demandSummary', 'styleTags', 'materialTags', 'sizeSpec', 'colorPref', 'budgetMin', 'budgetMax', 'deadlineAt', 'urgencyLevel', 'latestQuoteId', 'nextFollowupAt', 'lastInteractionAt', 'followupCount', 'lossReason', 'archivedAt', 'riskFlags', 'createdAt', 'updatedAt'],
  quotes: ['_id', 'dealId', 'quoteNo', 'quoteStatus', 'validUntil', 'quoteNote', 'sentAt', 'respondedAt', 'items', 'baseAmount', 'customFee', 'laborFee', 'shippingFee', 'discountAmount', 'depositRequired', 'totalAmount', 'createdAt', 'updatedAt'],
  sku_catalog: ['_id', 'name', 'title', 'category', 'basePrice', 'costPrice', 'specSchema', 'stockOnHand', 'stockReserved', 'safetyStock', 'stockUnit', 'stockLocation', 'tags', 'enabled', 'sortOrder', 'lastRestockedAt', 'createdAt', 'updatedAt'],
  inventory_movements: ['_id', 'skuId', 'skuName', 'movementType', 'quantity', 'relatedOrderId', 'relatedOrderNo', 'occurredAt', 'createdAt', 'updatedAt'],
  cashflow_entries: ['_id', 'direction', 'amount', 'category', 'paymentMethod', 'status', 'relatedOrderId', 'relatedOrderNo', 'relatedQuoteId', 'relatedSkuId', 'occurredAt', 'createdAt', 'updatedAt'],
  followup_tasks: ['_id', 'dealId', 'customerId', 'customerName', 'triggerType', 'triggerAt', 'dueAt', 'priorityScore', 'templateId', 'suggestedText', 'taskStatus', 'completedAt', 'resultType', 'createdAt', 'updatedAt'],
  message_templates: ['_id', 'title', 'name', 'scene', 'sceneType', 'content', 'variables', 'enabled', 'tags', 'sortOrder', 'useCount', 'createdAt', 'updatedAt'],
  captures: ['_id', 'parserResult', 'confidenceScore', 'confirmStatus', 'linkedCustomerId', 'linkedDealId', 'createdAt', 'updatedAt', 'createdBy'],
  activity_logs: ['_id', 'entityType', 'entityId', 'actionType', 'note', 'operatorId', 'createdAt']
}
const CAPTURE_DETAIL_FIELDS = RESPONSE_FIELDS.captures.concat(['rawText', 'ocrText'])
const QUOTE_ITEM_RESPONSE_FIELDS = ['name', 'qty', 'price', 'note', 'skuId', 'materialCode', 'unit']

function normalizeText(value) {
  return value == null ? '' : String(value).trim()
}

function normalizeList(value) {
  return String(value || '')
    .split(/[,\s，、;；]+/)
    .map((item) => item.trim())
    .filter(Boolean)
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

function resolveWorkspaceId(event, operatorId) {
  const workspaceId = normalizeText(event && event.workspaceId) || DEFAULT_WORKSPACE_ID
  const configured = normalizeList(process.env[ALLOWED_WORKSPACE_IDS_ENV_NAME])
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
  if (Array.isArray(value)) return value.map((item) => String(item).trim()).filter(Boolean)
  return String(value).split(/[,\s，、|/]+/).map((item) => item.trim()).filter(Boolean)
}

function resolveCollection(value) {
  const collection = normalizeText(value)
  return COLLECTIONS[collection] ? collection : ''
}

function isSafeFieldName(field) {
  return /^[A-Za-z0-9_]+$/.test(field) && field !== 'workspaceId'
}

function isAllowedField(collection, field, map) {
  return isSafeFieldName(field) && (map[collection] || []).indexOf(field) >= 0
}

function isSafeQueryValue(value) {
  return value == null || ['string', 'number', 'boolean'].indexOf(typeof value) >= 0
}

function sanitizeQuery(collection, query) {
  if (!query || typeof query !== 'object' || Array.isArray(query)) return {}
  return Object.keys(query).reduce((result, field) => {
    if (isAllowedField(collection, field, QUERY_FIELDS) && isSafeQueryValue(query[field])) result[field] = query[field]
    return result
  }, {})
}

function pickFields(source, fields) {
  const output = {}
  const input = source || {}
  fields.forEach((field) => {
    if (Object.prototype.hasOwnProperty.call(input, field)) output[field] = input[field]
  })
  return output
}

function projectQuoteItems(items) {
  if (!Array.isArray(items)) return []
  return items.slice(0, 100).map((item) => pickFields(item, QUOTE_ITEM_RESPONSE_FIELDS))
}

function projectRow(collection, row, detail) {
  if (!row) return null
  const fields = collection === 'captures' && detail ? CAPTURE_DETAIL_FIELDS : RESPONSE_FIELDS[collection]
  const projected = pickFields(row, fields || ['_id'])
  if (collection === 'quotes') {
    projected.items = projectQuoteItems(projected.items)
  }
  return projected
}

function sanitizeLimit(value) {
  const limit = Number(value || 100)
  if (!Number.isFinite(limit) || limit <= 0) return 100
  return Math.min(MAX_LIMIT, Math.floor(limit))
}

async function getById(collection, id, workspaceId) {
  const docId = normalizeText(id)
  if (!docId) return null
  try {
    const row = (await db.collection(collection).doc(docId).get()).data
    return row && row.workspaceId === workspaceId ? projectRow(collection, row, true) : null
  } catch (err) {
    return null
  }
}

async function listByQuery(collection, event, workspaceId) {
  const options = event.options || {}
  const query = Object.assign(sanitizeQuery(collection, event.query), { workspaceId })
  let ref = db.collection(collection).where(query)
  const orderField = normalizeText(options.orderBy && options.orderBy.field)
  if (isAllowedField(collection, orderField, ORDER_FIELDS)) {
    const direction = normalizeText(options.orderBy.direction) === 'asc' ? 'asc' : 'desc'
    ref = ref.orderBy(orderField, direction)
  }
  const rows = (await ref.limit(sanitizeLimit(options.limit)).get()).data || []
  return rows.map((row) => projectRow(collection, row, false)).filter(Boolean)
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

  const auth = requireOperatorId()
  if (!auth.ok) return auth

  const workspace = resolveWorkspaceId(event, auth.operatorId)
  if (!workspace.ok) return workspace

  const collection = resolveCollection(event.collection)
  if (!collection) return { ok: false, code: 'unsupported_collection', message: '不支持的集合。' }

  const action = normalizeText(event.action || 'list')
  if (action === 'getById') {
    const row = await getById(collection, event.id, workspace.workspaceId)
    return { ok: true, row }
  }
  if (action === 'list') {
    const rows = await listByQuery(collection, event, workspace.workspaceId)
    return { ok: true, rows }
  }
  return { ok: false, code: 'unsupported_action', message: '不支持的读取动作。' }
}

exports.main = async (event) => {
  try {
    return await handleRequest(event)
  } catch (err) {
    logInternalError('dataRead failed', err)
    return { ok: false, code: 'internal_error', message: '数据读取失败。' }
  }
}
