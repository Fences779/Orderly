const { getFollowupType } = require('../../constants/followupTypes')
const { formatDateTime } = require('../../utils/time')

Component({
  properties: {
    task: Object
  },
  observers: {
    task: function(task) {
      if (!task) return
      this.setData({
        typeText: getFollowupType(task.triggerType).label,
        dueText: formatDateTime(task.dueAt)
      })
    }
  },
  data: {
    typeText: '',
    dueText: ''
  },
  methods: {
    onCopy() { this.triggerEvent('copy', { task: this.data.task }) },
    onDone() { this.triggerEvent('done', { task: this.data.task }) },
    onSkip() { this.triggerEvent('skip', { task: this.data.task }) },
    onDelay() { this.triggerEvent('delay', { task: this.data.task }) },
    onOpen() { this.triggerEvent('open', { task: this.data.task }) }
  }
})
