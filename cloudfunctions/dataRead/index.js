const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const ALLOWED_OPENIDS_ENV_NAME = 'ORDERLY_ALLOWED_OPENIDS'
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
  return allowed.length === 0 || allowed.indexOf(operatorId) >= 0
}

function rejectOversizedEvent(event) {
  const bytes = Buffer.byteLength(JSON.stringify(event || {}), 'utf8')
  return bytes > MAX_EVENT_BYTES ? { ok: false, code: 'payload_too_large', message: '请求体过大。' } : null
}

function resolveWorkspaceId(event) {
  const workspaceId = normalizeText(event && event.workspaceId) || DEFAULT_WORKSPACE_ID
  const configured = normalizeList(process.env[ALLOWED_WORKSPACE_IDS_ENV_NAME])
  const allowed = configured.length ? configured : [DEFAULT_WORKSPACE_ID]
  if (allowed.indexOf(workspaceId) < 0) return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  return { ok: true, workspaceId }
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
    return row && row.workspaceId === workspaceId ? row : null
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
  return (await ref.limit(sanitizeLimit(options.limit)).get()).data || []
}

function logInternalError(scope, err) {
  const name = err && err.name ? String(err.name).slice(0, 64) : 'Error'
  console.error(scope, { name })
}

async function handleRequest(event) {
  event = event || {}
  const oversized = rejectOversizedEvent(event)
  if (oversized) return oversized

  const auth = requireOperatorId()
  if (!auth.ok) return auth

  const workspace = resolveWorkspaceId(event)
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
