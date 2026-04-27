const { DEAL_STAGES } = require('../constants/dealStages')

function required(value) {
  return value !== undefined && value !== null && String(value).trim() !== ''
}

function validMoney(value) {
  if (value === '' || value === undefined || value === null) return true
  const num = Number(value)
  return Number.isFinite(num) && num >= 0
}

function validStage(stage) {
  return DEAL_STAGES.indexOf(stage) >= 0
}

function ensureArray(value) {
  if (!value) return []
  if (Array.isArray(value)) return value
  if (typeof value === 'string') {
    return value.split(/[,\s，、/]+/).map(function(item) { return item.trim() }).filter(Boolean)
  }
  return []
}

function cleanNumber(value) {
  const num = Number(value || 0)
  return Number.isFinite(num) ? num : 0
}

module.exports = {
  required,
  validMoney,
  validStage,
  ensureArray,
  cleanNumber
}
