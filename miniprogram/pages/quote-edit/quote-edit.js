const quoteService = require('../../services/quoteService')
const skuService = require('../../services/skuService')
const dealService = require('../../services/dealService')
const toast = require('../../utils/toast')
const { addDays, formatDate } = require('../../utils/time')

Page({
  data: {
    dealId: '',
    quoteId: '',
    deal: null,
    skus: [],
    form: {
      items: [],
      baseAmount: 0,
      customFee: 0,
      laborFee: 0,
      shippingFee: 0,
      discountAmount: 0,
      depositRequired: 0,
      totalAmount: 0,
      validUntil: '',
      quoteNote: ''
    }
  },

  onLoad(options) {
    this.setData({
      dealId: options.dealId || '',
      quoteId: options.id || '',
      'form.validUntil': formatDate(addDays(new Date(), 7))
    })
    this.load()
  },

  load() {
    Promise.all([
      this.data.dealId ? dealService.detail(this.data.dealId) : Promise.resolve(null),
      skuService.list(true),
      this.data.quoteId ? quoteService.get(this.data.quoteId) : Promise.resolve(null)
    ]).then((res) => {
      const quote = res[2]
      this.setData({
        deal: res[0] && res[0].deal,
        skus: res[1] || []
      })
      if (quote) {
        this.setData({ form: quote, dealId: quote.dealId })
      }
      this.recalculate()
    }).catch((err) => toast.error(err.message || '加载失败'))
  },

  addSku(e) {
    const sku = this.data.skus[Number(e.detail.value)]
    if (!sku) return
    const items = this.data.form.items.concat([{
      skuId: sku._id,
      name: sku.name,
      qty: 1,
      price: Number(sku.basePrice || 0),
      note: ''
    }])
    this.setData({ 'form.items': items })
    this.recalculate()
  },

  onMoneyInput(e) {
    const field = e.currentTarget.dataset.field
    this.setData({ ['form.' + field]: Number(e.detail.value || 0) })
    this.recalculate()
  },

  onTextInput(e) {
    const field = e.currentTarget.dataset.field
    this.setData({ ['form.' + field]: e.detail.value })
  },

  updateItem(e) {
    const index = Number(e.currentTarget.dataset.index)
    const field = e.currentTarget.dataset.field
    const items = this.data.form.items.slice()
    items[index][field] = field === 'name' || field === 'note' ? e.detail.value : Number(e.detail.value || 0)
    this.setData({ 'form.items': items })
    this.recalculate()
  },

  removeItem(e) {
    const index = Number(e.currentTarget.dataset.index)
    const items = this.data.form.items.slice()
    items.splice(index, 1)
    this.setData({ 'form.items': items })
    this.recalculate()
  },

  recalculate() {
    const calculated = quoteService.calculate(this.data.form)
    this.setData({ form: calculated })
  },

  saveDraft() {
    this.submit('draft')
  },

  sendQuote() {
    this.submit('send')
  },

  submit(action) {
    if (!this.data.dealId) {
      toast.error('缺少 dealId')
      return
    }
    const payload = Object.assign({}, this.data.form, {
      _id: this.data.quoteId || this.data.form._id,
      dealId: this.data.dealId
    })
    toast.loading(action === 'send' ? '发送中' : '保存中')
    quoteService.save(payload, action).then((res) => {
      toast.success(action === 'send' ? '已发送报价' : '已保存草稿')
      wx.redirectTo({ url: '/pages/quote-detail/quote-detail?id=' + res.quote._id })
    }).catch((err) => toast.error(err.message || '保存失败')).finally(toast.hideLoading)
  }
})
