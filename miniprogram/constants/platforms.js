const PLATFORMS = [
  { key: 'wechat', label: '微信' },
  { key: 'xianyu', label: '闲鱼' },
  { key: 'xiaohongshu', label: '小红书' },
  { key: 'douyin', label: '抖音' },
  { key: 'offline', label: '线下' },
  { key: 'other', label: '其他' }
]

function getPlatformLabel(key) {
  const item = PLATFORMS.find(function(row) { return row.key === key })
  return item ? item.label : '其他'
}

module.exports = {
  PLATFORMS,
  getPlatformLabel
}
