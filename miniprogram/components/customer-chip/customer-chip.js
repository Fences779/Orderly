const { getPlatformLabel } = require('../../constants/platforms')

Component({
  properties: {
    customer: Object,
    selected: Boolean
  },
  observers: {
    customer: function(customer) {
      this.setData({
        platformText: getPlatformLabel(customer && customer.platform),
        avatarText: customer && customer.name ? customer.name.slice(0, 1) : '?'
      })
    }
  },
  data: {
    platformText: '',
    avatarText: '?'
  },
  methods: {
    onTap() {
      this.triggerEvent('select', { customer: this.data.customer })
    }
  }
})
