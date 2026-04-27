function currency(value) {
  const num = Number(value || 0)
  return '¥' + num.toFixed(2).replace(/\.00$/, '')
}

function percent(value) {
  const num = Number(value || 0)
  return Math.round(num * 100) + '%'
}

function compactText(value, length) {
  const text = value || ''
  const max = length || 32
  return text.length > max ? text.slice(0, max) + '...' : text
}

function joinTags(tags) {
  return Array.isArray(tags) && tags.length ? tags.join(' / ') : '未填写'
}

module.exports = {
  currency,
  percent,
  compactText,
  joinTags
}
