const RISK_WORDS = [
  { key: 'think_again', label: '我再想想', words: ['我再想想', '再考虑', '考虑一下'] },
  { key: 'just_looking', label: '先看看', words: ['先看看', '看一下', '了解一下'] },
  { key: 'price_sensitive', label: '价格敏感', words: ['有点贵', '太贵', '能便宜吗', '优惠点', '便宜点'] },
  { key: 'compare_others', label: '比价中', words: ['问问别人', '对比一下', '别家'] },
  { key: 'not_urgent', label: '不着急', words: ['暂时不着急', '不急', '以后再说'] }
]

function detectRiskFlags(text) {
  const value = text || ''
  const flags = []
  RISK_WORDS.forEach(function(group) {
    const hit = group.words.some(function(word) { return value.indexOf(word) >= 0 })
    if (hit) flags.push({ key: group.key, label: group.label })
  })
  return flags
}

module.exports = {
  RISK_WORDS,
  detectRiskFlags
}
