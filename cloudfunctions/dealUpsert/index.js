const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const ALLOWED_OPENIDS_ENV_NAME = 'ORDERLY_ALLOWED_OPENIDS'
const MAX_EVENT_BYTES = 65536

const STAGES = ['new_inquiry', 'needs_clarification', 'quote_preparing', 'quote_sent', 'waiting_deposit', 'scheduled', 'in_production', 'ready_to_ship', 'shipped', 'received', 'completed', 'repurchase_due', 'dormant', 'lost']

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
  return allowed.length === 0 || allowed.indexOf(operatorId) >= 0
}

function rejectOversizedEvent(event) {
  const bytes = Buffer.byteLength(JSON.stringify(event || {}), 'utf8')
  return bytes > MAX_EVENT_BYTES ? { ok: false, code: 'payload_too_large', message: '请求体过大。' } : null
}

function normalizeArray(value) {
  if (!value) return []
  if (Array.isArray(value)) return value
  return String(value).split(/[,\s，、/]+/).map((item) => item.trim()).filter(Boolean)
}

function resolveWorkspaceId(event) {
  const workspaceId = String((event && event.workspaceId) || '').trim() || DEFAULT_WORKSPACE_ID
  const configured = normalizeArray(process.env[ALLOWED_WORKSPACE_IDS_ENV_NAME])
  const allowed = configured.length ? configured : [DEFAULT_WORKSPACE_ID]
  if (allowed.indexOf(workspaceId) < 0) return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  return { ok: true, workspaceId }
}

function money(value) {
  const num = Number(value || 0)
  return Number.isFinite(num) && num >= 0 ? num : 0
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

exports.main = async (event) => {
  const auth = requireOperatorId()
  if (!auth.ok) return auth

  event = event || {}
  const oversized = rejectOversizedEvent(event)
  if (oversized) return oversized

  const workspace = resolveWorkspaceId(event)
  if (!workspace.ok) return workspace
  const workspaceId = workspace.workspaceId
  const operatorId = auth.operatorId
  const input = event.deal || {}
  if (!input.customerId) return { ok: false, message: 'deal 必须关联 customerId' }
  if (!input.title && !input.demandSummary) return { ok: false, message: 'deal 标题或需求摘要不能为空' }
  const stage = input.dealStage || 'new_inquiry'
  if (STAGES.indexOf(stage) < 0) return { ok: false, message: '非法 dealStage' }

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
    dealStage: stage,
    styleTags: normalizeArray(input.styleTags),
    materialTags: normalizeArray(input.materialTags),
    riskFlags: normalizeArray(input.riskFlags),
    budgetMin: money(input.budgetMin),
    budgetMax: money(input.budgetMax),
    updatedAt: now(),
    updatedBy: operatorId
  })

  if (input._id) {
    const before = (await db.collection('deals').doc(input._id).get()).data
    if (!before || before.workspaceId !== workspaceId) return { ok: false, code: 'not_found', message: 'deal 不存在。' }
    delete data._id
    delete data.createdAt
    delete data.createdBy
    await db.collection('deals').doc(input._id).update({ data })
    const deal = Object.assign({}, before, data, { _id: input._id })
    await log(workspaceId, input._id, 'deal_update', before, deal, 'deal 信息更新', operatorId)
    return { ok: true, deal }
  }

  data.createdAt = now()
  const added = await db.collection('deals').add({ data })
  const deal = Object.assign({}, data, { _id: added._id })
  await log(workspaceId, added._id, 'deal_create', {}, deal, 'deal 创建', operatorId)
  return { ok: true, deal }
}
