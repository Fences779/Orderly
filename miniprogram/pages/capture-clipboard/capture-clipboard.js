const clipboard = require('../../utils/clipboard')
const toast = require('../../utils/toast')
const captureService = require('../../services/captureService')

Page({
  data: {
    rawText: '',
    parserResult: null,
    confidenceScore: 0,
    captureId: ''
  },

  onLoad() {
    this.readClipboard()
  },

  readClipboard() {
    clipboard.getClipboardText().then((text) => {
      this.setData({ rawText: text || '' })
      if (!text) toast.error('剪贴板为空，可手动粘贴')
    })
  },

  onInput(e) {
    this.setData({ rawText: e.detail.value })
  },

  parse() {
    const rawText = (this.data.rawText || '').trim()
    if (!rawText) {
      toast.error('请先粘贴聊天文本')
      return
    }
    toast.loading('解析中')
    captureService.parseCapture({
      sourceType: 'clipboard',
      rawText
    }).then((res) => {
      this.setData({
        parserResult: res.parserResult,
        confidenceScore: res.confidenceScore,
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
