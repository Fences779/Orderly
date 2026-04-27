const CATEGORY_WORDS = [
  { key: 'bracelet', label: '手串', words: ['手串', '串珠', '珠串'] },
  { key: 'necklace', label: '项链', words: ['项链', '吊坠', '锁骨链'] },
  { key: 'gift', label: '礼物', words: ['礼物', '送人', '生日', '纪念日'] },
  { key: 'accessory', label: '配件', words: ['配件', '挂件', '钥匙扣'] },
  { key: 'custom', label: '定制', words: ['定制', '订做', '按图', '改款'] }
]

const STYLE_WORDS = ['简约', '高级', '复古', '甜酷', '清冷', '温柔', '国风', '通勤', '可爱', '低调', '显白']
const MATERIAL_WORDS = ['珍珠', '水晶', '玛瑙', '银', '14k', '18k', '朱砂', '檀木', '贝母', '琉璃', '天然石']
const COLOR_WORDS = ['白色', '黑色', '金色', '银色', '蓝色', '绿色', '红色', '粉色', '紫色', '棕色']
const CUSTOM_WORDS = ['定制', '订做', '按图', '改尺寸', '换材质']
const REPURCHASE_WORDS = ['再来', '回购', '上次', '之前买过', '再买']

function collectWords(text, words) {
  const value = text || ''
  return words.filter(function(word) { return value.indexOf(word) >= 0 })
}

function detectCategory(text) {
  const value = text || ''
  const hit = CATEGORY_WORDS.find(function(group) {
    return group.words.some(function(word) { return value.indexOf(word) >= 0 })
  })
  return hit ? hit.label : ''
}

module.exports = {
  CATEGORY_WORDS,
  STYLE_WORDS,
  MATERIAL_WORDS,
  COLOR_WORDS,
  CUSTOM_WORDS,
  REPURCHASE_WORDS,
  collectWords,
  detectCategory
}
