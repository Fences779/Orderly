const dealService = require('../../services/dealService')
const toast = require('../../utils/toast')
const { PLATFORMS } = require('../../constants/platforms')
const { getNextStages } = require('../../constants/dealStages')

Page({
  data: {
    platforms: [{ key: '', label: '全部平台' }].concat(PLATFORMS),
    filters: {
      platform: '',
      highPriority: false
    },
    columns: [],
    loading: false
  },

  onShow() {
    this.load()
  },

  load() {
    this.setData({ loading: true })
    dealService.board(this.data.filters).then((columns) => {
      this.setData({ columns })
    }).catch((err) => {
      toast.error(err.message || '看板加载失败')
    }).finally(() => {
      this.setData({ loading: false })
    })
  },

  pickPlatform(e) {
    const item = this.data.platforms[Number(e.detail.value)]
    this.setData({ 'filters.platform': item.key })
    this.load()
  },

  toggleHighPriority() {
    this.setData({ 'filters.highPriority': !this.data.filters.highPriority })
    this.load()
  },

  openDeal(e) {
    if (e.detail.id) wx.navigateTo({ url: '/pages/deal-detail/deal-detail?id=' + e.detail.id })
  },

  advanceDeal(e) {
    const deal = e.detail.deal
    const next = getNextStages(deal.dealStage)
    if (!next.length) {
      toast.error('当前状态不可直接推进')
      return
    }
    wx.showActionSheet({
      itemList: next.map(function(item) { return item.label }),
      success: (res) => {
        const target = next[res.tapIndex]
        dealService.updateStage(deal._id, target.key, '看板快捷推进').then(() => {
          toast.success('状态已更新')
          this.load()
        }).catch((err) => toast.error(err.message || '推进失败'))
      }
    })
  }
})
