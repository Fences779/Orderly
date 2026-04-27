const statsService = require('../../services/statsService')
const toast = require('../../utils/toast')
const { DATE_FILTERS } = require('../../constants/ui')

Page({
  data: {
    periods: DATE_FILTERS,
    period: '7d',
    summary: {},
    platformDistribution: [],
    templateTop: [],
    riskReasons: []
  },

  onShow() {
    this.load()
  },

  load() {
    statsService.summary(this.data.period).then((res) => {
      this.setData({
        summary: res.summary || {},
        platformDistribution: res.platformDistribution || [],
        templateTop: res.templateTop || [],
        riskReasons: res.riskReasons || []
      })
    }).catch((err) => toast.error(err.message || '统计加载失败'))
  },

  pickPeriod(e) {
    const item = this.data.periods[Number(e.detail.value)]
    this.setData({ period: item.key })
    this.load()
  }
})
