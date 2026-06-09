const crypto = require('node:crypto')
const mysql = require('mysql2/promise')

const DEFAULT_PAGE_SIZE = 10
const DEFAULT_MAX_PAGE_SIZE = 100
const DEFAULT_MAX_QUERY_ROWS = 5000
const DEFAULT_MAX_BULK_ROWS = 500
const DEFAULT_MAX_EVENT_BYTES = 1048576

let pool

function getEnv(name, fallback = '') {
  const value = process.env[name]
  return value == null || value === '' ? fallback : String(value).trim()
}

function getPool() {
  if (pool) return pool

  const host = getEnv('ORDERLY_INVENTORY_SQL_HOST')
  const user = getEnv('ORDERLY_INVENTORY_SQL_USER')
  const password = getEnv('ORDERLY_INVENTORY_SQL_PASSWORD')
  const database = getEnv('ORDERLY_INVENTORY_SQL_DATABASE')
  const port = Number(getEnv('ORDERLY_INVENTORY_SQL_PORT', '3306'))

  if (!host || !user || !database) {
    throw createError(500, 'sql_config_missing', '库存 SQL 连接环境变量未配置完整。')
  }

  pool = mysql.createPool({
    host,
    port,
    user,
    password,
    database,
    waitForConnections: true,
    connectionLimit: Number(getEnv('ORDERLY_INVENTORY_SQL_CONNECTION_LIMIT', '4')),
    decimalNumbers: true,
    dateStrings: true
  })
  return pool
}

function createError(statusCode, code, message, expose = statusCode < 500) {
  const error = new Error(message)
  error.statusCode = statusCode
  error.code = code
  error.expose = expose
  return error
}

function normalizeText(value) {
  return value == null ? '' : String(value).trim()
}

function normalizeNumber(value) {
  const number = Number(value || 0)
  return Number.isFinite(number) ? number : 0
}

function normalizePositiveInt(value, fallback, max) {
  const number = Number(value)
  if (!Number.isFinite(number) || number <= 0) return fallback
  return Math.min(Math.floor(number), max)
}

function getPositiveIntEnv(name, fallback, max) {
  return normalizePositiveInt(getEnv(name), fallback, max)
}

function rejectOversizedEvent(event) {
  const maxBytes = getPositiveIntEnv('ORDERLY_INVENTORY_MAX_EVENT_BYTES', DEFAULT_MAX_EVENT_BYTES, 5 * 1024 * 1024)
  const bytes = event && typeof event.body === 'string'
    ? Buffer.byteLength(event.body, 'utf8')
    : Buffer.byteLength(JSON.stringify(event || {}), 'utf8')
  if (bytes > maxBytes) {
    throw createError(413, 'payload_too_large', `库存网关请求体超过单次上限（${maxBytes} bytes）。`)
  }
}

function normalizeBool(value, fallback = true) {
  if (value == null || value === '') return fallback
  if (typeof value === 'boolean') return value
  const normalized = normalizeText(value).toLowerCase()
  if (['1', 'true', 'yes', 'y', '是', '启用', '正常'].includes(normalized)) return true
  if (['0', 'false', 'no', 'n', '否', '停用', '禁用', 'disabled'].includes(normalized)) return false
  return fallback
}

function formatDateTime(value) {
  const date = value instanceof Date ? value : new Date(value)
  if (Number.isNaN(date.getTime())) return null
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  const hour = String(date.getHours()).padStart(2, '0')
  const minute = String(date.getMinutes()).padStart(2, '0')
  const second = String(date.getSeconds()).padStart(2, '0')
  return `${year}-${month}-${day} ${hour}:${minute}:${second}`
}

function nowSql() {
  return formatDateTime(new Date())
}

function parseDateTime(value) {
  if (!value) return null
  const formatted = formatDateTime(value)
  return formatted || null
}

function toTimestamp(value) {
  if (!value) return 0
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? 0 : date.getTime()
}

function isHttpEvent(event) {
  return !!(event && (event.httpMethod || event.path || event.headers))
}

