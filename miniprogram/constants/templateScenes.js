const TEMPLATE_SCENES = [
  { key: 'clarify', label: '补充信息追问' },
  { key: 'quote_followup', label: '报价后跟进' },
  { key: 'shipping', label: '发货通知' },
  { key: 'aftersales', label: '收货关怀' },
  { key: 'repurchase', label: '复购触达' }
]

function getSceneLabel(key) {
  const item = TEMPLATE_SCENES.find(function(row) { return row.key === key })
  return item ? item.label : '其他'
}

module.exports = {
  TEMPLATE_SCENES,
  getSceneLabel
}
