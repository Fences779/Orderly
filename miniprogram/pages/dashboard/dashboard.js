const statsService = require('../../services/statsService')
const dealService = require('../../services/dealService')
const followupService = require('../../services/followupService')
const clipboard = require('../../utils/clipboard')
const toast = require('../../utils/toast')

Page({
  data: {
    metrics: {
      newDeals: 0,
      pendingFollowups: 0,
      quotedDeals: 0,
      wonDeals: 0,
      lostDeals: 0,
      repurchaseDue: 0
    },
    actions: [
      { title: '极速新建', url: '/pages/quick-create/quick-create' },
      { title: '粘贴板录入', url: '/pages/capture-clipboard/capture-clipboard' },
      { title: 'OCR 录入', url: '/pages/capture-ocr/capture-ocr' },
      { title: '成交看板', url: '/pages/deals-board/deals-board', tab: true },
      { title: '跟进任务', url: '/pages/followups/followups', tab: true },
      { title: '客户列表', url: '/pages/customers/customers', tab: true },
      { title: 'SKU 管理', url: '/pages/sku/sku' },
      { title: '模板管理', url: '/pages/templates/templates' },
      { title: '统计', url: '/pages/stats/stats', tab: true },
      { title: '设置', url: '/pages/settings/settings' }
    ],
    recentDeals: [],
    priorityTasks: [],
    loading: false
  },

  onShow() {
    this.load()
  },

  load() {
    this.setData({ loading: true })
    Promise.all([
      statsService.summary('today'),
      dealService.list({}),
      followupService.list({})
    ]).then((res) => {
      const stats = res[0].summary || {}
      this.setData({
        metrics: Object.assign(this.data.metrics, stats),
        recentDeals: (res[1] || []).slice(0, 5),
        priorityTasks: (res[2] || []).slice(0, 3)
      })
    }).catch((err) => {
      toast.error(err.message || '加载失败')
    }).finally(() => {
      this.setData({ loading: false })
    })
  },

  nav(e) {
    const index = Number(e.currentTarget.dataset.index)
    const item = this.data.actions[index]
    if (!item) return
    if (item.tab) wx.switchTab({ url: item.url })
    else wx.navigateTo({ url: item.url })
  },

  openDeal(e) {
    const id = e.detail.id
    if (id) wx.navigateTo({ url: '/pages/deal-detail/deal-detail?id=' + id })
  },

  openTask(e) {
    const task = e.detail.task
    if (task && task.dealId) wx.navigateTo({ url: '/pages/deal-detail/deal-detail?id=' + task.dealId })
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

  scanFollowups() {
    toast.loading('扫描中')
    followupService.scan().then((res) => {
      toast.success('新增 ' + (res.created || 0) + ' 条')
      this.load()
    }).catch((err) => {
      toast.error(err.message || '扫描失败')
    }).finally(toast.hideLoading)
  },

  navToFollowups() {
    wx.switchTab({ url: '/pages/followups/followups' })
  },

  goBoard() {
    wx.switchTab({ url: '/pages/deals-board/deals-board' })
  }
})
