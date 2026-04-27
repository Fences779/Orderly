function pad(value) {
  return value < 10 ? '0' + value : '' + value
}

function toDate(value) {
  if (!value) return null
  if (value instanceof Date) return value
  return new Date(value)
}

function now() {
  return new Date().toISOString()
}

function addDays(value, days) {
  const base = value ? new Date(value) : new Date()
  base.setDate(base.getDate() + Number(days || 0))
  return base.toISOString()
}

function addHours(value, hours) {
  const base = value ? new Date(value) : new Date()
  base.setHours(base.getHours() + Number(hours || 0))
  return base.toISOString()
}

function startOfPeriod(period) {
  const date = new Date()
  if (period === 'today') {
    date.setHours(0, 0, 0, 0)
  } else if (period === '7d') {
    date.setDate(date.getDate() - 6)
    date.setHours(0, 0, 0, 0)
  } else {
    date.setDate(date.getDate() - 29)
    date.setHours(0, 0, 0, 0)
  }
  return date.toISOString()
}

function formatDate(value) {
  const date = toDate(value)
  if (!date || Number.isNaN(date.getTime())) return ''
  return date.getFullYear() + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate())
}

function formatDateTime(value) {
  const date = toDate(value)
  if (!date || Number.isNaN(date.getTime())) return ''
  return formatDate(date) + ' ' + pad(date.getHours()) + ':' + pad(date.getMinutes())
}

function parseRelativeDeadline(text) {
  const value = text || ''
  const current = new Date()
  if (value.indexOf('后天') >= 0) return addDays(current, 2)
  if (value.indexOf('明天') >= 0) return addDays(current, 1)
  if (value.indexOf('这周') >= 0 || value.indexOf('周末') >= 0) return addDays(current, 6 - current.getDay())
  if (value.indexOf('月底') >= 0) {
    return new Date(current.getFullYear(), current.getMonth() + 1, 0, 18, 0, 0).toISOString()
  }
  if (value.indexOf('尽快') >= 0 || value.indexOf('急') >= 0) return addDays(current, 3)
  return ''
}

module.exports = {
  now,
  addDays,
  addHours,
  startOfPeriod,
  formatDate,
  formatDateTime,
  parseRelativeDeadline
}
