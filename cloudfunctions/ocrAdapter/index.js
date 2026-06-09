const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })
const ALLOWED_OPENIDS_ENV_NAME = 'ORDERLY_ALLOWED_OPENIDS'
const MAX_EVENT_BYTES = 65536

function requireOperatorId() {
  const operatorId = cloud.getWXContext().OPENID || ''
  if (!operatorId) return { ok: false, code: 'unauthorized', message: '未授权调用。' }
  if (!isAllowedOperatorId(operatorId)) return { ok: false, code: 'forbidden', message: '无权访问。' }
  return { ok: true, operatorId }
}

function normalizeList(value) {
  return String(value || '')
    .split(/[,\s，、;；]+/)
    .map((item) => item.trim())
    .filter(Boolean)
}

function isAllowedOperatorId(operatorId) {
  const allowed = normalizeList(process.env[ALLOWED_OPENIDS_ENV_NAME])
  return allowed.length === 0 || allowed.indexOf(operatorId) >= 0
}

function rejectOversizedEvent(event) {
  const bytes = Buffer.byteLength(JSON.stringify(event || {}), 'utf8')
  return bytes > MAX_EVENT_BYTES ? { ok: false, code: 'payload_too_large', message: '请求体过大。' } : null
}

exports.main = async (event) => {
  event = event || {}
  const oversized = rejectOversizedEvent(event)
  if (oversized) return oversized

  const auth = requireOperatorId()
  if (!auth.ok) return auth

  const provider = event.provider || process.env.OCR_PROVIDER || 'mock'
  if (provider !== 'mock') {
    return {
      ok: true,
      provider,
      fileID: event.fileID || '',
      ocrText: '',
      confidenceScore: 0,
      message: '当前 OCR provider 未配置真实密钥，已降级为空文本，可在页面手动粘贴修正。'
    }
  }
  return {
    ok: true,
    provider: 'mock',
    fileID: event.fileID || '',
    ocrText: '客户：想定制一条简约珍珠项链，生日送人，这周要，预算 300 左右，白色，高级一点。',
    confidenceScore: 72,
    message: 'mock OCR 已返回示例文本，可直接修改后解析。'
  }
}
