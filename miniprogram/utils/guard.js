function navigate(url) {
  if (!url) return
  wx.navigateTo({ url })
}

function switchTab(url) {
  wx.switchTab({ url })
}

function getAppWorkspaceId() {
  const app = getApp()
  return app && app.globalData ? app.globalData.workspaceId : 'default'
}

module.exports = {
  navigate,
  switchTab,
  getAppWorkspaceId
}
