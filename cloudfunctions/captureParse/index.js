const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const ALLOWED_OPENIDS_ENV_NAME = 'ORDERLY_ALLOWED_OPENIDS'
const OPENID_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_OPENID_WORKSPACE_IDS'
const MAX_EVENT_BYTES = 65536
const MAX_WORKSPACE_ID_LENGTH = 128
const MAX_SHORT_TEXT_LENGTH = 128
const MAX_SUMMARY_TEXT_LENGTH = 512
const MAX_CAPTURE_TEXT_LENGTH = 20000
const MAX_IMAGE_URL_LENGTH = 1024
const MAX_SOURCE_TYPE_LENGTH = 32
const MAX_TAGS = 20
const URGENCY_LEVELS = ['low', 'medium', 'high']
const SUGGESTED_STAGES = ['new_inquiry', 'needs_clarification', 'quote_preparing']
const CUSTOMER_MATCH_FIELDS = ['_id', 'name', 'platform', 'contactHandle']

const STYLE_WORDS = ['简约', '高级', '复古', '甜酷', '清冷', '温柔', '国风', '通勤', '可爱', '低调', '显白']
const MATERIAL_WORDS = ['珍珠', '水晶', '玛瑙', '银', '14k', '18k', '朱砂', '檀木', '贝母', '琉璃', '天然石']
const COLOR_WORDS = ['白色', '黑色', '金色', '银色', '蓝色', '绿色', '红色', '粉色', '紫色', '棕色']
const CATEGORY_WORDS = [
  { label: '手串', words: ['手串', '串珠', '珠串'] },
  { label: '项链', words: ['项链', '吊坠', '锁骨链'] },
  { label: '礼物', words: ['礼物', '送人', '生日', '纪念日'] },
  { label: '配件', words: ['配件', '挂件', '钥匙扣'] },
  { label: '定制', words: ['定制', '订做', '按图', '改款'] }
]
const RISK_WORDS = [
  { key: 'think_again', label: '我再想想', words: ['我再想想', '再考虑', '考虑一下'] },
  { key: 'just_looking', label: '先看看', words: ['先看看', '看一下', '了解一下'] },
  { key: 'price_sensitive', label: '价格敏感', words: ['有点贵', '太贵', '能便宜吗', '优惠点', '便宜点'] },
  { key: 'compare_others', label: '比价中', words: ['问问别人', '对比一下', '别家'] },
  { key: 'not_urgent', label: '不着急', words: ['暂时不着急', '不急', '以后再说'] }
]

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

function normalizeCaptureText(value, maxLength = MAX_CAPTURE_TEXT_LENGTH) {
  if (value == null || typeof value === 'object') return ''
  return String(value)
    .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, ' ')
    .trim()
    .slice(0, maxLength)
}

function pickFields(source, allowedFields) {
  const result = {}
  const input = source || {}
  allowedFields.forEach((field) => {
    if (Object.prototype.hasOwnProperty.call(input, field)) result[field] = input[field]
  })
  return result
}

function normalizeWorkspaceId(value) {
  if (value == null || typeof value === 'object') return ''
  const id = String(value).replace(/[\u0000-\u001f\u007f]/g, '').trim()
  return id.length <= MAX_WORKSPACE_ID_LENGTH && /^[A-Za-z0-9_.:-]+$/.test(id) ? id : ''
}

function normalizeWorkspaceList(value) {
  return normalizeList(value).map(normalizeWorkspaceId).filter(Boolean)
}

function normalizeTagList(value) {
  const source = Array.isArray(value)
    ? value
    : String(value || '').split(/[,\s，、;；|/]+/)
  return unique(source.map((item) => normalizeText(item)).filter(Boolean)).slice(0, MAX_TAGS)
}

