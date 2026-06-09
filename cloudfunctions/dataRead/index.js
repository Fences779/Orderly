const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
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
  return { ok: true, operatorId }
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

function isSafeQueryField(field) {
  return /^[A-Za-z0-9_]+$/.test(field) && field !== 'workspaceId'
}

function isSafeQueryValue(value) {
  return value == null || ['string', 'number', 'boolean'].indexOf(typeof value) >= 0
}

function sanitizeQuery(query) {
  if (!query || typeof query !== 'object' || Array.isArray(query)) return {}
  return Object.keys(query).reduce((result, field) => {
    if (isSafeQueryField(field) && isSafeQueryValue(query[field])) result[field] = query[field]
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
  const query = Object.assign(sanitizeQuery(event.query), { workspaceId })
  let ref = db.collection(collection).where(query)
  if (options.orderBy && /^[A-Za-z0-9_]+$/.test(normalizeText(options.orderBy.field))) {
    const direction = normalizeText(options.orderBy.direction) === 'asc' ? 'asc' : 'desc'
    ref = ref.orderBy(normalizeText(options.orderBy.field), direction)
  }
  return (await ref.limit(sanitizeLimit(options.limit)).get()).data || []
}

exports.main = async (event) => {
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
