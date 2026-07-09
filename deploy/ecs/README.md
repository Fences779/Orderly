# Orderly ECS Deployment Preparation

本目录放 ECS 部署材料。T10-B 后的策略是“应用适配现有 ECS baseline”，不是让 ECS 强行适配仓库旧模板。

## 现有 baseline

必须保留：

- `/opt/orderly`
- `orderly-postgres`
- `orderly-redis`
- `compose_postgres_data`
- `compose_redis_data`
- `compose_default`

新应用 compose 不创建、不删除、不重建、不改名这些资源。

## 目标目录

ECS 上按以下目录使用：

- `/opt/orderly/compose`：`docker-compose.prod.yml`、`Caddyfile`
- `/opt/orderly/env`：`orderly.prod.env`、现有 `postgres_password`、现有 `redis_password`
- `/opt/orderly/logs/api`：API 日志挂载点
- `/opt/orderly/logs/caddy`：Caddy 日志挂载点
- `/opt/orderly/backups`：PostgreSQL `.dump` 与迁移前备份
- `/opt/orderly/exports`：导出文件目录
- `/opt/orderly/data/object-storage`：仅作本地附件 fallback；生产优先使用 OSS
- `/opt/orderly/data/caddy`、`/opt/orderly/data/caddy-config`：Caddy 状态目录

PostgreSQL 数据仍在 `compose_postgres_data`，Redis 数据仍在 `compose_redis_data`。不要迁移、清空或重建。

## 文件

- `docker-compose.prod.yml`：生产应用 Compose 模板。只包含 `orderly-server` 和可选 `caddy`，通过 external network `compose_default` 访问现有 PostgreSQL/Redis baseline。
- `.env.prod.example`：生产环境变量模板，只含占位符和既有资源路径。
- `Caddyfile.example`：Caddy 生产代理模板，域名和 HTTPS 只保留占位符。
- `SECRETS_CHECKLIST.md`：上线前 secret 与环境变量检查表。
- `MIGRATION_RUNBOOK.md`：基于现有 PostgreSQL 的 DbUp migration 操作策略。
- `BACKUP_RESTORE_RUNBOOK.md`：备份、异地备份、附件备份和恢复演练策略。
- `T10_DEPLOYMENT_RUNBOOK.md`：T10-C 执行顺序，不会自动执行。

## 预检

在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ecs-preflight.ps1
```

预检只读取本地模板和脚本，不连接生产服务器，不连接生产数据库。

## 边界

- 不写入真实 secret。
- 不启动生产服务。
- 不 SSH 写 ECS。
- 不申请或配置真实 HTTPS 证书。
- 不修改现有 `orderly-postgres`、`orderly-redis`、`compose_postgres_data`、`compose_redis_data`。
- 不执行 `docker compose down -v`。
- 不修改 Cloud Sync v1 协议。
- 不弱化权限、设备、审计、冲突、幂等、Cursor、附件授权、永久删除规则。