function jsonResponse(event, statusCode, payload) {
  if (!isHttpEvent(event)) return payload
  return {
    statusCode,
    headers: {
      'content-type': 'application/json; charset=utf-8'
    },
    body: JSON.stringify(payload)
  }
}

function tryParseJson(value) {
  if (typeof value !== 'string' || !value) return null
  try {
    return JSON.parse(value)
  } catch (error) {
    return null
  }
}

function normalizeRequest(event) {
  if (!event || typeof event !== 'object') return {}
  if (event.body) {
    const parsedBody = typeof event.body === 'string' ? tryParseJson(event.body) : event.body
    if (parsedBody && typeof parsedBody === 'object') {
      return Object.assign({}, parsedBody, {
        headers: event.headers || parsedBody.headers || {}
      })
    }
  }

  return Object.assign({}, event, {
    headers: event.headers || {}
  })
}

function validateToken(request) {
  const expected = getEnv('ORDERLY_INVENTORY_GATEWAY_TOKEN')
  if (!expected) {
    throw createError(500, 'gateway_token_missing', '库存云端网关未配置鉴权 token。', false)
  }

  const authHeader = normalizeText((request.headers && (request.headers.authorization || request.headers.Authorization)) || '')
  const bearer = authHeader.toLowerCase().startsWith('bearer ')
    ? authHeader.slice(7).trim()
    : ''
  if (!tokensMatch(bearer, expected)) {
    throw createError(401, 'unauthorized', '库存云端网关 token 无效。')
  }
}

function tokensMatch(supplied, expected) {
  const suppliedToken = normalizeText(supplied)
  const expectedToken = normalizeText(expected)
  if (!suppliedToken || !expectedToken) return false

  const suppliedBuffer = Buffer.from(suppliedToken, 'utf8')
  const expectedBuffer = Buffer.from(expectedToken, 'utf8')
  return suppliedBuffer.length === expectedBuffer.length
    && crypto.timingSafeEqual(suppliedBuffer, expectedBuffer)
}

function compareValue(left, right, sortBy, direction) {
  const leftValue = left[sortBy]
  const rightValue = right[sortBy]
  const result = typeof leftValue === 'string' || typeof rightValue === 'string'
    ? String(leftValue || '').localeCompare(String(rightValue || ''), 'zh-Hans-CN')
    : normalizeNumber(leftValue) - normalizeNumber(rightValue)
  return direction === 'asc' ? result : -result
}

function buildStatus(item) {
  if (!item.enabled) return { status: 'disabled', label: '停用' }
  if (item.safeStockQty > 0 && item.currentStockQty <= item.safeStockQty) return { status: 'low_stock', label: '低库存' }
  if (item.sold30dRatio >= 0.5) return { status: 'fast_selling', label: '动销偏快' }
  if (item.sold30dQty === 0) return { status: 'low_selling', label: '低动销' }
  return { status: 'normal', label: '正常' }
}

function mapDbRow(row) {
  const currentStockQty = normalizeNumber(row.current_stock_qty)
  const sold7dQty = normalizeNumber(row.sold_7d_qty)
  const sold30dQty = normalizeNumber(row.sold_30d_qty)
  const sold7dRatio = currentStockQty > 0 ? sold7dQty / currentStockQty : 0
  const sold30dRatio = currentStockQty > 0 ? sold30dQty / currentStockQty : 0
  const base = {
    materialCode: normalizeText(row.material_code),
    materialName: normalizeText(row.material_name),
    category: normalizeText(row.category),
    stockUnit: normalizeText(row.stock_unit || '件'),
    currentStockQty,
    safeStockQty: normalizeNumber(row.safe_stock_qty),
    sold7dQty,
    sold7dRatio,
    sold30dQty,
    sold30dRatio,
    unitCost: normalizeNumber(row.unit_cost),
    braceletSalePrice: normalizeNumber(row.bracelet_sale_price),
    braceletCostPrice: normalizeNumber(row.bracelet_cost_price),
    supplierName: normalizeText(row.supplier_name),
    remark: normalizeText(row.remark),
    enabled: normalizeBool(row.enabled, true),
    lastRestockedAt: row.last_restocked_at || null,
    sourceUpdatedAt: row.source_updated_at || null,
    updatedAt: row.updated_at || null
  }
  const status = buildStatus(base)
  return Object.assign({}, base, {
    status: status.status,
    statusLabel: status.label
  })
}

