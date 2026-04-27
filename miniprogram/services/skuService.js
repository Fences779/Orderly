const cloud = require('./cloud')

function list(includeDisabled) {
  return cloud.listByQuery(cloud.COLLECTIONS.sku, {}, { orderBy: { field: 'sortOrder', direction: 'asc' }, limit: 100 }).then(function(rows) {
    return includeDisabled ? rows : rows.filter(function(item) { return item.enabled !== false })
  })
}

function save(sku) {
  return cloud.call('followupScan', {
    mode: 'skuSave',
    sku
  })
}

function toggle(id, enabled) {
  return save({ _id: id, enabled })
}

module.exports = {
  list,
  save,
  toggle
}