function normalizeRiskFlags(value) {
  const flags = Array.isArray(value) ? value : []
  return flags.slice(0, MAX_TAGS).map((flag) => ({
    key: normalizeText(flag && flag.key),
    label: normalizeText(flag && flag.label)
  })).filter((flag) => flag.key || flag.label)
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

function unique(list) {
  const seen = {}
  return list.filter((item) => item && !seen[item] && (seen[item] = true))
}

function collect(text, words) {
  return words.filter((word) => text.indexOf(word) >= 0)
}

function category(text) {
  const hit = CATEGORY_WORDS.find((group) => group.words.some((word) => text.indexOf(word) >= 0))
  return hit ? hit.label : ''
}

function nameHint(text) {
  const named = text.match(/(?:我是|我叫|昵称|客户|买家)[:：\s]*([\u4e00-\u9fa5A-Za-z0-9_-]{2,12})/)
  if (named) return named[1]
  const speaker = text.match(/([\u4e00-\u9fa5A-Za-z0-9_-]{2,12})[:：]\s*/)
  return speaker ? speaker[1] : ''
}

function contactHint(text) {
  const wxHandle = text.match(/(?:微信|vx|VX|v)[:：\s]*([A-Za-z0-9_-]{5,32})/)
  if (wxHandle) return wxHandle[1]
  const phone = text.match(/1[3-9]\d{9}/)
  return phone ? phone[0] : ''
}

function budget(text) {
  let budgetMin = 0
  let budgetMax = 0
  const around = text.match(/(\d{2,6})\s*(?:左右|上下|附近)/)
  const under = text.match(/(\d{2,6})\s*(?:以内|以下|内)/)
  const range = text.match(/(\d{2,6})\s*[-到至]\s*(\d{2,6})/)
  if (range) {
    budgetMin = Number(range[1])
    budgetMax = Number(range[2])
  } else if (around) {
    const base = Number(around[1])
    budgetMin = Math.round(base * 0.85)
    budgetMax = Math.round(base * 1.15)
  } else if (under) {
    budgetMax = Number(under[1])
  } else if (text.indexOf('预算不高') >= 0 || text.indexOf('别太贵') >= 0 || text.indexOf('便宜点') >= 0) {
    budgetMax = 200
  }
  return { budgetMin, budgetMax }
}

function deadline(text) {
  const date = new Date()
  if (text.indexOf('后天') >= 0) {
    date.setDate(date.getDate() + 2)
    return date.toISOString()
  }
  if (text.indexOf('明天') >= 0) {
    date.setDate(date.getDate() + 1)
    return date.toISOString()
  }
  if (text.indexOf('这周') >= 0 || text.indexOf('周末') >= 0) return addDays(6 - date.getDay())
  if (text.indexOf('月底') >= 0) return new Date(date.getFullYear(), date.getMonth() + 1, 0, 18, 0, 0).toISOString()
  if (text.indexOf('尽快') >= 0 || text.indexOf('急') >= 0) return addDays(3)
  return ''
}

function risk(text) {
  return RISK_WORDS.filter((group) => group.words.some((word) => text.indexOf(word) >= 0)).map((group) => ({ key: group.key, label: group.label }))
}

function normalizeMoney(value) {
  const num = Number(value || 0)
  return Number.isFinite(num) && num >= 0 ? num : 0
}

function normalizeParserResult(value) {
  const input = value && typeof value === 'object' ? value : {}
  const hints = input.customerHints && typeof input.customerHints === 'object' ? input.customerHints : {}
  const urgencyLevel = normalizeText(input.urgencyLevel || 'low', 16)
  const suggestedStage = normalizeText(input.suggestedStage || 'needs_clarification', 32)
  const confidenceScore = Number(input.confidenceScore || 0)

  return {
    customerHints: {
      name: normalizeText(hints.name),
      externalUid: normalizeText(hints.externalUid),
      contactHandle: normalizeText(hints.contactHandle),
      platformHint: normalizeText(hints.platformHint, 32)
    },
    intentCategory: normalizeText(input.intentCategory),
    demandSummary: normalizeText(input.demandSummary, MAX_SUMMARY_TEXT_LENGTH),
    styleTags: normalizeTagList(input.styleTags),
    materialTags: normalizeTagList(input.materialTags),
    sizeSpec: normalizeText(input.sizeSpec),
    colorPref: normalizeText(input.colorPref),
    deadlineAt: normalizeText(input.deadlineAt, 64),
    urgencyLevel: URGENCY_LEVELS.indexOf(urgencyLevel) >= 0 ? urgencyLevel : 'low',
    riskFlags: normalizeRiskFlags(input.riskFlags),
    suggestedStage: SUGGESTED_STAGES.indexOf(suggestedStage) >= 0 ? suggestedStage : 'needs_clarification',
    budgetMin: normalizeMoney(input.budgetMin),
    budgetMax: normalizeMoney(input.budgetMax),
    confidenceScore: Number.isFinite(confidenceScore) ? Math.max(0, Math.min(100, Math.round(confidenceScore))) : 0
  }
}

function parseText(rawText) {
  const text = String(rawText || '').trim()
  const riskFlags = risk(text)
  const result = {
    customerHints: {
      name: nameHint(text),
      externalUid: '',
      contactHandle: contactHint(text),
      platformHint: text.indexOf('闲鱼') >= 0 ? 'xianyu' : ''
    },
    intentCategory: category(text),
    demandSummary: text.split(/\n/).map((line) => line.trim()).filter(Boolean)[0] || text.slice(0, 48),
    styleTags: unique(collect(text, STYLE_WORDS)),
    materialTags: unique(collect(text, MATERIAL_WORDS)),
    sizeSpec: unique((text.match(/\d+(?:\.\d+)?\s*(?:cm|厘米|mm|毫米|颗|粒|件|个)/gi) || []).concat(text.match(/手围\s*\d+(?:\.\d+)?/g) || [])).join('，'),
    colorPref: unique(collect(text, COLOR_WORDS)).join('，'),
    deadlineAt: deadline(text),
    urgencyLevel: text.indexOf('急') >= 0 || text.indexOf('明天') >= 0 || text.indexOf('后天') >= 0 ? 'high' : (text.indexOf('这周') >= 0 || text.indexOf('生日') >= 0 ? 'medium' : 'low'),
    riskFlags,
    suggestedStage: riskFlags.length ? 'needs_clarification' : 'quote_preparing'
  }
  Object.assign(result, budget(text))
  let score = 28
  if (result.customerHints.name) score += 12
  if (result.intentCategory) score += 12
  if (result.styleTags.length) score += 8
  if (result.materialTags.length) score += 8
  if (result.budgetMax) score += 8
  if (result.deadlineAt) score += 8
  if (text.length > 18) score += 6
  result.confidenceScore = Math.min(96, score)
  return result
}

async function matchCustomers(workspaceId, parserResult) {
  const customers = await db.collection('customers').where({ workspaceId }).limit(100).get()
  const hints = parserResult.customerHints || {}
  return (customers.data || []).filter((customer) => {
    if (hints.platformHint && hints.externalUid && customer.platform === hints.platformHint && customer.externalUid === hints.externalUid) return true
    if (hints.contactHandle && customer.contactHandle === hints.contactHandle) return true
    if (hints.name && customer.name === hints.name) return true
    if (hints.platformHint && hints.name && customer.platform === hints.platformHint && customer.name.indexOf(hints.name) >= 0) return true
    return false
  }).slice(0, 5).map((customer) => pickFields(customer, CUSTOMER_MATCH_FIELDS))
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
  const rawText = normalizeCaptureText(event.rawText)
  const ocrText = normalizeCaptureText(event.ocrText)
  const rawImageUrl = normalizeText(event.rawImageUrl, MAX_IMAGE_URL_LENGTH)
  const sourceType = normalizeText(event.sourceType || 'clipboard', MAX_SOURCE_TYPE_LENGTH) || 'clipboard'
  const parserResult = normalizeParserResult(event.parserResult || parseText(rawText || ocrText))
  const customerMatches = await matchCustomers(workspaceId, parserResult)
  if (event.mode === 'matchOnly') {
    return { ok: true, parserResult, customerMatches }
  }
  if (!rawText && !ocrText) return { ok: false, message: '缺少可解析文本' }
  const capture = {
    workspaceId,
    sourceType,
    rawText,
    rawImageUrl,
    ocrText,
    parserResult,
    confidenceScore: parserResult.confidenceScore,
    confirmStatus: 'draft',
    linkedCustomerId: '',
    linkedDealId: '',
    createdAt: now(),
    updatedAt: now(),
    createdBy: operatorId
  }
  const created = await db.collection('captures').add({ data: capture })
  return {
    ok: true,
    captureId: created._id,
    parserResult,
    confidenceScore: parserResult.confidenceScore,
    customerMatches
  }
}

exports.main = async (event) => {
  try {
    return await handleRequest(event)
  } catch (err) {
    logInternalError('captureParse failed', err)
    return { ok: false, code: 'internal_error', message: '素材解析失败。' }
  }
}