function sameRow(left, right) {
  return normalizeText(left.materialName) === normalizeText(right.materialName)
    && normalizeText(left.category) === normalizeText(right.category)
    && normalizeText(left.stockUnit) === normalizeText(right.stockUnit)
    && normalizeNumber(left.currentStockQty) === normalizeNumber(right.currentStockQty)
    && normalizeNumber(left.safeStockQty) === normalizeNumber(right.safeStockQty)
    && normalizeNumber(left.sold7dQty) === normalizeNumber(right.sold7dQty)
    && normalizeNumber(left.sold30dQty) === normalizeNumber(right.sold30dQty)
    && normalizeNumber(left.unitCost) === normalizeNumber(right.unitCost)
    && normalizeNumber(left.braceletSalePrice) === normalizeNumber(right.braceletSalePrice)
    && normalizeNumber(left.braceletCostPrice) === normalizeNumber(right.braceletCostPrice)
    && normalizeText(left.supplierName) === normalizeText(right.supplierName)
    && normalizeText(left.remark) === normalizeText(right.remark)
    && normalizeBool(left.enabled, true) === normalizeBool(right.enabled, true)
    && normalizeText(left.lastRestockedAt || '') === normalizeText(right.lastRestockedAt || '')
    && normalizeText(left.sourceUpdatedAt || '') === normalizeText(right.sourceUpdatedAt || '')
}

function normalizeInputRow(input) {
  return {
    materialCode: normalizeText(input.materialCode),
    materialName: normalizeText(input.materialName),
    category: normalizeText(input.category),
    stockUnit: normalizeText(input.stockUnit || '件'),
    currentStockQty: normalizeNumber(input.currentStockQty),
    safeStockQty: normalizeNumber(input.safeStockQty),
    sold7dQty: normalizeNumber(input.sold7dQty),
    sold30dQty: normalizeNumber(input.sold30dQty),
    unitCost: normalizeNumber(input.unitCost),
    braceletSalePrice: normalizeNumber(input.braceletSalePrice),
    braceletCostPrice: normalizeNumber(input.braceletCostPrice),
    supplierName: normalizeText(input.supplierName),
    remark: normalizeText(input.remark),
    enabled: normalizeBool(input.enabled, true),
    lastRestockedAt: parseDateTime(input.lastRestockedAt),
    sourceUpdatedAt: parseDateTime(input.sourceUpdatedAt)
  }
}

async function queryInventoryRows(workspaceId) {
  const maxRows = getPositiveIntEnv('ORDERLY_INVENTORY_MAX_QUERY_ROWS', DEFAULT_MAX_QUERY_ROWS, 50000)
  const [rows] = await getPool().execute(
    `SELECT
        material_code,
        material_name,
        category,
        stock_unit,
        current_stock_qty,
        safe_stock_qty,
        sold_7d_qty,
        sold_30d_qty,
        unit_cost,
        bracelet_sale_price,
        bracelet_cost_price,
        supplier_name,
        remark,
        enabled,
        last_restocked_at,
        source_updated_at,
        updated_at
      FROM inventory_items
      WHERE workspace_id = ?
      ORDER BY category ASC, material_name ASC
      LIMIT ?`,
    [workspaceId, maxRows + 1]
  )
  if (rows.length > maxRows) {
    throw createError(413, 'too_many_inventory_rows', `库存数据超过单次读取上限（${maxRows}）。`)
  }

  return rows.map(mapDbRow)
}

