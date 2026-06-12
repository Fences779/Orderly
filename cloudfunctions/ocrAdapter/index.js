const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const ALLOWED_OPENIDS_ENV_NAME = 'ORDERLY_ALLOWED_OPENIDS'
const OPENID_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_OPENID_WORKSPACE_IDS'
const OPERATOR_PERMISSIONS_ENV_NAME = 'ORDERLY_OPERATOR_PERMISSIONS'
const MAX_EVENT_BYTES = 65536
const MAX_WORKSPACE_ID_LENGTH = 128
const MAX_FILE_ID_LENGTH = 512
const OCR_PROVIDERS = ['mock']
const OCR_PROCESS_PERMISSION = 'ocr:process'

function requireOperatorId() {
  const operatorId = cloud.getWXContext().OPENID || ''
  if (!operatorId) return { ok: false, code: 'unauthorized', message: '未授权调用。' }
  if (!isAllowedOperatorId(operatorId)) return { ok: false, code: 'forbidden', message: '无权访问。' }
  return { ok: true, operatorId }
}

function normalizeList(value) {
  return String(value || '')
    .split(/[,\s，、;；]+/)
    .map((item) => item.trim())
    .filter(Boolean)
}

function normalizeText(value, maxLength = 128) {
  if (value == null || typeof value === 'object') return ''
  return String(value).replace(/[\u0000-\u001f\u007f]/g, ' ').trim().slice(0, maxLength)
}

function normalizeProvider(value) {
  const provider = normalizeText(value || 'mock', 32).toLowerCase()
  return OCR_PROVIDERS.indexOf(provider) >= 0 ? provider : ''
}

function normalizeFileId(value) {
  const fileID = normalizeText(value, MAX_FILE_ID_LENGTH)
  return /[\r\n\u0000-\u001f\u007f]/.test(fileID) ? '' : fileID
}

function isAllowedOperatorId(operatorId) {
  const allowed = normalizeList(process.env[ALLOWED_OPENIDS_ENV_NAME])
  return allowed.indexOf(operatorId) >= 0
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

function requireOperatorPermission(operatorId, permission) {
  const permissions = resolveOperatorPermissions(operatorId)
  if (permissions.length === 0) return { ok: false, code: 'permission_binding_required', message: '操作权限未配置。' }
  if (permissions.indexOf('*') >= 0 || permissions.indexOf(permission) >= 0) return { ok: true }
  return { ok: false, code: 'permission_denied', message: '无权执行该操作。' }
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
  const permission = requireOperatorPermission(auth.operatorId, OCR_PROCESS_PERMISSION)
  if (!permission.ok) return permission

  const provider = normalizeProvider(event.provider || process.env.OCR_PROVIDER || 'mock')
  if (!provider) return { ok: false, code: 'unsupported_ocr_provider', message: 'OCR provider 不受支持。' }
  const fileID = normalizeFileId(event.fileID)
  if (event.fileID && !fileID) return { ok: false, code: 'invalid_file_id', message: 'fileID 非法。' }

  if (provider !== 'mock') {
    return {
      ok: true,
      provider: 'unconfigured',
      workspaceId: workspace.workspaceId,
      fileID,
      ocrText: '',
      confidenceScore: 0,
      message: '当前 OCR provider 未配置真实密钥，已降级为空文本，可在页面手动粘贴修正。'
    }
  }
  return {
    ok: true,
    provider: 'mock',
    workspaceId: workspace.workspaceId,
    fileID,
    ocrText: '客户：想定制一条简约珍珠项链，生日送人，这周要，预算 300 左右，白色，高级一点。',
    confidenceScore: 72,
    message: 'mock OCR 已返回示例文本，可直接修改后解析。'
  }
}

exports.main = async (event) => {
  try {
    return await handleRequest(event)
  } catch (err) {
    logInternalError('ocrAdapter failed', err)
    return { ok: false, code: 'internal_error', message: 'OCR 处理失败。' }
  }
}
