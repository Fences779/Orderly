# 库存 CloudBase SQL 落地

本次库存模块默认采用：

- CloudBase MySQL 作为库存主库
- `cloudfunctions/inventorySqlGateway` 作为桌面端调用入口
- 桌面端通过环境变量访问 inventory gateway
- Excel 只做导入、确认回写、手动导出

## 1. SQL 建表

在 CloudBase MySQL 控制台执行：

- [scripts/cloudbase/inventory_schema.sql](/D:/Dev/Orderly-SN/scripts/cloudbase/inventory_schema.sql)

涉及 3 张表：

- `inventory_items`
- `inventory_sync_batches`
- `inventory_item_revisions`

## 2. 部署 gateway

部署目录：

- [cloudfunctions/inventorySqlGateway](/D:/Dev/Orderly-SN/cloudfunctions/inventorySqlGateway)

函数需要配置以下环境变量：

- `ORDERLY_INVENTORY_GATEWAY_TOKEN`
- `ORDERLY_INVENTORY_SQL_HOST`
- `ORDERLY_INVENTORY_SQL_PORT`
- `ORDERLY_INVENTORY_SQL_USER`
- `ORDERLY_INVENTORY_SQL_PASSWORD`
- `ORDERLY_INVENTORY_SQL_DATABASE`
- `ORDERLY_INVENTORY_SQL_SSL_MODE`（非本地 SQL 默认 required）
- `ORDERLY_INVENTORY_SQL_SSL_CA`（如数据库证书链需要自定义 CA）
- `ORDERLY_INVENTORY_SQL_SSL_REJECT_UNAUTHORIZED`（默认 true）
- `ORDERLY_INVENTORY_SQL_ALLOW_PLAINTEXT`（仅本地或受控内网临时明文时显式设置）
- `ORDERLY_INVENTORY_GATEWAY_MIN_TOKEN_LENGTH`（默认 24）
- `ORDERLY_INVENTORY_WORKSPACE_ID`
- `ORDERLY_INVENTORY_OPERATOR_ID`

建议把函数通过 CloudBase HTTP 访问服务暴露为固定路由，例如 `/inventory`.

## 3. 桌面端环境变量

桌面端读取以下环境变量：

- `ORDERLY_INVENTORY_GATEWAY_ENDPOINT`
- `ORDERLY_INVENTORY_GATEWAY_TOKEN`
- `ORDERLY_INVENTORY_GATEWAY_TIMEOUT_SECONDS`
- `ORDERLY_INVENTORY_WORKSPACE_ID`
- `ORDERLY_INVENTORY_OPERATOR_ID`

示例：

```powershell
$env:ORDERLY_INVENTORY_GATEWAY_ENDPOINT="https://your-domain/inventory"
$env:ORDERLY_INVENTORY_GATEWAY_TOKEN="replace-me"
$env:ORDERLY_INVENTORY_WORKSPACE_ID="default"
$env:ORDERLY_INVENTORY_OPERATOR_ID="pc-admin"
```

## 4. 当前闭环

库存页当前支持：

- 从云端库存主库刷新看板
- 导入 Excel 后本地预分析
- 人工确认后批量同步云端
- 同步成功后自动备份并回写原 Excel
- 手动导出当前云端库存快照
