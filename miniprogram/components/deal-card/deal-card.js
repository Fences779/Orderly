const { getStageLabel } = require('../../constants/dealStages')
const { getPlatformLabel } = require('../../constants/platforms')
const { formatDateTime } = require('../../utils/time')
const { currency } = require('../../utils/format')

Component({
  properties: {
    deal: Object,
    compact: {
      type: Boolean,
      value: false
    }
  },
  observers: {
    deal: function(deal) {
      if (!deal) return
      this.setData({
        stageText: getStageLabel(deal.dealStage),
        platformText: getPlatformLabel(deal.sourceEntry),
        updatedText: formatDateTime(deal.updatedAt),
        budgetText: deal.budgetMax ? currency(deal.budgetMin || 0) + '-' + currency(deal.budgetMax) : '未填预算'
      })
    }
  },
  data: {
    stageText: '',
    platformText: '',
    updatedText: '',
    budgetText: ''
  },
  methods: {
    onOpen() {
      this.triggerEvent('open', { id: this.data.deal && this.data.deal._id })
    },
    onAdvance() {
      this.triggerEvent('advance', { deal: this.data.deal })
    }
  }
})
