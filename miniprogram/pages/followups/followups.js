const followupService = require('../../services/followupService')
const clipboard = require('../../utils/clipboard')
const toast = require('../../utils/toast')
const { FOLLOWUP_TYPES } = require('../../constants/followupTypes')
const { addDays } = require('../../utils/time')

Page({
  data: {
    filters: [{ key: '', label: '全部' }].concat(FOLLOWUP_TYPES),
    triggerType: '',
    tasks: []
  },

  onShow() {
    this.load()
  },

  load() {
    followupService.list({ triggerType: this.data.triggerType }).then((tasks) => {
      this.setData({ tasks })
    }).catch((err) => toast.error(err.message || '任务加载失败'))
  },

  pickType(e) {
    const item = this.data.filters[Number(e.detail.value)]
    this.setData({ triggerType: item.key })
    this.load()
  },

  scan() {
    toast.loading('扫描中')
    followupService.scan().then((res) => {
      toast.success('新增 ' + (res.created || 0) + ' 条')
      this.load()
    }).catch((err) => toast.error(err.message || '扫描失败')).finally(toast.hideLoading)
  },

  copy(e) {
    clipboard.setClipboardText(e.detail.task.suggestedText || '').then(() => toast.success('已复制'))
  },

  done(e) {
    followupService.updateTask(e.detail.task._id, 'complete').then(() => {
      toast.success('已完成')
      this.load()
    })
  },

  skip(e) {
    followupService.updateTask(e.detail.task._id, 'skip').then(() => {
      toast.success('已跳过')
      this.load()
    })
  },

  delay(e) {
    followupService.updateTask(e.detail.task._id, 'delay', { dueAt: addDays(new Date(), 1) }).then(() => {
      toast.success('已延期')
      this.load()
    })
  },

  open(e) {
    const task = e.detail.task
    if (task.dealId) wx.navigateTo({ url: '/pages/deal-detail/deal-detail?id=' + task.dealId })
  }
})
