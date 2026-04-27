const skuService = require('../../services/skuService')
const toast = require('../../utils/toast')

Page({
  data: {
    skus: [],
    form: {
      name: '',
      category: '',
      basePrice: '',
      costPrice: '',
      specSchema: '',
      tags: '',
      sortOrder: 10,
      enabled: true
    }
  },

  onShow() {
    this.load()
  },

  load() {
    skuService.list(true).then((skus) => this.setData({ skus })).catch((err) => toast.error(err.message || 'SKU 加载失败'))
  },

  onInput(e) {
    const field = e.currentTarget.dataset.field
    this.setData({ ['form.' + field]: e.detail.value })
  },

  edit(e) {
    const sku = this.data.skus[Number(e.currentTarget.dataset.index)]
    if (sku) this.setData({ form: Object.assign({}, sku) })
  },

  save() {
    if (!this.data.form.name) {
      toast.error('请填写 SKU 名称')
      return
    }
    skuService.save(this.data.form).then(() => {
      toast.success('已保存')
      this.setData({ form: { name: '', category: '', basePrice: '', costPrice: '', specSchema: '', tags: '', sortOrder: 10, enabled: true } })
      this.load()
    }).catch((err) => toast.error(err.message || '保存失败'))
  },

  toggle(e) {
    const sku = this.data.skus[Number(e.currentTarget.dataset.index)]
    if (!sku) return
    skuService.toggle(sku._id, !sku.enabled).then(() => this.load())
  }
})