async function inventoryDashboard(payload, workspaceId) {
  const maxPageSize = getPositiveIntEnv('ORDERLY_INVENTORY_MAX_PAGE_SIZE', DEFAULT_MAX_PAGE_SIZE, 1000)
  const page = normalizePositiveInt(payload.page, 1, 1000000)
  const pageSize = normalizePositiveInt(payload.pageSize, DEFAULT_PAGE_SIZE, maxPageSize)
  const keyword = normalizeText(payload.keyword).toLowerCase()
  const category = normalizeText(payload.category)
  const status = normalizeText(payload.status || 'all')
  const sortBy = normalizeText(payload.sortBy || 'sold30dRatio')
  const sortDirection = normalizeText(payload.sortDirection || 'desc') === 'asc' ? 'asc' : 'desc'

  const rows = await queryInventoryRows(workspaceId)
  let items = rows.filter((item) => {
    if (keyword && (item.materialCode + item.materialName + item.category + item.supplierName).toLowerCase().indexOf(keyword) < 0) return false
    if (category && item.category !== category) return false
    if (status && status !== 'all' && item.status !== status) return false
    return true
  }).sort((left, right) => compareValue(left, right, sortBy, sortDirection))

  const total = items.length
  const pagedItems = items.slice((page - 1) * pageSize, page * pageSize)
  const avg = (values) => values.length ? values.reduce((sum, value) => sum + value, 0) / values.length : null
  const lowStockCount = items.filter((item) => item.status === 'low_stock').length
  const fastSellingCount = items.filter((item) => item.status === 'fast_selling').length
  const lowSellingCount = items.filter((item) => item.status === 'low_selling').length
  const maxUpdatedAt = rows.reduce((maxValue, item) => Math.max(maxValue, toTimestamp(item.sourceUpdatedAt || item.updatedAt)), 0)
  const avgSalePrice = avg(items.map((item) => item.braceletSalePrice).filter((value) => value > 0))
  const avgBraceletCostPrice = avg(items.map((item) => item.braceletCostPrice || item.unitCost).filter((value) => value > 0))
  const grossMarginRate = avgSalePrice && avgBraceletCostPrice && avgSalePrice > 0
    ? ((avgSalePrice - avgBraceletCostPrice) / avgSalePrice) * 100
    : null

  return {
    ok: true,
    updatedAt: maxUpdatedAt || Date.now(),
    summary: {
      avgOrderMaterialUsage: avg(items.map((item) => item.sold30dQty).filter((value) => value > 0)),
      avgMaterialUnitCost: avg(items.map((item) => item.unitCost).filter((value) => value > 0)),
      avgBraceletSalePrice: avgSalePrice,
      avgBraceletCostPrice: avgBraceletCostPrice,
      grossMarginRate,
      lowStockCount,
      fastSellingCount,
      lowSellingCount,
      inventoryHealthStatus: lowStockCount > 0 ? 'warning' : 'healthy',
      inventoryHealthSummary: lowStockCount > 0 ? `${lowStockCount} 个物料低于安全库存` : '库存风险在可控范围内',
      inventoryWarningCount: lowStockCount
    },
    filterOptions: {
      categories: Array.from(new Set(rows.map((item) => item.category).filter(Boolean))),
      statuses: [
        { value: 'all', label: '全部状态' },
        { value: 'fast_selling', label: '动销偏快' },
        { value: 'low_selling', label: '低动销' },
        { value: 'normal', label: '正常' },
        { value: 'low_stock', label: '低库存' },
        { value: 'disabled', label: '停用' }
      ],
      defaultSortBy: 'sold30dRatio',
      defaultSortDirection: 'desc'
    },
    pageInfo: {
      page,
      pageSize,
      total,
      totalPages: Math.max(1, Math.ceil(total / pageSize))
    },
    items: pagedItems
  }
}

async function inventoryExportRows(workspaceId) {
  const rows = await queryInventoryRows(workspaceId)
  return {
    ok: true,
    items: rows.map((item) => ({
      materialCode: item.materialCode,
      materialName: item.materialName,
      category: item.category,
      stockUnit: item.stockUnit,
      currentStockQty: item.currentStockQty,
      safeStockQty: item.safeStockQty,
      sold7dQty: item.sold7dQty,
      sold30dQty: item.sold30dQty,
      unitCost: item.unitCost,
      braceletSalePrice: item.braceletSalePrice,
      braceletCostPrice: item.braceletCostPrice,
      supplierName: item.supplierName,
      remark: item.remark,
      enabled: item.enabled,
      lastRestockedAt: item.lastRestockedAt,
      sourceUpdatedAt: item.sourceUpdatedAt
    }))
  }
}

