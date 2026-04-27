const ocrService = require('../../services/ocrService')
const captureService = require('../../services/captureService')
const toast = require('../../utils/toast')

Page({
  data: {
    imagePath: '',
    fileID: '',
    ocrText: '',
    parserResult: null,
    captureId: ''
  },

  chooseImage() {
    ocrService.chooseImage().then((path) => {
      this.setData({ imagePath: path })
      toast.loading('上传中')
      return ocrService.uploadImage(path)
    }).then((fileID) => {
      this.setData({ fileID })
      toast.loading('识别中')
      return ocrService.recognize(fileID)
    }).then((res) => {
      this.setData({ ocrText: res.ocrText || '' })
      toast.success('OCR 已返回')
    }).catch((err) => {
      this.setData({ ocrText: this.data.ocrText || '' })
      toast.error(err.message || 'OCR 可手动粘贴')
    }).finally(toast.hideLoading)
  },

  onInput(e) {
    this.setData({ ocrText: e.detail.value })
  },

  parse() {
    const text = (this.data.ocrText || '').trim()
    if (!text) {
      toast.error('请先识别或粘贴文本')
      return
    }
    toast.loading('解析中')
    captureService.parseCapture({
      sourceType: 'ocr',
      rawText: text,
      ocrText: text,
      rawImageUrl: this.data.fileID
    }).then((res) => {
      this.setData({
        parserResult: res.parserResult,
        captureId: res.captureId
      })
      toast.success('已生成草稿')
    }).catch((err) => {
      toast.error(err.message || '解析失败')
    }).finally(toast.hideLoading)
  },

  goConfirm() {
    if (!this.data.captureId) {
      toast.error('请先解析')
      return
    }
    wx.navigateTo({ url: '/pages/capture-confirm/capture-confirm?id=' + this.data.captureId })
  }
})
