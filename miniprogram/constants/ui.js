const URGENCY_OPTIONS = [
  { key: 'low', label: '普通' },
  { key: 'medium', label: '较急' },
  { key: 'high', label: '急单' }
]

const PRIORITY_OPTIONS = [
  { key: 'low', label: '低' },
  { key: 'medium', label: '中' },
  { key: 'high', label: '高' }
]

const DATE_FILTERS = [
  { key: 'today', label: '今日' },
  { key: '7d', label: '近7天' },
  { key: '30d', label: '近30天' }
]

function getOptionLabel(options, key) {
  const item = options.find(function(row) { return row.key === key })
  return item ? item.label : ''
}

module.exports = {
  URGENCY_OPTIONS,
  PRIORITY_OPTIONS,
  DATE_FILTERS,
  getOptionLabel
}
