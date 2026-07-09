# ECS Backup And Restore Runbook

本 runbook 只做生产前准备说明，不执行备份或恢复。

## 备份范围

- PostgreSQL：业务数据、账号、权限、设备、审计、幂等、Cursor、附件元数据。
- 附件文件：OSS 中的 `attachments/` 对象，或紧急 fallback 下的 `/opt/orderly/data/object-storage`。
- 导出文件：`/opt/orderly/exports` 与 OSS export prefix，按业务需要保留。
- 配置：`/opt/orderly/env/orderly.prod.env` 只备份到受控密钥库，不进入普通文件备份包。

## PostgreSQL 每日备份

当前 API 容器内置 `BackupBackgroundService`，默认每天 UTC 02:00 生成 PostgreSQL custom dump。

生产要求：

- `ORDERLY_BACKUP_RETENTION_DAYS=30`
- `ORDERLY_LOCAL_BACKUP_DIR=/opt/orderly/backups`
- OSS 配置完整时，备份会上传到 `ORDERLY_OSS_BACKUP_PREFIX`
- 本机和异地备份至少保留 30 天
- `/health/backups` 必须纳入部署后观察

## 异地备份

推荐第一版使用 OSS：

- Bucket 不公开读。
- 开启服务端加密或使用外部加密流程。
- Access Key 最小权限，只允许指定 bucket 和 prefix。
- 备份 prefix 与附件 prefix 分开。
- `ORDERLY_BACKUP_ENCRYPTION_KEY` 是外部加密流程占位符，不由当前 API 进程消费。

## 附件备份

- `CloudAttachments` 表只保存元数据，不保存二进制正文。
- 附件下载必须走 API 鉴权，不允许公开直链。
- 使用 OSS 时，必须单独确认 `attachments/` prefix 的版本控制、生命周期和跨区域复制策略。
- 使用本地 fallback 时，必须备份 `/opt/orderly/data/object-storage`，并和 PostgreSQL dump 保持时间点接近。

## 恢复演练

1. 选取最新 `.dump`。
2. 恢复到临时数据库，不能覆盖生产库。
3. 执行基础查询：`CloudUsers`、`CloudWorkspaces`、`CloudAuditLogs`、`CloudAttachments`。
4. 验证完成后删除临时数据库。
5. 记录演练时间、备份文件、结果和错误。

当前服务端已有恢复演练服务，会把结果反映到 `/health/backups`。

## 正式恢复

1. 冻结写入入口。
2. 确认恢复目标时间点。
3. 备份当前故障库，保留现场。
4. 在新库或已确认可覆盖的目标库恢复 `.dump`。
5. 恢复附件文件或确认 OSS 对象仍完整。
6. 启动 API，检查 `/health`、`/health/db`、`/health/backups`。
7. 做登录、同步、附件下载、审计查询 smoke。

## 告警占位

T10 前必须接入至少一种告警：

- `/health/backups` 非 Healthy。
- 最近 24 小时无新备份。
- OSS 上传失败。
- 恢复演练失败或过期。
- 本地备份目录空间不足。
