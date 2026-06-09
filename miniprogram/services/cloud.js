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

function workspaceId() {
  return getAppWorkspaceId() || appConfig.workspaceId || 'default'
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
  return call('dataRead', {
    action: 'getById',
    collection,
    id
  }).then(function(res) {
    return res.row || null
  })
}

function listByQuery(collection, query, options) {
  return call('dataRead', {
    action: 'list',
    collection,
    query: query || {},
    options: options || {}
  }).then(function(res) {
    return res.rows || []
  })
}

function updateById(collection, id, data) {
  return Promise.reject(new Error('直接更新已禁用，请使用云函数写入。'))
}

module.exports = {
  COLLECTIONS,
  workspaceId,
  call,
  getById,
  listByQuery,
  updateById
}
