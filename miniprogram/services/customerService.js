const cloud = require('./cloud')
const time = require('../utils/time')

function list(params) {
  const query = {}
  const options = { orderBy: { field: 'updatedAt', direction: 'desc' }, limit: 80 }
  return cloud.listByQuery(cloud.COLLECTIONS.customers, query, options).then(function(rows) {
    const keyword = params && params.keyword ? params.keyword.trim() : ''
    const platform = params && params.platform ? params.platform : ''
    return rows.filter(function(item) {
      const keywordOk = !keyword || (item.name || '').indexOf(keyword) >= 0 || (item.contactHandle || '').indexOf(keyword) >= 0
      const platformOk = !platform || item.platform === platform
      return keywordOk && platformOk
    })
  })
}

function get(id) {
  return cloud.getById(cloud.COLLECTIONS.customers, id)
}

function upsert(data) {
  return cloud.call('customerUpsert', { customer: data })
}

function updateProfile(id, patch) {
  return cloud.call('customerUpsert', {
    customer: Object.assign({}, patch, { _id: id, updatedAt: time.now() })
  })
}

module.exports = {
  list,
  get,
  upsert,
  updateProfile
}
