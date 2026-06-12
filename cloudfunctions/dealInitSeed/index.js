const cloud = require('wx-server-sdk')
const crypto = require('node:crypto')
const { createSeed } = require('./seedData')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()

const COLLECTIONS = ['customers', 'deals', 'quotes', 'sku_catalog', 'inventory_movements', 'cashflow_entries', 'followup_tasks', 'message_templates', 'captures', 'activity_logs']
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const OPENID_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_OPENID_WORKSPACE_IDS'
const OPERATOR_PERMISSIONS_ENV_NAME = 'ORDERLY_OPERATOR_PERMISSIONS'
const ENABLE_SEED_ENV_NAME = 'ORDERLY_ENABLE_DEAL_INIT_SEED'
const SEED_RUNTIME_ENV_NAME = 'ORDERLY_DEAL_INIT_SEED_RUNTIME'
const SEED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_DEAL_INIT_SEED_WORKSPACE_IDS'
const SEED_ADMIN_OPENIDS_ENV_NAME = 'ORDERLY_DEAL_INIT_SEED_OPENIDS'
const SEED_CONFIRMATION_ENV_NAME = 'ORDERLY_DEAL_INIT_SEED_CONFIRMATION'
const SEED_PERMISSION = 'seed:init'
const MAX_EVENT_BYTES = 65536
const MAX_WORKSPACE_ID_LENGTH = 128
const MIN_CONFIRMATION_LENGTH = 16

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

function normalizeText(value, maxLength = 256) {
  if (value == null || typeof value === 'object') return ''
  const text = String(value).replace(/[\u0000-\u001f\u007f]/g, '').trim()
  return text.length <= maxLength ? text : ''
}

function requireSeedAuthorization(event) {
  if (process.env[ENABLE_SEED_ENV_NAME] !== '1') {
    return { ok: false, code: 'seed_disabled', message: '演示数据初始化未启用。' }
  }

  const runtime = requireSeedRuntime()
  if (!runtime.ok) return runtime

  const operatorId = cloud.getWXContext().OPENID || ''
  const allowedOpenids = normalizeList(process.env[SEED_ADMIN_OPENIDS_ENV_NAME])
  if (!operatorId || !allowedOpenids.includes(operatorId)) {
    return { ok: false, code: 'unauthorized', message: '无权执行演示数据初始化。' }
  }

  if (!hasOperatorPermission(operatorId, SEED_PERMISSION)) {
    return { ok: false, code: 'permission_denied', message: '无权执行演示数据初始化。' }
  }

  if (!validateSeedConfirmation(event)) {
    return { ok: false, code: 'seed_confirmation_required', message: '缺少有效的演示数据初始化确认参数。' }
  }

  return { ok: true, operatorId }
}

function requireSeedRuntime() {
  const seedRuntime = normalizeText(process.env[SEED_RUNTIME_ENV_NAME]).toLowerCase()
  if (!['local', 'dev', 'development', 'test', 'qa', 'staging', 'sandbox'].includes(seedRuntime)) {
    return { ok: false, code: 'seed_runtime_required', message: '演示数据初始化必须配置为非生产 seed runtime。' }
  }

  const ambientRuntime = [
    process.env.ORDERLY_RUNTIME_ENV,
    process.env.NODE_ENV,
    process.env.TCB_ENV
  ].map((item) => normalizeText(item).toLowerCase()).filter(Boolean)
  if (ambientRuntime.some(isProductionRuntime)) {
    return { ok: false, code: 'seed_production_forbidden', message: '生产运行时禁止初始化演示数据。' }
  }

  return { ok: true }
}

function isProductionRuntime(value) {
  return /(^|[-_:])(prod|production|live)([-_:]|$)/.test(value)
}

function validateSeedConfirmation(event) {
  const expected = normalizeText(process.env[SEED_CONFIRMATION_ENV_NAME])
  if (expected.length < MIN_CONFIRMATION_LENGTH || isPlaceholderSecret(expected)) return false

  const supplied = normalizeText(event && (event.confirmation || event.confirmSeed || event.confirmationToken))
  if (supplied.length !== expected.length) return false

  const suppliedBytes = Buffer.from(supplied)
  const expectedBytes = Buffer.from(expected)
  return suppliedBytes.length === expectedBytes.length && crypto.timingSafeEqual(suppliedBytes, expectedBytes)
}

