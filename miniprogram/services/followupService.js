const cloud = require('./cloud')
const { renderTemplate } = require('../utils/templateRender')

function list(params) {
  const query = { taskStatus: params && params.status ? params.status : 'pending' }
  if (params && params.triggerType) query.triggerType = params.triggerType
  return cloud.listByQuery(cloud.COLLECTIONS.followups, query, { orderBy: { field: 'priorityScore', direction: 'desc' }, limit: 100 }).then(function(rows) {
    return rows.sort(function(a, b) {
      if ((b.priorityScore || 0) !== (a.priorityScore || 0)) return (b.priorityScore || 0) - (a.priorityScore || 0)
      return String(a.dueAt || '').localeCompare(String(b.dueAt || ''))
    })
  })
}

function scan() {
  return cloud.call('followupScan', {})
}

function updateTask(id, action, payload) {
  return cloud.call('followupScan', {
    mode: 'taskAction',
    taskId: id,
    action,
    payload: payload || {}
  })
}

function createManual(data) {
  return cloud.call('followupScan', {
    mode: 'manualCreate',
    task: data
  })
}

function renderTaskText(task, scope) {
  return renderTemplate(task.suggestedText || '', scope || {})
}

module.exports = {
  list,
  scan,
  updateTask,
  createManual,
  renderTaskText
}
