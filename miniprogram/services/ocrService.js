const cloud = require('./cloud')

function chooseImage() {
  return new Promise(function(resolve, reject) {
    wx.chooseMedia({
      count: 1,
      mediaType: ['image'],
      sourceType: ['album', 'camera'],
      success(res) {
        const file = res.tempFiles && res.tempFiles[0]
        if (!file) {
          reject(new Error('未选择图片'))
          return
        }
        resolve(file.tempFilePath)
      },
      fail: reject
    })
  })
}

function uploadImage(path) {
  const suffix = normalizeImageSuffix(path)
  return wx.cloud.uploadFile({
    cloudPath: 'captures/ocr_' + Date.now() + '_' + Math.random().toString(36).slice(2, 6) + '.' + suffix,
    filePath: path
  }).then(function(res) {
    return res.fileID
  })
}

function normalizeImageSuffix(path) {
  const raw = String(path || '').split('.').pop() || 'jpg'
  const suffix = raw.trim().toLowerCase()
  return /^(png|jpg|jpeg|bmp|gif|webp)$/.test(suffix) ? suffix : 'jpg'
}

function recognize(fileID) {
  return cloud.call('ocrAdapter', { fileID })
}

module.exports = {
  chooseImage,
  uploadImage,
  recognize
}
