const cloud = require('../../services/cloud')
const followupService = require('../../services/followupService')
const toast = require('../../utils/toast')
const appConfig = require('../../config/appConfig')

Page({
  data: {
    appConfig,
    initResult: null
  },

  initSeed() {
    toast.loading('初始化中')
    cloud.call('dealInitSeed', { force: false }).then((res) => {
      this.setData({ initResult: res })
      toast.success('初始化完成')
    }).catch((err) => toast.error(err.message || '初始化失败')).finally(toast.hideLoading)
  },

  scanFollowups() {
    followupService.scan().then((res) => {
      toast.success('新增 ' + (res.created || 0) + ' 条')
    }).catch((err) => toast.error(err.message || '扫描失败'))
  }
})
