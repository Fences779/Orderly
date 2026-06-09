const cloud = require('wx-server-sdk')

cloud.init({ env: cloud.DYNAMIC_CURRENT_ENV })

function requireOperatorId() {
  const operatorId = cloud.getWXContext().OPENID || ''
  if (!operatorId) return { ok: false, code: 'unauthorized', message: '未授权调用。' }
  return { ok: true, operatorId }
}

exports.main = async (event) => {
  event = event || {}
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
    ocrText: '客户：想定制一条简约珍珠项链，生日送人，这周要，预算 300 左右，白色，高级一点。vx: lin_vx88',
    confidenceScore: 72,
    message: 'mock OCR 已返回示例文本，可直接修改后解析。'
  }
}
