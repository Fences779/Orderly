const appConfig = require('../config/appConfig')
const { getAppWorkspaceId } = require('../utils/guard')

const COLLECTIONS = {
  customers: 'customers',
  deals: 'deals',
  quotes: 'quotes',
  sku: 'sku_catalog',
  inventoryMovements: 'inventory_movements',
  cashflow: 'cashflow_entries',
  followups: 'followup_tasks',
  templates: 'message_templates',
  captures: 'captures',
  logs: 'activity_logs'
}

function getDb() {
  return wx.cloud.database()
}

function workspaceId() {
  return getAppWorkspaceId() || appConfig.workspaceId || 'default'
}

function isCurrentWorkspaceRow(row) {
  return row && row.workspaceId === workspaceId()
}

function call(name, data) {
  return wx.cloud.callFunction({
    name,
    data: Object.assign({}, data || {}, { workspaceId: workspaceId() })
  }).then(function(res) {
    const result = res.result || {}
    if (result.ok === false) {
      throw new Error(result.message || '云函数调用失败')
    }
    return result
  })
}

function getById(collection, id) {
  if (!id) return Promise.resolve(null)
  return getDb().collection(collection).doc(id).get().then(function(res) {
    const row = res.data || null
    return isCurrentWorkspaceRow(row) ? row : null
  }).catch(function() {
    return null
  })
}

function listByQuery(collection, query, options) {
  const db = getDb()
  let ref = db.collection(collection).where(Object.assign({}, query || {}, { workspaceId: workspaceId() }))
  const opts = options || {}
  if (opts.orderBy) {
    ref = ref.orderBy(opts.orderBy.field, opts.orderBy.direction || 'desc')
  }
  if (opts.limit) ref = ref.limit(opts.limit)
  return ref.get().then(function(res) {
    return res.data || []
  })
}

function updateById(collection, id, data) {
  return getById(collection, id).then(function(row) {
    if (!row) throw new Error('记录不存在或无权访问')
    return getDb().collection(collection).doc(id).update({
      data: Object.assign({}, data || {}, { workspaceId: workspaceId() })
    })
  })
}

module.exports = {
  COLLECTIONS,
  getDb,
  workspaceId,
  call,
  getById,
  listByQuery,
  updateById
}
