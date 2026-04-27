Component({
  properties: {
    title: String,
    desc: String,
    actionText: String
  },
  methods: {
    onAction() {
      this.triggerEvent('action')
    }
  }
})
