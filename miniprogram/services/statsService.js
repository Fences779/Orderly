const cloud = require('./cloud')

function summary(period) {
  return cloud.call('statsSummary', { period: period || '7d' })
}

function workbenchDashboard() {
  return summary('today').then(function(res) {
    return res.workbenchDashboard || {}
  })
}

module.exports = {
  summary,
  workbenchDashboard
}
