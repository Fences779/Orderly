Component({
  properties: {
    label: String,
    value: {
      type: null,
      value: ''
    },
    placeholder: String,
    type: {
      type: String,
      value: 'text'
    },
    multiline: Boolean
  },
  methods: {
    onInput(e) {
      this.triggerEvent('change', { value: e.detail.value })
    }
  }
})
