const { detectRiskFlags } = require('../constants/riskDictionary')
const parseDictionary = require('../constants/parseDictionary')
const { parseRelativeDeadline } = require('../utils/time')

function unique(list) {
  const map = {}
  return (list || []).filter(function(item) {
    if (!item || map[item]) return false
    map[item] = true
    return true
  })
}

function detectName(text) {
  const value = text || ''
  const named = value.match(/(?:我是|我叫|昵称|客户|买家)[:：\s]*([\u4e00-\u9fa5A-Za-z0-9_-]{2,12})/)
  if (named) return named[1]
  const speaker = value.match(/([\u4e00-\u9fa5A-Za-z0-9_-]{2,12})[:：]\s*/)
  return speaker ? speaker[1] : ''
}

function detectContact(text) {
  const value = text || ''
  const wxHandle = value.match(/(?:微信|vx|VX|v)[:：\s]*([A-Za-z0-9_-]{5,32})/)
  if (wxHandle) return wxHandle[1]
  const phone = value.match(/1[3-9]\d{9}/)
  return phone ? phone[0] : ''
}

function detectBudget(text) {
  const value = text || ''
  let min = 0
  let max = 0
  const around = value.match(/(\d{2,6})\s*(?:左右|上下|附近)/)
  const under = value.match(/(\d{2,6})\s*(?:以内|以下|内)/)
  const range = value.match(/(\d{2,6})\s*[-到至]\s*(\d{2,6})/)
  if (range) {
    min = Number(range[1])
    max = Number(range[2])
  } else if (around) {
    const base = Number(around[1])
    min = Math.max(0, Math.round(base * 0.85))
    max = Math.round(base * 1.15)
  } else if (under) {
    max = Number(under[1])
  } else if (value.indexOf('预算不高') >= 0 || value.indexOf('别太贵') >= 0 || value.indexOf('便宜点') >= 0) {
    max = 200
  }
  return { budgetMin: min, budgetMax: max }
}

function detectSize(text) {
  const value = text || ''
  const hits = []
  const patterns = [
    /\d+(?:\.\d+)?\s*(?:cm|厘米|mm|毫米)/gi,
    /\d+\s*(?:颗|粒|件|个)/g,
    /手围\s*\d+(?:\.\d+)?/g
  ]
  patterns.forEach(function(pattern) {
    const match = value.match(pattern)
    if (match) hits.push.apply(hits, match)
  })
  return unique(hits).join('，')
}

function detectUrgency(text) {
  const value = text || ''
  if (value.indexOf('急') >= 0 || value.indexOf('后天') >= 0 || value.indexOf('明天') >= 0 || value.indexOf('尽快') >= 0) return 'high'
  if (value.indexOf('这周') >= 0 || value.indexOf('月底') >= 0 || value.indexOf('生日') >= 0) return 'medium'
  return 'low'
}

function confidenceScore(result, rawText) {
  let score = 28
  if (result.customerHints.name) score += 12
  if (result.intentCategory) score += 12
  if (result.demandSummary) score += 10
  if (result.styleTags.length) score += 8
  if (result.materialTags.length) score += 8
  if (result.budgetMax) score += 8
  if (result.deadlineAt) score += 8
  if ((rawText || '').length > 18) score += 6
  return Math.min(96, score)
}

function parseText(rawText) {
  const text = (rawText || '').trim()
  const intentCategory = parseDictionary.detectCategory(text)
  const styleTags = unique(parseDictionary.collectWords(text, parseDictionary.STYLE_WORDS))
  const materialTags = unique(parseDictionary.collectWords(text, parseDictionary.MATERIAL_WORDS))
  const colorHits = unique(parseDictionary.collectWords(text, parseDictionary.COLOR_WORDS))
  const budget = detectBudget(text)
  const riskFlags = detectRiskFlags(text)
  const deadlineAt = parseRelativeDeadline(text)
  const urgencyLevel = detectUrgency(text)
  const isCustom = parseDictionary.collectWords(text, parseDictionary.CUSTOM_WORDS).length > 0
  const isRepurchase = parseDictionary.collectWords(text, parseDictionary.REPURCHASE_WORDS).length > 0
  const suggestedStage = riskFlags.length ? 'needs_clarification' : (intentCategory || isCustom ? 'quote_preparing' : 'new_inquiry')
  const summary = text.split(/\n/).map(function(line) { return line.trim() }).filter(Boolean)[0] || text.slice(0, 48)
  const result = {
    customerHints: {
      name: detectName(text),
      externalUid: '',
      contactHandle: detectContact(text),
      platformHint: text.indexOf('闲鱼') >= 0 ? 'xianyu' : ''
    },
    intentCategory,
    demandSummary: summary,
    styleTags,
    materialTags,
    sizeSpec: detectSize(text),
    colorPref: colorHits.join('，'),
    budgetMin: budget.budgetMin,
    budgetMax: budget.budgetMax,
    deadlineAt,
    urgencyLevel,
    riskFlags,
    isCustom,
    isRepurchase,
    suggestedStage
  }
  result.confidenceScore = confidenceScore(result, text)
  return result
}

module.exports = {
  parseText
}
