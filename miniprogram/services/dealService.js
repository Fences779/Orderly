const cloud = require('./cloud')
const stages = require('../constants/dealStages')

function decorateDeal(row) {
  return Object.assign({}, row, {
    stageLabel: stages.getStageLabel(row.dealStage),
    stageTone: stages.getStageTone(row.dealStage),
    nextStages: stages.getNextStages(row.dealStage)
  })
}

function list(params) {
  const options = { orderBy: { field: 'updatedAt', direction: 'desc' }, limit: 100 }
  return cloud.listByQuery(cloud.COLLECTIONS.deals, {}, options).then(function(rows) {
    const filters = params || {}
    return rows.filter(function(item) {
      const stageOk = !filters.stage || item.dealStage === filters.stage
      const platformOk = !filters.platform || item.sourceEntry === filters.platform
      const categoryOk = !filters.intentCategory || item.intentCategory === filters.intentCategory
      const highOk = !filters.highPriority || item.priorityLevel === 'high' || item.urgencyLevel === 'high'
      return stageOk && platformOk && categoryOk && highOk
    }).map(decorateDeal)
  })
}

function board(params) {
  return list(params).then(function(rows) {
    return stages.BOARD_COLUMNS.map(function(column) {
      return Object.assign({}, column, {
        deals: rows.filter(function(item) {
          return column.stages.indexOf(item.dealStage) >= 0
        })
      })
    })
  })
}

function detail(id) {
  if (!id) return Promise.resolve(null)
  return Promise.all([
    cloud.getById(cloud.COLLECTIONS.deals, id),
    cloud.listByQuery(cloud.COLLECTIONS.quotes, { dealId: id }, { orderBy: { field: 'createdAt', direction: 'desc' }, limit: 10 }),
    cloud.listByQuery(cloud.COLLECTIONS.followups, { dealId: id }, { orderBy: { field: 'dueAt', direction: 'asc' }, limit: 20 }),
    cloud.listByQuery(cloud.COLLECTIONS.logs, { entityId: id }, { orderBy: { field: 'createdAt', direction: 'desc' }, limit: 30 })
  ]).then(function(res) {
    const deal = res[0] ? decorateDeal(res[0]) : null
    if (!deal) return null
    return cloud.getById(cloud.COLLECTIONS.customers, deal.customerId).then(function(customer) {
      return {
        deal,
        customer,
        quotes: res[1] || [],
        followups: res[2] || [],
        logs: res[3] || []
      }
    })
  })
}

function upsert(data) {
  return cloud.call('dealUpsert', { deal: data })
}

function updateStage(id, toStage, note) {
  return cloud.call('dealStageUpdate', { dealId: id, toStage, note: note || '' })
}

module.exports = {
  list,
  board,
  detail,
  upsert,
  updateStage,
  decorateDeal
}
