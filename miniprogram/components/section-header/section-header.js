Component({
  properties: {
    title: String,
    subtitle: String,
    actionText: String
  },
  methods: {
    onAction() {
      this.triggerEvent('action')
    }
  }
})
