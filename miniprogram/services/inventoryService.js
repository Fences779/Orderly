const cloud = require('./cloud')

function list(includeDisabled) {
  return cloud.listByQuery(cloud.COLLECTIONS.sku, {}, { orderBy: { field: 'sortOrder', direction: 'asc' }, limit: 200 }).then(function(rows) {
    return includeDisabled ? rows : rows.filter(function(item) { return item.enabled !== false })
  })
}

function recentMovements(limit) {
  return cloud.listByQuery(cloud.COLLECTIONS.inventoryMovements, {}, { orderBy: { field: 'occurredAt', direction: 'desc' }, limit: limit || 50 })
}

function saveSku(sku) {
  return cloud.call('followupScan', {
    mode: 'skuSave',
    sku
  })
}

function saveMovement(movement) {
  return cloud.call('followupScan', {
    mode: 'inventoryMovementSave',
    movement
  })
}

module.exports = {
  list,
  recentMovements,
  saveSku,
  saveMovement
}
