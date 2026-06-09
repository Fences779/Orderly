const cloud = require('wx-server-sdk')
const { createSeed } = require('./seedData')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()

const COLLECTIONS = ['customers', 'deals', 'quotes', 'sku_catalog', 'inventory_movements', 'cashflow_entries', 'followup_tasks', 'message_templates', 'captures', 'activity_logs']
const ENABLE_SEED_ENV_NAME = 'ORDERLY_ENABLE_DEAL_INIT_SEED'
const SEED_ADMIN_OPENIDS_ENV_NAME = 'ORDERLY_DEAL_INIT_SEED_OPENIDS'

function normalizeList(value) {
  return String(value || '')
    .split(/[,\s，、;；]+/)
    .map((item) => item.trim())
    .filter(Boolean)
}

function requireSeedAuthorization() {
  if (process.env[ENABLE_SEED_ENV_NAME] !== '1') {
    return { ok: false, code: 'seed_disabled', message: '演示数据初始化未启用。' }
  }

  const operatorId = cloud.getWXContext().OPENID || ''
  const allowedOpenids = normalizeList(process.env[SEED_ADMIN_OPENIDS_ENV_NAME])
  if (!operatorId || !allowedOpenids.includes(operatorId)) {
    return { ok: false, code: 'unauthorized', message: '无权执行演示数据初始化。' }
  }

  return { ok: true, operatorId }
}

async function ensureCollections() {
  for (const name of COLLECTIONS) {
    try {
      await db.createCollection(name)
    } catch (err) {
      // Collection already exists or current account has no explicit create permission.
    }
  }
}

async function upsertDoc(collection, row) {
  const data = Object.assign({}, row)
  const id = data._id
  try {
    await db.collection(collection).doc(id).get()
    delete data._id
    await db.collection(collection).doc(id).update({ data })
    return 'updated'
  } catch (err) {
    delete data._id
    await db.collection(collection).doc(id).set({ data })
    return 'created'
  }
}

exports.main = async (event) => {
  const auth = requireSeedAuthorization()
  if (!auth.ok) return auth

  const request = event || {}
  const workspaceId = request.workspaceId || 'default'
  await ensureCollections()
  const seed = createSeed(workspaceId)
  const counts = {}
  for (const collection of Object.keys(seed)) {
    counts[collection] = { created: 0, updated: 0 }
    for (const row of seed[collection]) {
      const result = await upsertDoc(collection, row)
      counts[collection][result] += 1
    }
  }
  return {
    ok: true,
    counts,
    message: '演示数据已写入，可从工作台开始验收。'
  }
}
