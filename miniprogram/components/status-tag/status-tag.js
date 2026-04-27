const stages = require('../../constants/dealStages')

Component({
  properties: {
    stage: String,
    label: String,
    tone: String
  },
  observers: {
    'stage,label,tone': function(stage, label, tone) {
      this.setData({
        viewLabel: label || stages.getStageLabel(stage),
        viewTone: tone || stages.getStageTone(stage)
      })
    }
  },
  data: {
    viewLabel: '',
    viewTone: 'gray'
  }
})
