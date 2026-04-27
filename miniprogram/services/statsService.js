const cloud = require('./cloud')

function summary(period) {
  return cloud.call('statsSummary', { period: period || '7d' })
}

module.exports = {
  summary
}
