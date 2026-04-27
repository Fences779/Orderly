const cloud = require('./cloud')
const format = require('../utils/format')
const { cleanNumber } = require('../utils/validators')
const { renderTemplate } = require('../utils/templateRender')

function calculate(input) {
  const items = input.items || []
  const itemBase = items.reduce(function(sum, item) {
    return sum + cleanNumber(item.qty || 1) * cleanNumber(item.price)
  }, 0)
  const baseAmount = cleanNumber(input.baseAmount || itemBase)
  const customFee = cleanNumber(input.customFee)
  const laborFee = cleanNumber(input.laborFee)
  const shippingFee = cleanNumber(input.shippingFee)
  const discountAmount = cleanNumber(input.discountAmount)
  const totalAmount = Math.max(0, baseAmount + customFee + laborFee + shippingFee - discountAmount)
  return Object.assign({}, input, {
    baseAmount,
    customFee,
    laborFee,
    shippingFee,
    discountAmount,
    totalAmount,
    depositRequired: cleanNumber(input.depositRequired)
  })
}

function listByDeal(dealId) {
  return cloud.listByQuery(cloud.COLLECTIONS.quotes, { dealId }, { orderBy: { field: 'createdAt', direction: 'desc' }, limit: 20 })
}

function get(id) {
  return cloud.getById(cloud.COLLECTIONS.quotes, id)
}

function save(quote, action) {
  return cloud.call('quoteCreateOrUpdate', { quote: calculate(quote), action: action || 'draft' })
}

function copyText(quote, deal, customer) {
  const rows = (quote.items || []).map(function(item) {
    return '- ' + (item.name || '报价项') + ' x' + (item.qty || 1) + '：' + format.currency(item.price || 0)
  }).join('\n')
  return renderTemplate('亲爱的{{customerName}}，这是{{productName}}的报价：\n{{rows}}\n合计：{{amount}}\n定金：{{deposit}}\n有效期至：{{validUntil}}\n{{note}}', {
    customerName: customer && customer.name ? customer.name : '您',
    productName: deal && deal.title ? deal.title : '这次需求',
    rows,
    amount: format.currency(quote.totalAmount),
    deposit: format.currency(quote.depositRequired),
    validUntil: quote.validUntil || '以沟通为准',
    note: quote.quoteNote || ''
  })
}

module.exports = {
  calculate,
  listByDeal,
  get,
  save,
  copyText
}
