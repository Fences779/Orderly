const { formatDateTime } = require('../../utils/time')

Component({
  properties: {
    logs: Array
  },
  observers: {
    logs: function(logs) {
      this.setData({
        viewLogs: (logs || []).map(function(item) {
          return Object.assign({}, item, { timeText: formatDateTime(item.createdAt) })
        })
      })
    }
  },
  data: {
    viewLogs: []
  }
})
