const dealService = require('../../services/dealService')
const followupService = require('../../services/followupService')
const clipboard = require('../../utils/clipboard')
const toast = require('../../utils/toast')
const format = require('../../utils/format')
const { getNextStages } = require('../../constants/dealStages')
const { addDays } = require('../../utils/time')

Page({
  data: {
    id: '',
    deal: null,
    customer: null,
    quotes: [],
    followups: [],
    logs: [],
    latestQuote: null,
    editMode: false,
    editForm: {}
  },

  onLoad(options) {
    this.setData({ id: options.id || '' })
  },

  onShow() {
    this.load()
  },

  load() {
    if (!this.data.id) return
    toast.loading('加载中')
    dealService.detail(this.data.id).then((res) => {
      if (!res) {
        toast.error('deal 不存在')
        return
      }
      const latestQuote = res.quotes && res.quotes[0] ? res.quotes[0] : null
      this.setData({
        deal: res.deal,
        customer: res.customer,
        quotes: res.quotes,
        followups: (res.followups || []).filter(function(item) { return item.taskStatus === 'pending' }),
        logs: res.logs,
        latestQuote,
        styleTagsText: (res.deal.styleTags || []).join('，') || '未填',
        materialTagsText: (res.deal.materialTags || []).join('，') || '未填',
        riskText: (res.deal.riskFlags || []).join('，'),
        editForm: Object.assign({}, res.deal)
      })
    }).catch((err) => {
      toast.error(err.message || '加载失败')
    }).finally(toast.hideLoading)
  },

  toggleEdit() {
    this.setData({ editMode: !this.data.editMode })
  },

  onInput(e) {
    const field = e.currentTarget.dataset.field
    this.setData({ ['editForm.' + field]: e.detail.value })
  },

  saveDemand() {
    dealService.upsert(this.data.editForm).then(() => {
      toast.success('已保存')
      this.setData({ editMode: false })
      this.load()
    }).catch((err) => toast.error(err.message || '保存失败'))
  },

  changeStage() {
    const deal = this.data.deal
    const next = getNextStages(deal.dealStage)
    if (!next.length) {
      toast.error('当前状态无直接下一步')
      return
    }
    wx.showActionSheet({
      itemList: next.map(function(item) { return item.label }),
      success: (res) => {
        const target = next[res.tapIndex]
        dealService.updateStage(deal._id, target.key, '详情页状态变更').then(() => {
          toast.success('状态已更新')
          this.load()
        }).catch((err) => toast.error(err.message || '状态更新失败'))
      }
    })
  },

  createQuote() {
    wx.navigateTo({ url: '/pages/quote-edit/quote-edit?dealId=' + this.data.deal._id })
  },

  openQuote(e) {
    const id = e.currentTarget.dataset.id
    wx.navigateTo({ url: '/pages/quote-detail/quote-detail?id=' + id })
  },

  createManualTask() {
    const deal = this.data.deal
    const customer = this.data.customer || {}
    followupService.createManual({
      dealId: deal._id,
      customerId: deal.customerId,
      customerName: customer.name,
      triggerType: 'manual',
      dueAt: addDays(new Date(), 1),
      suggestedText: '亲爱的' + (customer.name || '您') + '，我来跟进一下这次需求，有任何想调整的地方都可以直接告诉我。'
    }).then(() => {
      toast.success('已创建提醒')
      this.load()
    }).catch((err) => toast.error(err.message || '创建失败'))
  },

  copyTask(e) {
    clipboard.setClipboardText(e.detail.task.suggestedText || '').then(() => toast.success('已复制'))
  },

  completeTask(e) {
    followupService.updateTask(e.detail.task._id, 'complete').then(() => {
      toast.success('已完成')
      this.load()
    })
  },

  skipTask(e) {
    followupService.updateTask(e.detail.task._id, 'skip').then(() => {
      toast.success('已跳过')
      this.load()
    })
  },

  quickRepurchase() {
    const customer = this.data.customer
    const deal = this.data.deal
    dealService.upsert({
      customerId: customer._id,
      title: '复购 - ' + (deal.intentCategory || deal.title),
      sourceEntry: customer.platform,
      dealStage: 'new_inquiry',
      priorityLevel: 'medium',
      intentCategory: deal.intentCategory,
      demandSummary: '基于历史 deal 创建的复购机会',
      styleTags: deal.styleTags || [],
      materialTags: deal.materialTags || []
    }).then((res) => {
      wx.navigateTo({ url: '/pages/deal-detail/deal-detail?id=' + res.deal._id })
    })
  },

  money(value) {
    return format.currency(value)
  }
})
