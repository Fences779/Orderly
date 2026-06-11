const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const ALLOWED_OPENIDS_ENV_NAME = 'ORDERLY_ALLOWED_OPENIDS'
const OPENID_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_OPENID_WORKSPACE_IDS'
const MAX_EVENT_BYTES = 65536
const MAX_WORKSPACE_ID_LENGTH = 128
const MAX_DOC_ID_LENGTH = 128
const MAX_SHORT_TEXT_LENGTH = 128
const MAX_NOTE_TEXT_LENGTH = 512
const MAX_TAGS = 20
const CUSTOMER_FIELDS = ['_id', 'name', 'platform', 'externalUid', 'contactHandle', 'sourceChannel', 'profileTags', 'preferenceNotes', 'tabooNotes', 'riskNotes']
const PLATFORMS = ['wechat', 'xianyu', 'xiaohongshu', 'douyin', 'offline', 'other']

function now() {
  return new Date().toISOString()
}

function requireOperatorId() {
  const operatorId = cloud.getWXContext().OPENID || ''
  if (!operatorId) return { ok: false, code: 'unauthorized', message: '未授权调用。' }
  if (!isAllowedOperatorId(operatorId)) return { ok: false, code: 'forbidden', message: '无权访问。' }
  return { ok: true, operatorId }
}

function isAllowedOperatorId(operatorId) {
  const allowed = normalizeArray(process.env[ALLOWED_OPENIDS_ENV_NAME])
  return allowed.indexOf(operatorId) >= 0
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

function normalizeArray(value) {
  if (!value) return []
  if (Array.isArray(value)) return value
  return String(value).split(/[,\s，、/]+/).map((item) => item.trim()).filter(Boolean)
}

function normalizeText(value, maxLength = MAX_SHORT_TEXT_LENGTH) {
  if (value == null || typeof value === 'object') return ''
  return String(value).replace(/[\u0000-\u001f\u007f]/g, ' ').trim().slice(0, maxLength)
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

function normalizeLimitedArray(value, maxItems = MAX_TAGS) {
  const source = Array.isArray(value)
    ? value
    : String(value || '').split(/[,\s，、;；|/]+/)
  const seen = Object.create(null)
  return source
    .map((item) => normalizeText(item))
    .filter((item) => item && !seen[item] && (seen[item] = true))
    .slice(0, maxItems)
}

function normalizePlatform(value) {
  const platform = normalizeText(value || 'wechat', 32)
  return PLATFORMS.indexOf(platform) >= 0 ? platform : ''
}

function pickFields(source, allowedFields) {
  const result = {}
  const input = source || {}
  allowedFields.forEach((field) => {
    if (Object.prototype.hasOwnProperty.call(input, field)) result[field] = input[field]
  })
  return result
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

const REDACTED_LOG_VALUE = '[redacted]'
const SAFE_LOG_TEXT_KEYS = ['_id', 'workspaceId', 'customerId', 'dealId', 'quoteId', 'latestQuoteId', 'linkedCustomerId', 'linkedDealId', 'dealStage', 'quoteStatus', 'confirmStatus', 'taskStatus', 'resultType', 'triggerType', 'actionType', 'priorityLevel', 'urgencyLevel', 'sourceEntry', 'platform', 'createdAt', 'updatedAt', 'sentAt', 'respondedAt', 'validUntil', 'lastContactAt', 'lastPurchaseAt', 'deadlineAt', 'nextFollowupAt', 'archivedAt', 'createdBy', 'updatedBy']

function isSafeLogTextKey(key) {
  return SAFE_LOG_TEXT_KEYS.indexOf(key) >= 0 || /Id$/.test(key || '') || /At$/.test(key || '')
}

function sanitizeLogData(value, key) {
  if (value == null) return value
  if (Array.isArray(value)) return { redactedArrayLength: value.length }
  if (typeof value === 'object') {
    return Object.keys(value).reduce((result, field) => {
      result[field] = sanitizeLogData(value[field], field)
      return result
    }, {})
  }
  if (typeof value === 'string') return isSafeLogTextKey(key) ? value : REDACTED_LOG_VALUE
  return value
}

async function log(workspaceId, entityId, actionType, beforeData, afterData, note, operatorId) {
  await db.collection('activity_logs').add({
    data: { workspaceId, entityType: 'customer', entityId, actionType, beforeData: sanitizeLogData(beforeData), afterData: sanitizeLogData(afterData), note, operatorId, createdAt: now() }
  })
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
  const operatorId = auth.operatorId
  const input = pickFields(event.customer, CUSTOMER_FIELDS)
  const name = normalizeText(input.name)
  const platform = normalizePlatform(input.platform)
  const externalUid = normalizeText(input.externalUid)
  const contactHandle = normalizeText(input.contactHandle)
  if (!name) return { ok: false, message: '客户名不能为空' }
  if (!platform) return { ok: false, code: 'invalid_platform', message: '非法客户来源平台。' }
  const data = Object.assign({
    workspaceId,
    platform: 'wechat',
    externalUid: '',
    contactHandle: '',
    sourceChannel: '',
    profileTags: [],
    preferenceNotes: '',
    tabooNotes: '',
    riskNotes: '',
    totalOrders: 0,
    totalSpent: 0,
    lastContactAt: now(),
    lastPurchaseAt: '',
    createdBy: operatorId,
    updatedBy: operatorId
  }, input, {
    workspaceId,
    name,
    platform,
    externalUid,
    contactHandle,
    sourceChannel: normalizeText(input.sourceChannel, 64),
    profileTags: normalizeLimitedArray(input.profileTags),
    preferenceNotes: normalizeText(input.preferenceNotes, MAX_NOTE_TEXT_LENGTH),
    tabooNotes: normalizeText(input.tabooNotes, MAX_NOTE_TEXT_LENGTH),
    riskNotes: normalizeText(input.riskNotes, MAX_NOTE_TEXT_LENGTH),
    updatedAt: now(),
    updatedBy: operatorId
  })

  let existing = null
  if (input._id) {
    const customerId = normalizeDocId(input._id)
    if (!customerId) return { ok: false, code: 'invalid_customer_id', message: '非法客户 ID。' }
    try {
      existing = (await db.collection('customers').doc(customerId).get()).data
    } catch (err) {}
    if (!existing || existing.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: '客户不存在。' }
  }
  if (!existing && platform && externalUid) {
    const matched = await db.collection('customers').where({ workspaceId, platform, externalUid }).limit(1).get()
    existing = matched.data && matched.data[0]
  }
  if (!existing && contactHandle) {
    const matched = await db.collection('customers').where({ workspaceId, contactHandle }).limit(1).get()
    existing = matched.data && matched.data[0]
  }

  if (existing) {
    const id = existing._id
    delete data._id
    delete data.createdAt
    delete data.createdBy
    delete data.totalOrders
    delete data.totalSpent
    delete data.lastContactAt
    delete data.lastPurchaseAt
    await db.collection('customers').doc(id).update({ data })
    const customer = Object.assign({}, existing, data, { _id: id })
    await log(workspaceId, id, 'customer_update', existing, customer, '客户档案更新', operatorId)
    return { ok: true, customer }
  }

  data.createdAt = now()
  const added = await db.collection('customers').add({ data })
  const customer = Object.assign({}, data, { _id: added._id })
  await log(workspaceId, added._id, 'customer_create', {}, customer, '客户建档', operatorId)
  return { ok: true, customer }
}

exports.main = async (event) => {
  try {
    return await handleRequest(event)
  } catch (err) {
    logInternalError('customerUpsert failed', err)
    return { ok: false, code: 'internal_error', message: '客户保存失败。' }
  }
}
