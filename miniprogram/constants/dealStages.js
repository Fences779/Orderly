const DEAL_STAGES = [
  'new_inquiry',
  'needs_clarification',
  'quote_preparing',
  'quote_sent',
  'waiting_deposit',
  'scheduled',
  'in_production',
  'ready_to_ship',
  'shipped',
  'received',
  'completed',
  'repurchase_due',
  'dormant',
  'lost'
]

const DEAL_STAGE_LABELS = {
  new_inquiry: '新咨询',
  needs_clarification: '待补信息',
  quote_preparing: '待报价',
  quote_sent: '待回复',
  waiting_deposit: '待确认',
  scheduled: '已排期',
  in_production: '制作中',
  ready_to_ship: '待发货',
  shipped: '已发货',
  received: '已收货',
  completed: '已完成',
  repurchase_due: '待复购',
  dormant: '休眠',
  lost: '流失'
}

const DEAL_STAGE_TONE = {
  new_inquiry: 'blue',
  needs_clarification: 'orange',
  quote_preparing: 'orange',
  quote_sent: 'purple',
  waiting_deposit: 'purple',
  scheduled: 'green',
  in_production: 'green',
  ready_to_ship: 'green',
  shipped: 'green',
  received: 'green',
  completed: 'gray',
  repurchase_due: 'teal',
  dormant: 'gray',
  lost: 'red'
}

const STAGE_TRANSITIONS = {
  new_inquiry: ['needs_clarification', 'quote_preparing', 'lost'],
  needs_clarification: ['quote_preparing', 'dormant', 'lost'],
  quote_preparing: ['quote_sent'],
  quote_sent: ['waiting_deposit', 'quote_preparing', 'dormant', 'lost'],
  waiting_deposit: ['scheduled', 'lost'],
  scheduled: ['in_production'],
  in_production: ['ready_to_ship'],
  ready_to_ship: ['shipped'],
  shipped: ['received'],
  received: ['completed'],
  completed: ['repurchase_due', 'dormant'],
  repurchase_due: [],
  dormant: ['new_inquiry', 'needs_clarification', 'quote_preparing'],
  lost: []
}

const BOARD_COLUMNS = [
  { key: 'inquiry', title: '新咨询', stages: ['new_inquiry'] },
  { key: 'clarify', title: '待补信息', stages: ['needs_clarification'] },
  { key: 'quote', title: '待报价', stages: ['quote_preparing'] },
  { key: 'reply', title: '待回复', stages: ['quote_sent'] },
  { key: 'confirm', title: '待确认', stages: ['waiting_deposit'] },
  { key: 'production', title: '制作中', stages: ['scheduled', 'in_production'] },
  { key: 'shipping', title: '待发货 / 已发货', stages: ['ready_to_ship', 'shipped', 'received'] },
  { key: 'done', title: '已完成 / 待复购 / 流失', stages: ['completed', 'repurchase_due', 'dormant', 'lost'] }
]

const WON_STAGES = ['scheduled', 'in_production', 'ready_to_ship', 'shipped', 'received', 'completed', 'repurchase_due']

function isValidStage(stage) {
  return DEAL_STAGES.indexOf(stage) >= 0
}

function canTransition(fromStage, toStage) {
  if (!isValidStage(toStage)) return false
  if (!fromStage || fromStage === toStage) return true
  const options = STAGE_TRANSITIONS[fromStage] || []
  return options.indexOf(toStage) >= 0
}

function getNextStages(stage) {
  return (STAGE_TRANSITIONS[stage] || []).map(function(key) {
    return { key, label: DEAL_STAGE_LABELS[key] }
  })
}

function getStageLabel(stage) {
  return DEAL_STAGE_LABELS[stage] || stage || '未知'
}

function getStageTone(stage) {
  return DEAL_STAGE_TONE[stage] || 'gray'
}

function isWonStage(stage) {
  return WON_STAGES.indexOf(stage) >= 0
}

function getBoardColumn(stage) {
  for (let i = 0; i < BOARD_COLUMNS.length; i += 1) {
    if (BOARD_COLUMNS[i].stages.indexOf(stage) >= 0) return BOARD_COLUMNS[i]
  }
  return BOARD_COLUMNS[0]
}

module.exports = {
  DEAL_STAGES,
  DEAL_STAGE_LABELS,
  DEAL_STAGE_TONE,
  STAGE_TRANSITIONS,
  BOARD_COLUMNS,
  WON_STAGES,
  isValidStage,
  canTransition,
  getNextStages,
  getStageLabel,
  getStageTone,
  isWonStage,
  getBoardColumn
}
