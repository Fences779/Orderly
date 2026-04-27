const cloud = require('./cloud')

function parseCapture(payload) {
  return cloud.call('captureParse', payload)
}

function getCapture(id) {
  return cloud.getById(cloud.COLLECTIONS.captures, id).then(function(capture) {
    if (!capture) return null
    const result = capture.parserResult || {}
    return cloud.call('captureParse', {
      mode: 'matchOnly',
      parserResult: result,
      rawText: capture.rawText || capture.ocrText || ''
    }).then(function(matchRes) {
      return Object.assign({}, capture, { customerMatches: matchRes.customerMatches || [] })
    }).catch(function() {
      return Object.assign({}, capture, { customerMatches: [] })
    })
  })
}

function confirm(payload) {
  return cloud.call('captureConfirm', payload)
}

module.exports = {
  parseCapture,
  getCapture,
  confirm
}
