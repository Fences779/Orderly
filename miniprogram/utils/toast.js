function success(title) {
  wx.showToast({ title: title || '已完成', icon: 'success' })
}

function error(title) {
  wx.showToast({ title: title || '操作失败', icon: 'none' })
}

function loading(title) {
  wx.showLoading({ title: title || '处理中' })
}

function hideLoading() {
  wx.hideLoading()
}

module.exports = {
  success,
  error,
  loading,
  hideLoading
}
