function getClipboardText() {
  return new Promise(function(resolve) {
    wx.getClipboardData({
      success(res) {
        resolve(res.data || '')
      },
      fail() {
        resolve('')
      }
    })
  })
}

function setClipboardText(text) {
  return new Promise(function(resolve, reject) {
    wx.setClipboardData({
      data: text || '',
      success: resolve,
      fail: reject
    })
  })
}

module.exports = {
  getClipboardText,
  setClipboardText
}