function isPlaceholderSecret(value) {
  return ['replace-me', 'changeme', 'change-me', 'test', 'token', 'password'].includes(value.trim().toLowerCase())
}

function hasOperatorPermission(operatorId, permission) {
  const permissions = resolveOperatorPermissions(operatorId)
  return permissions.includes('*') || permissions.includes(permission)
}

function resolveOperatorPermissions(operatorId) {
  if (!operatorId) return []
  const raw = String(process.env[OPERATOR_PERMISSIONS_ENV_NAME] || '').trim()
  if (!raw) return []

  if (raw[0] === '{') {
    try {
      const parsed = JSON.parse(raw)
      return normalizePermissionList(parsed && parsed[operatorId])
    } catch (err) {
      return []
    }
  }

  const entries = raw.split(/[;；\r\n]+/).map((item) => item.trim()).filter(Boolean)
  for (const entry of entries) {
    const separatorIndex = entry.indexOf('=') >= 0 ? entry.indexOf('=') : entry.indexOf(':')
    if (separatorIndex <= 0) continue
    const key = entry.slice(0, separatorIndex).trim()
    if (key === operatorId) return normalizePermissionList(entry.slice(separatorIndex + 1))
  }

  return []
}

function normalizePermissionList(value) {
  if (!value) return []
  const values = Array.isArray(value)
    ? value.map((item) => String(item).trim())
    : String(value).split(/[,\s，、|/]+/).map((item) => item.trim())
  return values.filter((item) => item === '*' || /^[a-z][a-z0-9:_-]{1,64}$/.test(item))
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
  const seedWorkspaces = normalizeWorkspaceList(process.env[SEED_WORKSPACE_IDS_ENV_NAME])
  if (seedWorkspaces.length === 0) {
    return { ok: false, code: 'seed_workspace_allowlist_required', message: '演示数据初始化工作区未配置。' }
  }

  if (seedWorkspaces.indexOf(workspaceId) < 0) return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }

  const configured = normalizeWorkspaceList(process.env[ALLOWED_WORKSPACE_IDS_ENV_NAME])
  const allowed = Array.from(new Set(configured.length ? configured : seedWorkspaces))
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
  delete data._id
  await db.collection(collection).doc(id).set({ data })
  return 'created'
}

async function findExistingSeedDoc(seed) {
  for (const collection of Object.keys(seed)) {
    for (const row of seed[collection]) {
      const result = await db.collection(collection).where({ _id: row._id }).limit(1).get()
      const existing = result.data && result.data.length > 0 ? result.data[0] : null
      if (existing) return { collection, id: row._id, workspaceId: existing.workspaceId || '' }
    }
  }

  return null
}

function logInternalError(scope, err) {
  const name = err && err.name ? String(err.name).slice(0, 64) : 'Error'
  console.error(scope, { name })
}

async function handleRequest(event) {
  const request = event || {}
  const oversized = rejectOversizedEvent(request)
  if (oversized) return oversized
  const polluted = rejectPollutedEvent(request)
  if (polluted) return polluted

  const auth = requireSeedAuthorization(request)
  if (!auth.ok) return auth

  const workspace = resolveWorkspaceId(request, auth.operatorId)
  if (!workspace.ok) return workspace
  const workspaceId = workspace.workspaceId
  await ensureCollections()
  const seed = createSeed(workspaceId)
  const existingSeed = await findExistingSeedDoc(seed)
  if (existingSeed) {
    return { ok: false, code: 'seed_target_not_empty', message: '演示数据目标已存在，已拒绝覆盖。' }
  }

  const counts = {}
  for (const collection of Object.keys(seed)) {
    counts[collection] = { created: 0 }
    for (const row of seed[collection]) {
      await upsertDoc(collection, row)
      counts[collection].created += 1
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
