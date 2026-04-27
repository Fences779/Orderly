const captureService = require('../../services/captureService')
const toast = require('../../utils/toast')
const validators = require('../../utils/validators')

Page({
  data: {
    captureId: '',
    capture: null,
    matches: [],
    selectedCustomerId: '',
    customerMode: 'new',
    form: {
      customerName: '',
      contactHandle: '',
      platform: 'wechat',
      title: '',
      demandSummary: '',
      intentCategory: '',
      styleTags: '',
      materialTags: '',
      sizeSpec: '',
      colorPref: '',
      budgetMin: '',
      budgetMax: '',
      deadlineAt: '',
      urgencyLevel: 'low',
      dealStage: 'new_inquiry',
      riskFlags: ''
    }
  },

  onLoad(options) {
    this.setData({ captureId: options.id || '' })
    this.load()
  },

  load() {
    if (!this.data.captureId) return
    toast.loading('加载中')
    captureService.getCapture(this.data.captureId).then((capture) => {
      const result = capture.parserResult || {}
      const hints = result.customerHints || {}
      const form = {
        customerName: hints.name || '未命名客户',
        contactHandle: hints.contactHandle || '',
        platform: hints.platformHint || 'wechat',
        title: result.demandSummary ? result.demandSummary.slice(0, 24) : '新需求',
        demandSummary: result.demandSummary || '',
        intentCategory: result.intentCategory || '',
        styleTags: (result.styleTags || []).join('，'),
        materialTags: (result.materialTags || []).join('，'),
        sizeSpec: result.sizeSpec || '',
        colorPref: result.colorPref || '',
        budgetMin: result.budgetMin || '',
        budgetMax: result.budgetMax || '',
        deadlineAt: result.deadlineAt || '',
        urgencyLevel: result.urgencyLevel || 'low',
        dealStage: result.suggestedStage || 'new_inquiry',
        riskFlags: (result.riskFlags || []).map(function(item) { return item.label }).join('，')
      }
      this.setData({
        capture,
        matches: capture.customerMatches || [],
        form
      })
    }).catch((err) => {
      toast.error(err.message || '加载失败')
    }).finally(toast.hideLoading)
  },

  onInput(e) {
    const field = e.currentTarget.dataset.field
    this.setData({ ['form.' + field]: e.detail.value })
  },

  selectCustomer(e) {
    const customer = e.detail.customer
    this.setData({
      selectedCustomerId: customer._id,
      customerMode: 'existing',
      'form.customerName': customer.name,
      'form.contactHandle': customer.contactHandle || '',
      'form.platform': customer.platform || 'wechat'
    })
  },

  useNewCustomer() {
    this.setData({ selectedCustomerId: '', customerMode: 'new' })
  },

  confirm() {
    const form = this.data.form
    if (!validators.required(form.customerName) || !validators.required(form.demandSummary)) {
      toast.error('客户名和需求摘要必填')
      return
    }
    toast.loading('入库中')
    captureService.confirm({
      captureId: this.data.captureId,
      customerMode: this.data.customerMode,
      selectedCustomerId: this.data.selectedCustomerId,
      form
    }).then((res) => {
      toast.success('已入库')
      wx.redirectTo({ url: '/pages/deal-detail/deal-detail?id=' + res.dealId })
    }).catch((err) => {
      toast.error(err.message || '入库失败')
    }).finally(toast.hideLoading)
  }
})