async function inventoryBulkUpsert(payload, workspaceId, operatorId) {
  const inputRows = Array.isArray(payload.rows) ? payload.rows : []
  if (!inputRows.length) {
    throw createError(400, 'empty_rows', '库存导入数据不能为空。')
  }

  const maxBulkRows = getPositiveIntEnv('ORDERLY_INVENTORY_MAX_BULK_ROWS', DEFAULT_MAX_BULK_ROWS, 5000)
  if (inputRows.length > maxBulkRows) {
    throw createError(413, 'too_many_import_rows', `库存导入行数超过单次上限（${maxBulkRows}）。`)
  }

  const connection = await getPool().getConnection()
  const batchNo = `batch-${Date.now()}-${crypto.randomBytes(4).toString('hex')}`
  const now = nowSql()

  let insertedCount = 0
  let updatedCount = 0
  let unchangedCount = 0
  let skippedCount = 0

  try {
    await connection.beginTransaction()

    const [batchInsert] = await connection.execute(
      `INSERT INTO inventory_sync_batches
        (workspace_id, batch_no, source_file_name, source_file_hash, total_rows, inserted_rows, updated_rows, unchanged_rows, skipped_rows, operator_id, summary_json, created_at)
       VALUES (?, ?, ?, ?, 0, 0, 0, 0, 0, ?, ?, ?)`,
      [
        workspaceId,
        batchNo,
        normalizeText(payload.sourceFileName),
        normalizeText(payload.sourceFileHash),
        operatorId,
        JSON.stringify({ mode: 'inventoryBulkUpsert' }),
        now
      ]
    )
    const batchId = batchInsert.insertId

    for (const rawRow of inputRows) {
      const row = normalizeInputRow(rawRow)
      if (!row.materialCode) {
        skippedCount += 1
        continue
      }

      const [existingRows] = await connection.execute(
        `SELECT
            material_code,
            material_name,
            category,
            stock_unit,
            current_stock_qty,
            safe_stock_qty,
            sold_7d_qty,
            sold_30d_qty,
            unit_cost,
            bracelet_sale_price,
            bracelet_cost_price,
            supplier_name,
            remark,
            enabled,
            last_restocked_at,
            source_updated_at
          FROM inventory_items
          WHERE workspace_id = ? AND material_code = ?
          FOR UPDATE`,
        [workspaceId, row.materialCode]
      )

      const existing = existingRows[0] ? mapDbRow(existingRows[0]) : null
      if (!existing) {
        await connection.execute(
          `INSERT INTO inventory_items
            (workspace_id, material_code, material_name, category, stock_unit, current_stock_qty, safe_stock_qty, sold_7d_qty, sold_30d_qty, unit_cost, bracelet_sale_price, bracelet_cost_price, supplier_name, remark, enabled, last_restocked_at, source_updated_at, created_at, updated_at)
           VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
          [
            workspaceId,
            row.materialCode,
            row.materialName,
            row.category,
            row.stockUnit,
            row.currentStockQty,
            row.safeStockQty,
            row.sold7dQty,
            row.sold30dQty,
            row.unitCost,
            row.braceletSalePrice,
            row.braceletCostPrice,
            row.supplierName,
            row.remark,
            row.enabled ? 1 : 0,
            row.lastRestockedAt,
            row.sourceUpdatedAt,
            now,
            now
          ]
        )
        insertedCount += 1

        await connection.execute(
          `INSERT INTO inventory_item_revisions
            (workspace_id, batch_id, material_code, change_type, before_json, after_json, operator_id, created_at)
           VALUES (?, ?, ?, 'inserted', NULL, ?, ?, ?)`,
          [workspaceId, batchId, row.materialCode, JSON.stringify(row), operatorId, now]
        )
        continue
      }

      if (sameRow(existing, row)) {
        unchangedCount += 1
        continue
      }

      await connection.execute(
        `UPDATE inventory_items
            SET material_name = ?,
                category = ?,
                stock_unit = ?,
                current_stock_qty = ?,
                safe_stock_qty = ?,
                sold_7d_qty = ?,
                sold_30d_qty = ?,
                unit_cost = ?,
                bracelet_sale_price = ?,
                bracelet_cost_price = ?,
                supplier_name = ?,
                remark = ?,
                enabled = ?,
                last_restocked_at = ?,
                source_updated_at = ?,
                updated_at = ?
          WHERE workspace_id = ? AND material_code = ?`,
        [
          row.materialName,
          row.category,
          row.stockUnit,
          row.currentStockQty,
          row.safeStockQty,
          row.sold7dQty,
          row.sold30dQty,
          row.unitCost,
          row.braceletSalePrice,
          row.braceletCostPrice,
          row.supplierName,
          row.remark,
          row.enabled ? 1 : 0,
          row.lastRestockedAt,
          row.sourceUpdatedAt,
          now,
          workspaceId,
          row.materialCode
        ]
      )
      updatedCount += 1

      await connection.execute(
        `INSERT INTO inventory_item_revisions
          (workspace_id, batch_id, material_code, change_type, before_json, after_json, operator_id, created_at)
         VALUES (?, ?, ?, 'updated', ?, ?, ?, ?)`,
        [workspaceId, batchId, row.materialCode, JSON.stringify(existing), JSON.stringify(row), operatorId, now]
      )
    }

    await connection.execute(
      `UPDATE inventory_sync_batches
          SET total_rows = ?,
              inserted_rows = ?,
              updated_rows = ?,
              unchanged_rows = ?,
              skipped_rows = ?,
              summary_json = ?
        WHERE id = ?`,
      [
        inputRows.length,
        insertedCount,
        updatedCount,
        unchangedCount,
        skippedCount,
        JSON.stringify({
          totalRows: inputRows.length,
          insertedCount,
          updatedCount,
          unchangedCount,
          skippedCount
        }),
        batchId
      ]
    )

    await connection.commit()

    return {
      ok: true,
      batchNo,
      totalRows: inputRows.length,
      insertedCount,
      updatedCount,
      unchangedCount,
      skippedCount,
      updatedAt: Date.now()
    }
  } catch (error) {
    await connection.rollback()
    throw error
  } finally {
    connection.release()
  }
}

exports.main = async (event) => {
  try {
    rejectOversizedEvent(event)
    const request = normalizeRequest(event)
    const workspaceId = normalizeText(getEnv('ORDERLY_INVENTORY_WORKSPACE_ID', 'default')) || 'default'
    const operatorId = normalizeText(getEnv('ORDERLY_INVENTORY_OPERATOR_ID', 'pc-admin')) || 'pc-admin'
    const action = normalizeText(request.action)
    const payload = request.payload && typeof request.payload === 'object' ? request.payload : {}

    validateToken(request)

    let result
    if (action === 'inventoryDashboard') {
      result = await inventoryDashboard(payload, workspaceId)
    } else if (action === 'inventoryExportRows') {
      result = await inventoryExportRows(workspaceId)
    } else if (action === 'inventoryBulkUpsert') {
      result = await inventoryBulkUpsert(payload, workspaceId, operatorId)
    } else if (action === 'ping') {
      result = { ok: true, workspaceId, operatorId, at: Date.now() }
    } else {
      throw createError(400, 'unsupported_action', `不支持的 action：${action || '(empty)'}`)
    }

    return jsonResponse(event, 200, result)
  } catch (error) {
    const statusCode = error.statusCode || 500
    return jsonResponse(event, statusCode, {
      ok: false,
      code: error.code || 'internal_error',
      message: error.expose ? error.message : '库存云端网关执行失败。'
    })
  }
}
