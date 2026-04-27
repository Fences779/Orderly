const templateService = require('../../services/templateService')
const clipboard = require('../../utils/clipboard')
const toast = require('../../utils/toast')
const { TEMPLATE_SCENES } = require('../../constants/templateScenes')

Page({
  data: {
    scenes: [{ key: '', label: '全部场景' }].concat(TEMPLATE_SCENES),
    sceneType: '',
    templates: [],
    previewScope: {
      customerName: '小林',
      productName: '定制手串',
      quoteAmount: '¥368',
      deadline: '本周五',
      lastPurchaseDate: '上次购买'
    },
    form: {
      sceneType: 'quote_followup',
      title: '',
      content: '',
      toneStyle: '自然',
      enabled: true
    }
  },

  onShow() {
    this.load()
  },

  load() {
    templateService.list({ sceneType: this.data.sceneType }).then((templates) => {
      const rows = (templates || []).map((item) => Object.assign({}, item, {
        previewText: templateService.preview(item, this.data.previewScope)
      }))
      this.setData({ templates: rows })
    }).catch((err) => toast.error(err.message || '模板加载失败'))
  },

  pickScene(e) {
    const item = this.data.scenes[Number(e.detail.value)]
    this.setData({ sceneType: item.key })
    this.load()
  },

  onInput(e) {
    const field = e.currentTarget.dataset.field
    this.setData({ ['form.' + field]: e.detail.value })
  },

  edit(e) {
    this.setData({ form: Object.assign({}, e.detail.template) })
  },

  save() {
    if (!this.data.form.title || !this.data.form.content) {
      toast.error('标题和内容必填')
      return
    }
    templateService.save(this.data.form).then(() => {
      toast.success('已保存')
      this.setData({ form: { sceneType: 'quote_followup', title: '', content: '', toneStyle: '自然', enabled: true } })
      this.load()
    }).catch((err) => toast.error(err.message || '保存失败'))
  },

  copy(e) {
    clipboard.setClipboardText(e.detail.text || '').then(() => {
      const template = e.detail.template || {}
      return templateService.markUse(template._id)
    }).then(() => {
      toast.success('已复制')
      this.load()
    })
  },

  previewOf(template) {
    return templateService.preview(template, this.data.previewScope)
  }
})
