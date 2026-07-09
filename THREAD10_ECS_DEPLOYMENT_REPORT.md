# Thread 10 ECS Deployment Report

结论时间：2026-07-09 12:41:08 +08:00  
T10-B adoption plan 更新时间：2026-07-09 23:31:44 +08:00  
T10-C1 pre-deployment gate 更新时间：2026-07-10 00:51:43 +08:00

## 1. 总体结论

T10-B：PASS。  
T10-C1：PASS。  
T10 整体：仍未启动应用，未进入 T10-C2，未进入 T11。

blocker = 0，high risk = 0。

本轮已完成 ECS 预部署暂存和 migration safety gate：远端只读 preflight、应用目录创建、compose/Caddy/env 模板上传、远端 compose config、数据库只读审计、DbUp 静态审计、migration 前 PostgreSQL 备份、备份格式验证、镜像暂存、secret runtime 可读性验证均完成。

是否建议授权 T10-C2 启动并执行 migration：建议授权，但仅限启动 `orderly-server` 并执行 DbUp migration，不启动 Caddy，不进入 T11。T10-C2 启动前仍需写入真实 `/opt/orderly/env/orderly.prod.env`，不得使用本轮的 template/staged env 直接对外运行。

## 2. 当前 ECS Baseline 状态

- SSH：`orderlyops@118.178.237.56` 可用。
- 主机：`iZbp129jazy73vlmmvgz3bZ`
- T10-C1 preflight 时间：`2026-07-10T00:42:42+08:00`
- `orderly-postgres`：`postgres:16`，running，`Up 47 hours`，仅 `5432/tcp` 容器端口，无宿主机端口映射。
- `orderly-redis`：`redis:7`，running，`Up 47 hours`，仅 `6379/tcp` 容器端口，无宿主机端口映射。
- `compose_default`：存在，Docker bridge network，旧 PostgreSQL/Redis 均在此网络内。
- `compose_postgres_data`：存在，仍由 `orderly-postgres` 挂载。
- `compose_redis_data`：存在，仍由 `orderly-redis` 挂载。
- 监听端口复核：未发现宿主机监听 `5432`、`6379`、`8080`、`80`、`443`。
- 磁盘：`/opt/orderly` 所在盘约 40G，总可用约 29G。

旧容器、旧 volumes 未停止、未重启、未重建、未改名。

## 3. Secret 引用验证

只检查文件元信息，未读取或输出内容：

- `/opt/orderly/env/postgres_password`：`-rw------- orderlyops orderlyops 64`，SHA256 `71233ff8f8a5002979327f5ae5b544053147b3f5ea3caacf817d0701ee4ef727`
- `/opt/orderly/env/redis_password`：`-rw------- orderlyops orderlyops 64`，SHA256 `1c899d4ae761aaa2bfa9d2e561439ffb31d1c0f6fc844586b7cceaaf36e92417`

发现并修复的启动前问题：

- 镜像默认 `app` 用户为 `uid=1654 gid=1654`，无法读取宿主 `1000:1000` 且 `600` 的 existing password 文件。
- 已将 `orderly-server` compose 用户改为 `${ORDERLY_APP_UID:-1000}:${ORDERLY_APP_GID:-1000}`。
- 远端 helper 验证：以 `1000:1000` 运行镜像、覆盖 entrypoint 为 shell，只执行 `test -r /run/secrets/existing_postgres_password`，返回可读且大小为 `64`。未输出 secret 内容，未启动 `Orderly.Server.dll`。

## 4. 当前数据库状态

只读 SQL 审计结果：

- PostgreSQL major：16
- PostgreSQL version：`16.14 (Debian 16.14-1.pgdg13+1)`
- database：`orderly`
- user：`orderly`
- schema：`public`
- 非系统 base table 数：`0`
- DbUp journal：`public.schemaversions` 不存在
- 数据库大小：`7699479` bytes
- 业务数据判断：当前无业务表，无业务数据

## 5. Migration 状态

已执行 migration：无。

待执行 migration：全部 7 个。

| Migration | 主要操作 | ALTER / DROP / DELETE / TRUNCATE | 锁表/数据影响 | 幂等性 | 回滚路径 |
| --- | --- | --- | --- | --- | --- |
| `0001_InitialSchema.sql` | 创建 Cloud/Commerce 初始表和索引 | 无破坏性语句 | 空库创建对象 | `IF NOT EXISTS`，高 | 使用本轮 `.dump` 恢复到迁移前空库 |
| `0002_LoginFailures.sql` | 创建登录失败表和索引 | 无破坏性语句 | 创建新表/索引 | 高 | 使用备份恢复或新增反向 migration |
| `0003_ExportReliability.sql` | 给 `CloudExportJobs` 加导出重试字段 | `ALTER TABLE IF EXISTS ... ADD COLUMN IF NOT EXISTS` | 当前空库下 0001 已含字段，预计无实际变更 | 高 | 使用备份恢复或新增反向 migration |
| `0004_CloudImportReliability.sql` | 给 `CloudImportBatches` 加 `ResultJson` | `ALTER TABLE IF EXISTS ... ADD COLUMN IF NOT EXISTS` | 当前空库下 0001 已含字段，预计无实际变更 | 高 | 使用备份恢复或新增反向 migration |
| `0005_UserApplicationsAndDevices.sql` | 加设备字段，创建邀请/申请/设备表 | `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`，无 DROP/DELETE/TRUNCATE | 空表短锁，创建新表/索引 | 中高 | 使用备份恢复或新增反向 migration |
| `0006_LifecycleAttachmentsAndHistory.sql` | 创建实体版本和附件表 | 无破坏性语句 | 创建新表/索引 | 高 | 使用备份恢复或新增反向 migration |
| `0007_AuditSyncContractHardening.sql` | 加 audit 字段、索引并修补空字段 | 包含 `UPDATE CloudAuditLogs`，无 DROP/DELETE/TRUNCATE | 由于当前无表无数据，0001 创建后 `CloudAuditLogs` 为空，预计 UPDATE 影响 0 行 | 中高 | 使用备份恢复或新增反向 migration |

