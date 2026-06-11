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
const MAX_REASON_TEXT_LENGTH = 512
const MAX_TASK_TEXT_LENGTH = 512

const TRANSITIONS = {
  new_inquiry: ['needs_clarification', 'quote_preparing', 'lost'],
  needs_clarification: ['quote_preparing', 'dormant', 'lost'],
  quote_preparing: ['quote_sent'],
  quote_sent: ['waiting_deposit', 'quote_preparing', 'dormant', 'lost'],
  waiting_deposit: ['scheduled', 'lost'],
  scheduled: ['in_production'],
  in_production: ['ready_to_ship'],
  ready_to_ship: ['shipped'],
  shipped: ['received'],
  received: ['completed'],
  completed: ['repurchase_due', 'dormant'],
  repurchase_due: [],
  dormant: ['new_inquiry', 'needs_clarification', 'quote_preparing'],
  lost: []
}

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
  const allowed = normalizeList(process.env[ALLOWED_OPENIDS_ENV_NAME])
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

function normalizeList(value) {
  return String(value || '')
    .split(/[,\s，、;；]+/)
    .map((item) => item.trim())
    .filter(Boolean)
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

function normalizeWorkspaceList(value) {
  return normalizeList(value).map(normalizeWorkspaceId).filter(Boolean)
}

function normalizeDocId(value) {
  if (value == null || typeof value === 'object') return ''
  const id = String(value).replace(/[\u0000-\u001f\u007f]/g, '').trim()
  return id.length <= MAX_DOC_ID_LENGTH && /^[A-Za-z0-9_-]+$/.test(id) ? id : ''
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

function addDays(days) {
  const date = new Date()
  date.setDate(date.getDate() + days)
  return date.toISOString()
}

function canTransition(fromStage, toStage) {
  if (!Object.prototype.hasOwnProperty.call(TRANSITIONS, toStage)) return false
  if (!Object.prototype.hasOwnProperty.call(TRANSITIONS, fromStage)) return false
  if (fromStage === toStage) return true
  return (TRANSITIONS[fromStage] || []).indexOf(toStage) >= 0
}

function priority(triggerType, deal, customer) {
  let score = triggerType === 'post_delivery' ? 30 : 35
  if (deal.urgencyLevel === 'high') score += 20
  if ((customer.totalSpent || 0) >= 500 || (customer.totalOrders || 0) > 1) score += 15
  return score
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

async function addLog(workspaceId, entityId, beforeData, afterData, note, operatorId) {
  await db.collection('activity_logs').add({
    data: { workspaceId, entityType: 'deal', entityId, actionType: 'stage_update', beforeData: sanitizeLogData(beforeData), afterData: sanitizeLogData(afterData), note, operatorId, createdAt: now() }
  })
}

async function createTaskIfMissing(workspaceId, deal, customer, triggerType, dueAt, suggestedText) {
  const dealId = normalizeDocId(deal._id)
  const customerId = normalizeDocId(deal.customerId)
  if (!dealId) return null
  const dedupeKey = dealId + ':' + triggerType + ':' + dueAt.slice(0, 10)
  const existed = await db.collection('followup_tasks').where({ workspaceId, dedupeKey }).limit(1).get()
  if (existed.data && existed.data.length) return null
  const task = {
    workspaceId,
    dealId,
    customerId,
    customerName: normalizeText(customer.name),
    triggerType,
    triggerAt: now(),
    dueAt,
    priorityScore: priority(triggerType, deal, customer),
    templateId: '',
    suggestedText: normalizeText(suggestedText, MAX_TASK_TEXT_LENGTH),
    taskStatus: 'pending',
    completedAt: '',
    resultType: '',
    dedupeKey,
    createdAt: now(),
    updatedAt: now(),
    createdBy: 'system',
    updatedBy: 'system'
  }
  const added = await db.collection('followup_tasks').add({ data: task })
  return Object.assign({}, task, { _id: added._id })
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
  const dealId = normalizeDocId(event.dealId)
  const toStage = normalizeText(event.toStage, 32)
  if (!dealId || !toStage) return { ok: false, message: '缺少 dealId 或 toStage' }
  const deal = (await db.collection('deals').doc(dealId).get()).data
  if (!deal || deal.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: 'deal 不存在。' }
  const fromStage = normalizeText(deal.dealStage, 32)
  if (!canTransition(fromStage, toStage)) return { ok: false, message: '非法状态流转：' + fromStage + ' -> ' + toStage }

  const before = { dealStage: fromStage, nextFollowupAt: deal.nextFollowupAt }
  const patch = { dealStage: toStage, updatedAt: now(), updatedBy: operatorId, lastInteractionAt: now() }
  if (toStage === 'lost') patch.lossReason = normalizeText(event.lossReason || deal.lossReason || '未填写', MAX_REASON_TEXT_LENGTH)
  if (toStage === 'received') patch.nextFollowupAt = addDays(3)
  if (toStage === 'completed') patch.nextFollowupAt = addDays(30)
  await db.collection('deals').doc(dealId).update({ data: patch })

  if (deal.dealStage === 'quote_sent' && toStage !== 'quote_sent') {
    const tasks = await db.collection('followup_tasks').where({ workspaceId, dealId, triggerType: 'quote_no_reply', taskStatus: 'pending' }).get()
    for (const task of tasks.data || []) {
      await db.collection('followup_tasks').doc(task._id).update({ data: { taskStatus: 'completed', completedAt: now(), resultType: 'auto_closed', updatedAt: now() } })
    }
  }

  const customerId = normalizeDocId(deal.customerId)
  let customer = {}
  if (customerId) {
    try {
      const customerDoc = (await db.collection('customers').doc(customerId).get()).data || {}
      customer = customerDoc.workspaceId === workspaceId ? customerDoc : {}
    } catch (err) {
      customer = {}
    }
  }
  const updatedDeal = Object.assign({}, deal, patch)
  let task = null
  if (toStage === 'received') {
    task = await createTaskIfMissing(workspaceId, updatedDeal, customer, 'post_delivery', addDays(3), (customer.name || '您好') + '，收到后佩戴感觉怎么样？尺寸、颜色如果有偏差可以直接告诉我。')
  }
  if (toStage === 'completed') {
    task = await createTaskIfMissing(workspaceId, updatedDeal, customer, 'repurchase', addDays(30), (customer.name || '您好') + '，上次这单已经完成一段时间了，如果想做同风格升级版，我可以按历史档案直接给你配。')
  }

  await addLog(workspaceId, dealId, before, { dealStage: toStage }, '状态变更', operatorId)
  return { ok: true, deal: updatedDeal, task }
}

exports.main = async (event) => {
  try {
    return await handleRequest(event)
  } catch (err) {
    logInternalError('dealStageUpdate failed', err)
    return { ok: false, code: 'internal_error', message: '状态更新失败。' }
  }
}
