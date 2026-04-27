const quoteService = require('../../services/quoteService')
const dealService = require('../../services/dealService')
const clipboard = require('../../utils/clipboard')
const toast = require('../../utils/toast')

Page({
  data: {
    id: '',
    quote: null,
    deal: null,
    customer: null
  },

  onLoad(options) {
    this.setData({ id: options.id || '' })
    this.load()
  },

  load() {
    quoteService.get(this.data.id).then((quote) => {
      this.setData({ quote })
      return dealService.detail(quote.dealId)
    }).then((detail) => {
      this.setData({ deal: detail.deal, customer: detail.customer })
    }).catch((err) => toast.error(err.message || '加载失败'))
  },

  copyText() {
    const text = quoteService.copyText(this.data.quote, this.data.deal, this.data.customer)
    clipboard.setClipboardText(text).then(() => toast.success('已复制报价'))
  },

  edit() {
    wx.navigateTo({ url: '/pages/quote-edit/quote-edit?id=' + this.data.quote._id + '&dealId=' + this.data.quote.dealId })
  },

  mark(e) {
    const status = e.currentTarget.dataset.status
    quoteService.save(Object.assign({}, this.data.quote, { quoteStatus: status }), 'status').then(() => {
      toast.success('已更新')
      this.load()
    })
  }
})
