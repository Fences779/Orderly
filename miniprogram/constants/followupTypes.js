const FOLLOWUP_TYPES = [
  { key: 'quote_no_reply', label: '报价未回复', baseScore: 40 },
  { key: 'post_delivery', label: '收货后关怀', baseScore: 30 },
  { key: 'repurchase', label: '复购提醒', baseScore: 35 },
  { key: 'manual', label: '手动提醒', baseScore: 28 },
  { key: 'custom', label: '自定义', baseScore: 25 }
]

const FOLLOWUP_STATUS = {
  pending: '待处理',
  completed: '已完成',
  skipped: '已跳过'
}

function getFollowupType(key) {
  return FOLLOWUP_TYPES.find(function(item) { return item.key === key }) || FOLLOWUP_TYPES[4]
}

module.exports = {
  FOLLOWUP_TYPES,
  FOLLOWUP_STATUS,
  getFollowupType
}
