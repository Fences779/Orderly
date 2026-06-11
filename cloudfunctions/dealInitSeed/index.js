const cloud = require('wx-server-sdk')
const { createSeed } = require('./seedData')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()

const COLLECTIONS = ['customers', 'deals', 'quotes', 'sku_catalog', 'inventory_movements', 'cashflow_entries', 'followup_tasks', 'message_templates', 'captures', 'activity_logs']
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const OPENID_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_OPENID_WORKSPACE_IDS'
const ENABLE_SEED_ENV_NAME = 'ORDERLY_ENABLE_DEAL_INIT_SEED'
const SEED_ADMIN_OPENIDS_ENV_NAME = 'ORDERLY_DEAL_INIT_SEED_OPENIDS'
const MAX_EVENT_BYTES = 65536
const MAX_WORKSPACE_ID_LENGTH = 128

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

function requireSeedAuthorization() {
  if (process.env[ENABLE_SEED_ENV_NAME] !== '1') {
    return { ok: false, code: 'seed_disabled', message: '演示数据初始化未启用。' }
  }

  const operatorId = cloud.getWXContext().OPENID || ''
  const allowedOpenids = normalizeList(process.env[SEED_ADMIN_OPENIDS_ENV_NAME])
  if (!operatorId || !allowedOpenids.includes(operatorId)) {
    return { ok: false, code: 'unauthorized', message: '无权执行演示数据初始化。' }
  }

  return { ok: true, operatorId }
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

  return { ok: false, code: 'workspace_binding_required', message: '工作区权限未配置。' }
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

async function ensureCollections() {
  for (const name of COLLECTIONS) {
    try {
      await db.createCollection(name)
    } catch (err) {
      // Collection already exists or current account has no explicit create permission.
    }
  }
}

async function upsertDoc(collection, row) {
  const data = Object.assign({}, row)
  const id = data._id
  let existing = null
  try {
    existing = (await db.collection(collection).doc(id).get()).data || null
  } catch (err) {
    existing = null
  }

  if (existing) {
    if (existing.workspaceId !== row.workspaceId) return 'workspace_conflict'
    delete data._id
    await db.collection(collection).doc(id).update({ data })
    return 'updated'
  }

  delete data._id
  await db.collection(collection).doc(id).set({ data })
  return 'created'
}

function logInternalError(scope, err) {
  const name = err && err.name ? String(err.name).slice(0, 64) : 'Error'
  console.error(scope, { name })
}

async function handleRequest(event) {
  const auth = requireSeedAuthorization()
  if (!auth.ok) return auth

  const request = event || {}
  const oversized = rejectOversizedEvent(request)
  if (oversized) return oversized
  const polluted = rejectPollutedEvent(request)
  if (polluted) return polluted

  const workspace = resolveWorkspaceId(request, auth.operatorId)
  if (!workspace.ok) return workspace
  const workspaceId = workspace.workspaceId
  await ensureCollections()
  const seed = createSeed(workspaceId)
  const counts = {}
  for (const collection of Object.keys(seed)) {
    counts[collection] = { created: 0, updated: 0 }
    for (const row of seed[collection]) {
      const result = await upsertDoc(collection, row)
      if (result === 'workspace_conflict') {
        return { ok: false, code: 'seed_workspace_conflict', message: '演示数据 ID 已属于其他工作区，已拒绝覆盖。' }
      }
      counts[collection][result] += 1
    }
  }
  return {
    ok: true,
    counts,
    message: '演示数据已写入，可从工作台开始验收。'
  }
}

exports.main = async (event) => {
  try {
    return await handleRequest(event)
  } catch (err) {
    logInternalError('dealInitSeed failed', err)
    return { ok: false, code: 'internal_error', message: '演示数据初始化失败。' }
  }
}
