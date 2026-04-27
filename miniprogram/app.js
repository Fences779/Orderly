const appConfig = require('./config/appConfig')

App({
  globalData: {
    workspaceId: appConfig.workspaceId,
    cloudReady: false,
    user: null
  },

  onLaunch() {
    if (wx.cloud) {
      wx.cloud.init({
        env: appConfig.cloudEnv || undefined,
        traceUser: true
      })
      this.globalData.cloudReady = true
    }
  }
})
