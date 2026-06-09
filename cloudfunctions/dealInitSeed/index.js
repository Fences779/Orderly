const cloud = require('wx-server-sdk')
const { createSeed } = require('./seedData')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const db = cloud.database()

const COLLECTIONS = ['customers', 'deals', 'quotes', 'sku_catalog', 'inventory_movements', 'cashflow_entries', 'followup_tasks', 'message_templates', 'captures', 'activity_logs']
const DEFAULT_WORKSPACE_ID = 'default'
const ALLOWED_WORKSPACE_IDS_ENV_NAME = 'ORDERLY_ALLOWED_WORKSPACE_IDS'
const ENABLE_SEED_ENV_NAME = 'ORDERLY_ENABLE_DEAL_INIT_SEED'
const SEED_ADMIN_OPENIDS_ENV_NAME = 'ORDERLY_DEAL_INIT_SEED_OPENIDS'
const MAX_EVENT_BYTES = 65536

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

function rejectOversizedEvent(event) {
  const bytes = Buffer.byteLength(JSON.stringify(event || {}), 'utf8')
  return bytes > MAX_EVENT_BYTES ? { ok: false, code: 'payload_too_large', message: '请求体过大。' } : null
}

function resolveWorkspaceId(event) {
  const workspaceId = String((event && event.workspaceId) || '').trim() || DEFAULT_WORKSPACE_ID
  const configured = normalizeList(process.env[ALLOWED_WORKSPACE_IDS_ENV_NAME])
  const allowed = configured.length ? configured : [DEFAULT_WORKSPACE_ID]
  if (allowed.indexOf(workspaceId) < 0) return { ok: false, code: 'workspace_forbidden', message: '无权访问该工作区。' }
  return { ok: true, workspaceId }
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
  const oversized = rejectOversizedEvent(request)
  if (oversized) return oversized

  const workspace = resolveWorkspaceId(request)
  if (!workspace.ok) return workspace
  const workspaceId = workspace.workspaceId
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
