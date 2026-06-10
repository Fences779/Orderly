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
const MAX_SHORT_TEXT_LENGTH = 128
const MAX_SUMMARY_TEXT_LENGTH = 512
const MAX_MONEY_AMOUNT = 100000000
const MAX_TAGS = 20
const STAGES = ['new_inquiry', 'needs_clarification', 'quote_preparing', 'quote_sent', 'waiting_deposit', 'scheduled', 'in_production', 'ready_to_ship', 'shipped', 'received', 'completed', 'repurchase_due', 'dormant', 'lost']
const URGENCY_LEVELS = ['low', 'medium', 'high']
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

function normalizeArray(value) {
  if (!value) return []
  if (Array.isArray(value)) return value
  return String(value).split(/[,\s，、/]+/).map((item) => item.trim()).filter(Boolean)
}

function normalizeText(value) {
  return normalizeTextWithLimit(value, MAX_SHORT_TEXT_LENGTH)
}

function normalizeTextWithLimit(value, maxLength) {
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

function normalizeDealStage(value) {
  const stage = normalizeText(value || 'new_inquiry')
  return STAGES.indexOf(stage) >= 0 ? stage : ''
}

function normalizeUrgencyLevel(value) {
  const urgencyLevel = normalizeText(value || 'low')
  return URGENCY_LEVELS.indexOf(urgencyLevel) >= 0 ? urgencyLevel : ''
}

function normalizePlatform(value) {
  const platform = normalizeText(value || 'wechat')
  return PLATFORMS.indexOf(platform) >= 0 ? platform : ''
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

async function addLog(workspaceId, entityType, entityId, actionType, beforeData, afterData, note, operatorId) {
  await db.collection('activity_logs').add({
    data: { workspaceId, entityType, entityId, actionType, beforeData: sanitizeLogData(beforeData), afterData: sanitizeLogData(afterData), note, operatorId, createdAt: now() }
  })
}

async function createCustomer(workspaceId, form, operatorId) {
  const customer = {
    workspaceId,
    name: normalizeText(form.customerName),
    platform: normalizePlatform(form.platform),
    externalUid: '',
    contactHandle: normalizeText(form.contactHandle),
    sourceChannel: 'capture',
    profileTags: [],
    preferenceNotes: '',
    tabooNotes: '',
    riskNotes: '',
    totalOrders: 0,
    totalSpent: 0,
    lastContactAt: now(),
    lastPurchaseAt: '',
    createdAt: now(),
    updatedAt: now(),
    createdBy: operatorId,
    updatedBy: operatorId
  }
  const added = await db.collection('customers').add({ data: customer })
  const full = Object.assign({}, customer, { _id: added._id })
  await addLog(workspaceId, 'customer', added._id, 'customer_create', {}, full, 'capture 确认建档', operatorId)
  return full
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
  const form = event.form || {}
  const captureId = normalizeText(event.captureId)
  if (!captureId) return { ok: false, message: '缺少 captureId' }
  const customerName = normalizeText(form.customerName)
  const demandSummary = normalizeTextWithLimit(form.demandSummary, MAX_SUMMARY_TEXT_LENGTH)
  const platform = normalizePlatform(form.platform)
  if (!customerName || !demandSummary) return { ok: false, message: '客户名和需求摘要必填' }
  if (!platform) return { ok: false, code: 'invalid_platform', message: '非法来源平台。' }
  const dealStage = normalizeDealStage(form.dealStage || 'new_inquiry')
  if (!dealStage) return { ok: false, code: 'invalid_deal_stage', message: '非法 dealStage。' }
  const urgencyLevel = normalizeUrgencyLevel(form.urgencyLevel || 'low')
  if (!urgencyLevel) return { ok: false, code: 'invalid_urgency_level', message: '非法 urgencyLevel。' }

  const capture = (await db.collection('captures').doc(captureId).get()).data
  if (!capture || capture.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: 'capture 不存在。' }
  if (capture.confirmStatus === 'confirmed' && capture.linkedDealId) {
    return { ok: true, customerId: capture.linkedCustomerId, dealId: capture.linkedDealId, message: 'capture 已确认' }
  }

  let customer
  if (event.customerMode === 'existing' && event.selectedCustomerId) {
    const selectedCustomerId = normalizeText(event.selectedCustomerId)
    if (!selectedCustomerId) return { ok: false, code: 'invalid_customer_id', message: '非法客户 ID。' }
    customer = (await db.collection('customers').doc(selectedCustomerId).get()).data
    if (!customer || customer.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: '客户不存在。' }
    await db.collection('customers').doc(selectedCustomerId).update({ data: { lastContactAt: now(), updatedAt: now(), updatedBy: operatorId } })
  } else {
    customer = await createCustomer(workspaceId, Object.assign({}, form, { customerName, platform }), operatorId)
  }

  const deal = {
    workspaceId,
    customerId: customer._id,
    title: normalizeText(form.title || demandSummary.slice(0, 24)),
    sourceEntry: platform || customer.platform || 'wechat',
    dealStage,
    priorityLevel: urgencyLevel === 'high' ? 'high' : 'medium',
    intentCategory: normalizeText(form.intentCategory),
    demandSummary,
    styleTags: normalizeLimitedArray(form.styleTags),
    materialTags: normalizeLimitedArray(form.materialTags),
    sizeSpec: normalizeText(form.sizeSpec),
    colorPref: normalizeText(form.colorPref),
    budgetMin: money(form.budgetMin),
    budgetMax: money(form.budgetMax),
    deadlineAt: normalizeTextWithLimit(form.deadlineAt, 64),
    urgencyLevel,
    riskFlags: normalizeLimitedArray(form.riskFlags),
    latestQuoteId: '',
    nextFollowupAt: '',
    lastInteractionAt: now(),
    followupCount: 0,
    lossReason: '',
    archivedAt: '',
    createdAt: now(),
    updatedAt: now(),
    createdBy: operatorId,
    updatedBy: operatorId
  }
  const addedDeal = await db.collection('deals').add({ data: deal })
  const dealId = addedDeal._id
  await db.collection('captures').doc(captureId).update({
    data: {
      parserResult: capture.parserResult,
      confirmStatus: 'confirmed',
      linkedCustomerId: customer._id,
      linkedDealId: dealId,
      updatedAt: now()
    }
  })
  await addLog(workspaceId, 'capture', captureId, 'capture_confirm', capture, { linkedCustomerId: customer._id, linkedDealId: dealId }, 'capture 确认入库', operatorId)
  await addLog(workspaceId, 'deal', dealId, 'deal_create', {}, Object.assign({}, deal, { _id: dealId }), 'capture 确认创建 deal', operatorId)

  return { ok: true, customerId: customer._id, dealId }
}

exports.main = async (event) => {
  try {
    return await handleRequest(event)
  } catch (err) {
    logInternalError('captureConfirm failed', err)
    return { ok: false, code: 'internal_error', message: '入库确认失败。' }
  }
}
