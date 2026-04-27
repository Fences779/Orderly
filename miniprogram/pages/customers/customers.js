const customerService = require('../../services/customerService')
const { PLATFORMS } = require('../../constants/platforms')
const toast = require('../../utils/toast')

Page({
  data: {
    platforms: [{ key: '', label: '全部平台' }].concat(PLATFORMS),
    keyword: '',
    platform: '',
    customers: []
  },

  onShow() {
    this.load()
  },

  load() {
    customerService.list({ keyword: this.data.keyword, platform: this.data.platform }).then((customers) => {
      this.setData({ customers })
    }).catch((err) => toast.error(err.message || '客户加载失败'))
  },

  onSearch(e) {
    this.setData({ keyword: e.detail.value })
    this.load()
  },

  pickPlatform(e) {
    const item = this.data.platforms[Number(e.detail.value)]
    this.setData({ platform: item.key })
    this.load()
  },

  open(e) {
    const customer = e.detail.customer
    wx.navigateTo({ url: '/pages/customer-detail/customer-detail?id=' + customer._id })
  }
})
