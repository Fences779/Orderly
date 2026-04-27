const customerService = require('../../services/customerService')
const dealService = require('../../services/dealService')
const { PLATFORMS } = require('../../constants/platforms')
const { DEAL_STAGES, DEAL_STAGE_LABELS } = require('../../constants/dealStages')
const { URGENCY_OPTIONS, PRIORITY_OPTIONS } = require('../../constants/ui')
const toast = require('../../utils/toast')
const validators = require('../../utils/validators')

Page({
  data: {
    platforms: PLATFORMS,
    stages: DEAL_STAGES.map(function(key) { return { key, label: DEAL_STAGE_LABELS[key] } }),
    urgencyOptions: URGENCY_OPTIONS,
    priorityOptions: PRIORITY_OPTIONS,
    form: {
      customerName: '',
      platform: 'wechat',
      demandSummary: '',
      dealStage: 'new_inquiry',
      budgetMax: '',
      urgencyLevel: 'low',
      priorityLevel: 'medium'
    }
  },

  onInput(e) {
    const field = e.currentTarget.dataset.field
    this.setData({ ['form.' + field]: e.detail.value })
  },

  onPick(e) {
    const field = e.currentTarget.dataset.field
    const list = e.currentTarget.dataset.list
    const index = Number(e.detail.value)
    const source = this.data[list] || []
    if (source[index]) this.setData({ ['form.' + field]: source[index].key })
  },

  save() {
    const form = this.data.form
    if (!validators.required(form.customerName)) {
      toast.error('请填写客户名')
      return
    }
    if (!validators.required(form.demandSummary)) {
      toast.error('请填写一句话需求')
      return
    }
    toast.loading('保存中')
    customerService.upsert({
      name: form.customerName,
      platform: form.platform,
      sourceChannel: 'quick_create',
      lastContactAt: new Date().toISOString()
    }).then((res) => {
      const customer = res.customer
      return dealService.upsert({
        customerId: customer._id,
        title: form.demandSummary.slice(0, 24),
        sourceEntry: form.platform,
        dealStage: form.dealStage,
        priorityLevel: form.priorityLevel,
        demandSummary: form.demandSummary,
        budgetMax: Number(form.budgetMax || 0),
        urgencyLevel: form.urgencyLevel,
        lastInteractionAt: new Date().toISOString()
      })
    }).then((res) => {
      toast.success('已建档')
      wx.redirectTo({ url: '/pages/deal-detail/deal-detail?id=' + res.deal._id })
    }).catch((err) => {
      toast.error(err.message || '保存失败')
    }).finally(toast.hideLoading)
  }
})
