const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const MAX_EVENT_BYTES = 65536

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
  return { ok: true, operatorId }
}

function rejectOversizedEvent(event) {
  const bytes = Buffer.byteLength(JSON.stringify(event || {}), 'utf8')
  return bytes > MAX_EVENT_BYTES ? { ok: false, code: 'payload_too_large', message: '请求体过大。' } : null
}

function normalizeList(value) {
  return String(value || '')
    .split(/[,\s，、;；]+/)
    .map((item) => item.trim())
    .filter(Boolean)
}

function resolveWorkspaceId(event) {
  const workspaceId = String((event && event.workspaceId) || '').trim() || DEFAULT_WORKSPACE_ID
  const configured = normalizeList(process.env[ALLOWED_WORKSPACE_IDS_ENV_NAME])
  const allowed = configured.length ? configured : [DEFAULT_WORKSPACE_ID]
  if (allowed.indexOf(workspaceId) < 0) return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  return { ok: true, workspaceId }
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
  }).slice(0, 5)
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
  const parserResult = event.parserResult || parseText(event.rawText || event.ocrText || '')
  const customerMatches = await matchCustomers(workspaceId, parserResult)
  if (event.mode === 'matchOnly') {
    return { ok: true, parserResult, customerMatches }
  }
  if (!event.rawText && !event.ocrText) return { ok: false, message: '缺少可解析文本' }
  const capture = {
    workspaceId,
    sourceType: event.sourceType || 'clipboard',
    rawText: event.rawText || '',
    rawImageUrl: event.rawImageUrl || '',
    ocrText: event.ocrText || '',
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
