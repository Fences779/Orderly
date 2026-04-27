function renderTemplate(content, data) {
  const source = content || ''
  const scope = data || {}
  return source.replace(/\{\{\s*([a-zA-Z0-9_]+)\s*\}\}/g, function(_, key) {
    const value = scope[key]
    return value === undefined || value === null ? '' : String(value)
  })
}

function extractVariables(content) {
  const source = content || ''
  const set = {}
  source.replace(/\{\{\s*([a-zA-Z0-9_]+)\s*\}\}/g, function(_, key) {
    set[key] = true
    return ''
  })
  return Object.keys(set)
}

module.exports = {
  renderTemplate,
  extractVariables
}
