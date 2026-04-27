Component({
  properties: {
    template: Object,
    preview: String
  },
  methods: {
    onCopy() {
      this.triggerEvent('copy', { text: this.data.preview, template: this.data.template })
    },
    onEdit() {
      this.triggerEvent('edit', { template: this.data.template })
    }
  }
})
