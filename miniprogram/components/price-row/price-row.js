const { currency } = require('../../utils/format')

Component({
  properties: {
    label: String,
    amount: {
      type: null,
      value: 0
    },
    strong: Boolean
  },
  observers: {
    amount: function(amount) {
      this.setData({ amountText: currency(amount) })
    }
  },
  data: {
    amountText: '¥0'
  }
})
