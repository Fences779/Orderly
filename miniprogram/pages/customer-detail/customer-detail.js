const customerService = require('../../services/customerService')
const dealService = require('../../services/dealService')
const toast = require('../../utils/toast')

Page({
  data: {
    id: '',
    customer: null,
    deals: [],
    form: {}
  },

  onLoad(options) {
    this.setData({ id: options.id || '' })
  },

  onShow() {
    this.load()
  },

  load() {
    Promise.all([
      customerService.get(this.data.id),
      dealService.list({})
    ]).then((res) => {
      const customer = res[0]
      this.setData({
        customer,
        form: Object.assign({}, customer),
        deals: (res[1] || []).filter((deal) => deal.customerId === this.data.id)
      })
    }).catch((err) => toast.error(err.message || '加载失败'))
  },

  onInput(e) {
    const field = e.currentTarget.dataset.field
    this.setData({ ['form.' + field]: e.detail.value })
  },

  save() {
    customerService.updateProfile(this.data.id, this.data.form).then(() => {
      toast.success('已保存')
      this.load()
    }).catch((err) => toast.error(err.message || '保存失败'))
  },

  openDeal(e) {
    wx.navigateTo({ url: '/pages/deal-detail/deal-detail?id=' + e.detail.id })
  },

  newDeal() {
    const customer = this.data.customer
    dealService.upsert({
      customerId: customer._id,
      title: '复购咨询',
      sourceEntry: customer.platform,
      dealStage: 'new_inquiry',
      priorityLevel: 'medium',
      demandSummary: '客户历史档案快速创建的复购需求'
    }).then((res) => {
      wx.navigateTo({ url: '/pages/deal-detail/deal-detail?id=' + res.deal._id })
    })
  }
})
