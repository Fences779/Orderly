function createId(prefix) {
  const random = Math.random().toString(36).slice(2, 8)
  return (prefix || 'id') + '_' + Date.now().toString(36) + '_' + random
}

function quoteNo() {
  const date = new Date()
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return 'Q' + y + m + d + '-' + Math.random().toString(36).slice(2, 6).toUpperCase()
}

module.exports = {
  createId,
  quoteNo
}
