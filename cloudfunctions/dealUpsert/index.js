const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const ALLOWED_OPENIDS_ENV_NAME = 'ORDERLY_ALLOWED_OPENIDS'
const OPENID_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_OPENID_WORKSPACE_IDS'
const OPERATOR_PERMISSIONS_ENV_NAME = 'ORDERLY_OPERATOR_PERMISSIONS'
const MAX_EVENT_BYTES = 65536
const MAX_WORKSPACE_ID_LENGTH = 128
const MAX_DOC_ID_LENGTH = 128
const MAX_SHORT_TEXT_LENGTH = 128
const MAX_SUMMARY_TEXT_LENGTH = 512
const MAX_MONEY_AMOUNT = 100000000
const MAX_TAGS = 20

const STAGES = ['new_inquiry', 'needs_clarification', 'quote_preparing', 'quote_sent', 'waiting_deposit', 'scheduled', 'in_production', 'ready_to_ship', 'shipped', 'received', 'completed', 'repurchase_due', 'dormant', 'lost']
const DEAL_FIELDS = ['_id', 'customerId', 'title', 'sourceEntry', 'dealStage', 'priorityLevel', 'intentCategory', 'demandSummary', 'styleTags', 'materialTags', 'sizeSpec', 'colorPref', 'budgetMin', 'budgetMax', 'deadlineAt', 'urgencyLevel', 'lossReason', 'riskFlags']
const DEAL_WRITE_PERMISSION = 'deals:write'
const PLATFORMS = ['wechat', 'xianyu', 'xiaohongshu', 'douyin', 'offline', 'other']
const PRIORITY_LEVELS = ['low', 'medium', 'high']
const URGENCY_LEVELS = ['low', 'medium', 'high']

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

function normalizeEnum(value, allowed, fallback) {
  const text = normalizeText(value || fallback, 32)
  return allowed.indexOf(text) >= 0 ? text : ''
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
  const namespacePermission = permission.split(':')[0] + ':*'
  if (permissions.indexOf(namespacePermission) >= 0) return { ok: true }
  return { ok: false, code: 'permission_forbidden', message: '无权执行该操作。' }
}

function resolveOperatorPermissions(operatorId) {
  if (!operatorId) return []
  const raw = String(process.env[OPERATOR_PERMISSIONS_ENV_NAME] || '').trim()
  if (!raw) return []

  if (raw[0] === '{') {
    try {
      const parsed = JSON.parse(raw)
      return normalizePermissionBindingValue(parsed && parsed[operatorId])
    } catch (err) {
      return []
    }
  }

  const entries = raw.split(/[;；\r\n]+/).map((item) => item.trim()).filter(Boolean)
  for (const entry of entries) {
    const separatorIndex = entry.indexOf('=') >= 0 ? entry.indexOf('=') : entry.indexOf(':')
    if (separatorIndex <= 0) continue
    const key = entry.slice(0, separatorIndex).trim()
    if (key === operatorId) return normalizePermissionBindingValue(entry.slice(separatorIndex + 1))
  }

  return []
}

function normalizePermissionBindingValue(value) {
  if (!value) return []
  const values = Array.isArray(value)
    ? value.map((item) => String(item).trim())
    : String(value).split(/[,\s，、|/]+/).map((item) => item.trim())
  return values
    .map((item) => (item === '*' ? '*' : item.toLowerCase()))
    .filter((item) => item === '*' || /^[a-z][a-z0-9_-]*(?::[a-z0-9_*.-]+)?$/.test(item))
}

