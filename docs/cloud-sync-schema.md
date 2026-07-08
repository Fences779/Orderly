# Cloud Sync v1 Schema Baseline

来源：`docs/Cloud Sync v1.txt`、`src/Orderly.Server/Migrations/*.sql`。

验收口径：

- 本仓库 Cloud Sync v1 当前采用 DbUp + PostgreSQL SQL migration。
- 迁移脚本必须可应用到空 PostgreSQL。
- 迁移执行必须使用事务保护，避免失败后留下半套 schema。
- 审计表至少覆盖 `Actor / ActorRole / DeviceId / TargetType / TargetId / Action / Before / After / Reason / Timestamp / IP / Result / CorrelationId`。
- 幂等状态必须持久化在云端数据库，不允许使用 fake、in-memory 或硬编码结果。