destructive or unknown migration = 0。

## 6. Migration 前备份

备份命令使用 PostgreSQL custom format：`pg_dump -Fc`。未停止数据库，未修改 schema 或数据。

- server time：`2026-07-10T00:45:35+08:00`
- UTC time：`2026-07-09T16:45:35+00:00`
- file：`/opt/orderly/backups/orderly_pre_migration_t10c1_20260709T164535Z.dump`
- size：`868` bytes
- SHA-256：`bcf009db7a3fee8d5ada9a4d4038860f21dc8b7304af6f5d69d739f4f55b66d3`
- pg_dump：`16.14 (Debian 16.14-1.pgdg13+1)`
- database：`orderly`
- 格式验证：`pg_restore -l` 成功识别 `Format: CUSTOM`，`Dump Version: 1.15-0`

本轮未恢复、未覆盖生产库。

## 7. 应用暂存状态

已创建应用专用目录：

- `/opt/orderly/logs/api`
- `/opt/orderly/logs/caddy`
- `/opt/orderly/data/object-storage`
- `/opt/orderly/data/caddy`
- `/opt/orderly/data/caddy-config`
- `/opt/orderly/staging/t10-c1`
- `/opt/orderly/images`

已上传文件：

- `/opt/orderly/compose/docker-compose.prod.yml`
- `/opt/orderly/compose/Caddyfile`
- `/opt/orderly/staging/t10-c1/orderly.prod.env.template`
- `/opt/orderly/staging/t10-c1/orderly.prod.env.staged`

staged env 只用于 config 验证，不含真实生产 secret；其中镜像指向：

- `ORDERLY_SERVER_IMAGE=orderly-server:t10-c1-20260709T164535Z`

镜像暂存：

- local tar SHA-256：`a5e2b05ceb482f717e1d5d25cae747634b698666d6e216adc33791793efef0f9`
- remote tar：`/opt/orderly/images/orderly-server-t10-c1-20260709T164535Z.tar`
- remote tar SHA-256：`a5e2b05ceb482f717e1d5d25cae747634b698666d6e216adc33791793efef0f9`
- loaded image：`orderly-server:t10-c1-20260709T164535Z`
- image id：`sha256:dbc77b27d60b10abe9c305f0d5fcdf11d68209179590b8627d5a66f1b80bb8c8`

远端 compose 验证：

- `docker compose --env-file /opt/orderly/staging/t10-c1/orderly.prod.env.staged -f /opt/orderly/compose/docker-compose.prod.yml config --quiet`：PASS
- 默认 services：`orderly-server`
- edge services：`orderly-server,caddy`
- images：`orderly-server:t10-c1-20260709T164535Z`；edge profile 另含 `caddy:2-alpine`

本轮结束时：

- `orderly-server` 未运行
- `caddy` 未运行
- `orderly-app` project 下无容器

## 8. 启动 orderly-server 的精确影响

如果 T10-C2 授权启动 `orderly-server`：

1. Docker Compose 会在 `orderly-app` project 下创建并启动 API 容器。
2. API 容器会以 `1000:1000` 运行，通过 `compose_default` 访问现有 `orderly-postgres`。
3. API 容器会读取 `/run/secrets/existing_postgres_password`，该 secret 来源为 `/opt/orderly/env/postgres_password`。
4. `Orderly.Server` 启动时会运行 DbUp。
5. 当前 `schemaversions` 不存在，因此 DbUp 会执行 0001-0007。
6. 执行结果会创建 Cloud/Commerce schema、DbUp journal、索引，并执行 `0007` 中两条 UPDATE；由于当前库无表无业务数据，预计 UPDATE 影响 0 行。
7. 不会启动 Caddy，除非显式使用 `--profile edge`。
8. 不会重建 `orderly-postgres`、`orderly-redis` 或旧 volumes，因为新 compose 不声明这些服务和 volumes。

## 9. 出错时停止与恢复方案

如果 API 启动失败或 migration 失败：

- 立即停止 T10-C2。
- 只允许停止/删除 `orderly-app` project 下的新应用容器。
- 禁止停止、重启、重建 `orderly-postgres` / `orderly-redis`。
- 禁止删除、重建 `compose_postgres_data` / `compose_redis_data`。
- 禁止 `docker compose down -v`。
- 保留 API 日志、DbUp 错误、镜像 tag 和备份文件。
- 如需恢复数据库，只能基于 `/opt/orderly/backups/orderly_pre_migration_t10c1_20260709T164535Z.dump`，并由人类确认恢复目标库；不得在本轮或 T10-C2 自动覆盖生产库。

## 10. T10-C1 PASS / FAIL

PASS

判定：

- blocker = 0
- high risk = 0
- destructive or unknown migration = 0
- 备份成功且格式验证通过
- secret 未泄漏
- compose config 通过
- 旧 PostgreSQL / Redis / volumes 未被改变
- rollback / restore 路径明确

允许建议进入 T10-C2：是，仅限启动 `orderly-server` 并执行 migration。  
不允许进入 T11。
