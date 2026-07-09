# ECS Migration Runbook

Orderly Cloud Sync v1 当前使用 DbUp + PostgreSQL SQL migration。迁移脚本在 `src/Orderly.Server/Migrations/*.sql`，服务端启动时会执行 DbUp。

## 原则

- 不重写历史 migration。
- 不清空生产库。
- 不跳过失败 migration。
- 不在生产库上手写临时 schema 修复。
- 后续升级必须先备份，再启动会触发 migration 的 API 容器。

## 首次空库

1. 确认 T8 和 T9 均 PASS。
2. 在 ECS 上创建 `/opt/orderly/data/postgres`、`/opt/orderly/backups`、`/opt/orderly/env`、`/opt/orderly/compose`。
3. 写入真实 `/opt/orderly/env/orderly.prod.env`，确认 `ASPNETCORE_ENVIRONMENT=Production`。
4. 只启动 PostgreSQL：

```bash
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml up -d postgres
```

5. 确认 PostgreSQL healthy。
6. 首次启动 API：

```bash
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml up -d orderly-server
```

7. API 启动时执行 DbUp。空库应应用 `0001` 到当前最新脚本。
8. 检查 `/health`、`/health/db`、容器日志和 `schemaversions`。

## 后续升级

1. 确认新镜像 tag、migration diff、回滚镜像 tag。
2. 手工生成一次升级前备份，保存到 `/opt/orderly/backups`。
3. 确认 `ORDERLY_REQUIRE_PRE_MIGRATION_BACKUP=true`，让 API 在发现 migration 需要执行时再次做启动前备份。
4. 更新 `ORDERLY_SERVER_IMAGE`。
5. 启动新 API 容器，由 DbUp 执行新增 migration。
6. 检查 `/health/db`、`/health/backups`、日志和关键 smoke。

## 失败处理

- 如果 migration 失败，API 容器必须保持不可用，不允许带半套 schema 对外服务。
- 保留失败现场：容器日志、migration 名称、备份文件名、镜像 tag。
- 先切回旧镜像并评估 schema 是否已经变更。
- 如需恢复数据库，只能从升级前 `.dump` 恢复，并由人类确认恢复目标库。
- 修复方式必须新增 migration，不允许改写已经发布过的 migration 文件。

## 禁止

- 禁止清空生产库。
- 禁止删除生产 PostgreSQL volume。
- 禁止跳过 DbUp 记录直接改表。
- 禁止使用本地测试 `.env` 连接生产库。
