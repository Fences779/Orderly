const cloud = require('./cloud')
const { extractVariables, renderTemplate } = require('../utils/templateRender')

function list(params) {
  const query = {}
  if (params && params.sceneType) query.sceneType = params.sceneType
  return cloud.listByQuery(cloud.COLLECTIONS.templates, query, { orderBy: { field: 'updatedAt', direction: 'desc' }, limit: 100 })
}

function save(template) {
  const data = Object.assign({}, template, {
    variables: extractVariables(template.content)
  })
  return cloud.call('followupScan', {
    mode: 'templateSave',
    template: data
  })
}

function preview(template, scope) {
  return renderTemplate(template.content || '', scope || {})
}

function markUse(templateId) {
  if (!templateId) return Promise.resolve({ ok: true })
  return cloud.call('followupScan', {
    mode: 'templateUse',
    templateId
  })
}

module.exports = {
  list,
  save,
  preview,
  markUse
}
