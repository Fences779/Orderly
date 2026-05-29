const cloud = require('./cloud')

function list(limit) {
  return cloud.listByQuery(cloud.COLLECTIONS.cashflow, {}, { orderBy: { field: 'occurredAt', direction: 'desc' }, limit: limit || 100 })
}

function healthDashboard(params) {
  return cloud.call('followupScan', Object.assign({
    mode: 'cashflowHealthDashboard'
  }, params || {}))
}

function save(entry) {
  return cloud.call('followupScan', {
    mode: 'cashflowSave',
    entry
  })
}

function summarize(entries) {
  return (entries || []).reduce(function(summary, entry) {
    const amount = Number(entry.amount || 0)
    if (entry.direction === 'expense') {
      summary.expenseTotal += amount
    } else {
      summary.incomeTotal += amount
    }
    summary.netAmount = summary.incomeTotal - summary.expenseTotal
    summary.entryCount += 1
    return summary
  }, { incomeTotal: 0, expenseTotal: 0, netAmount: 0, entryCount: 0 })
}

module.exports = {
  list,
  healthDashboard,
  save,
  summarize
}
