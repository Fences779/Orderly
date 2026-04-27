const cloud = require('wx-server-sdk')
const { createSeed } = require('./seedData')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()

const COLLECTIONS = ['customers', 'deals', 'quotes', 'sku_catalog', 'followup_tasks', 'message_templates', 'captures', 'activity_logs']

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
  const workspaceId = event.workspaceId || 'default'
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
