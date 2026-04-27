const DEFAULT_REPURCHASE_DAYS = 30

const CATEGORY_REPURCHASE_DAYS = {
  手串: 45,
  项链: 60,
  礼物: 90,
  配件: 30,
  定制: 60
}

function getRepurchaseDays(category) {
  return CATEGORY_REPURCHASE_DAYS[category] || DEFAULT_REPURCHASE_DAYS
}

module.exports = {
  DEFAULT_REPURCHASE_DAYS,
  CATEGORY_REPURCHASE_DAYS,
  getRepurchaseDays
}