function money(value) {
  const num = Number(value || 0)
  if (!Number.isFinite(num) || num < 0) return 0
  return Math.min(num, MAX_MONEY_AMOUNT)
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
    data: { workspaceId, entityType: 'deal', entityId, actionType, beforeData: sanitizeLogData(beforeData), afterData: sanitizeLogData(afterData), note, operatorId, createdAt: now() }
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
  const permission = requireOperatorPermission(auth.operatorId, DEAL_WRITE_PERMISSION)
  if (!permission.ok) return permission

  const workspaceId = workspace.workspaceId
  const operatorId = auth.operatorId
  const input = pickFields(event.deal, DEAL_FIELDS)
  const customerId = normalizeDocId(input.customerId)
  const title = normalizeText(input.title || input.demandSummary)
  const demandSummary = normalizeText(input.demandSummary, MAX_SUMMARY_TEXT_LENGTH)
  if (!customerId) return { ok: false, message: 'deal 必须关联 customerId' }
  if (!title && !demandSummary) return { ok: false, message: 'deal 标题或需求摘要不能为空' }
  const stage = normalizeText(input.dealStage || 'new_inquiry', 32)
  if (STAGES.indexOf(stage) < 0) return { ok: false, message: '非法 dealStage' }
  const sourceEntry = normalizeEnum(input.sourceEntry, PLATFORMS, 'wechat')
  const priorityLevel = normalizeEnum(input.priorityLevel, PRIORITY_LEVELS, 'medium')
  const urgencyLevel = normalizeEnum(input.urgencyLevel, URGENCY_LEVELS, 'low')
  if (!sourceEntry) return { ok: false, code: 'invalid_source_entry', message: '非法来源平台。' }
  if (!priorityLevel) return { ok: false, code: 'invalid_priority_level', message: '非法优先级。' }
  if (!urgencyLevel) return { ok: false, code: 'invalid_urgency_level', message: '非法紧急程度。' }

  const customer = (await db.collection('customers').doc(customerId).get()).data
  if (!customer || customer.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: '客户不存在。' }

  const data = Object.assign({
    workspaceId,
    title: input.demandSummary || '新需求',
    sourceEntry: 'wechat',
    dealStage: stage,
    priorityLevel: 'medium',
    intentCategory: '',
    demandSummary: '',
    styleTags: [],
    materialTags: [],
    sizeSpec: '',
    colorPref: '',
    budgetMin: 0,
    budgetMax: 0,
    deadlineAt: '',
    urgencyLevel: 'low',
    latestQuoteId: '',
    nextFollowupAt: '',
    lastInteractionAt: now(),
    followupCount: 0,
    lossReason: '',
    archivedAt: '',
    createdBy: operatorId,
    updatedBy: operatorId
  }, input, {
    workspaceId,
    customerId,
    title: title || demandSummary || '新需求',
    sourceEntry,
    dealStage: stage,
    priorityLevel,
    intentCategory: normalizeText(input.intentCategory),
    demandSummary,
    styleTags: normalizeLimitedArray(input.styleTags),
    materialTags: normalizeLimitedArray(input.materialTags),
    sizeSpec: normalizeText(input.sizeSpec),
    colorPref: normalizeText(input.colorPref),
    deadlineAt: normalizeText(input.deadlineAt, 64),
    urgencyLevel,
    lossReason: normalizeText(input.lossReason, MAX_SUMMARY_TEXT_LENGTH),
    riskFlags: normalizeLimitedArray(input.riskFlags),
    budgetMin: money(input.budgetMin),
    budgetMax: money(input.budgetMax),
    updatedAt: now(),
    updatedBy: operatorId
  })

  if (input._id) {
    const dealId = normalizeDocId(input._id)
    if (!dealId) return { ok: false, code: 'invalid_deal_id', message: '非法 dealId。' }
    const before = (await db.collection('deals').doc(dealId).get()).data
    if (!before || before.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: 'deal 不存在。' }
    delete data._id
    delete data.createdAt
    delete data.createdBy
    delete data.dealStage
    delete data.latestQuoteId
    delete data.nextFollowupAt
    delete data.lastInteractionAt
    delete data.followupCount
    delete data.archivedAt
    await db.collection('deals').doc(dealId).update({ data })
    const deal = Object.assign({}, before, data, { _id: dealId })
    await log(workspaceId, dealId, 'deal_update', before, deal, 'deal 信息更新', operatorId)
    return { ok: true, deal }
  }

  data.createdAt = now()
  const added = await db.collection('deals').add({ data })
  const deal = Object.assign({}, data, { _id: added._id })
  await log(workspaceId, added._id, 'deal_create', {}, deal, 'deal 创建', operatorId)
  return { ok: true, deal }
}

exports.main = async (event) => {
  try {
    return await handleRequest(event)
  } catch (err) {
    logInternalError('dealUpsert failed', err)
    return { ok: false, code: 'internal_error', message: 'deal 保存失败。' }
  }
}
