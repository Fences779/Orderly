# Thread 9 ECS Prep Report

结论时间：2026-07-09

## 1. 总体结论

PASS

blocker = 0，high risk = 0。

允许进入 ECS 实际部署：是，仅限进入 T10 人工部署流程。T9 未部署 ECS，未 SSH 登录服务器，未连接生产数据库，未配置真实域名 / HTTPS / DNS / 证书，未写入真实 secret。

## 2. 修改文件清单

- `deploy/ecs/docker-compose.prod.yml`
- `deploy/ecs/.env.prod.example`
- `deploy/ecs/Caddyfile.example`
- `deploy/ecs/README.md`
- `deploy/ecs/SECRETS_CHECKLIST.md`
- `deploy/ecs/MIGRATION_RUNBOOK.md`
- `deploy/ecs/BACKUP_RESTORE_RUNBOOK.md`
- `deploy/ecs/T10_DEPLOYMENT_RUNBOOK.md`
- `scripts/ecs-preflight.ps1`
- `README.md`
- `THREAD9_ECS_PREP_REPORT.md`

## 3. 生产 compose 模板检查结果

通过。

- PostgreSQL 不暴露公网端口，只在 Compose 网络内 `expose 5432`。
- Redis 当前仓库无实际实现，生产模板未新增 Redis。
- API 不暴露公网端口，只 `expose 8080` 给 reverse proxy。
- Caddy 通过 `edge` profile 暴露 80 / 443，避免 API 直连公网。
- `postgres`、`orderly-server`、`caddy` 均有 healthcheck。
- 使用 `/opt/orderly/data/postgres`、`/opt/orderly/backups`、`/opt/orderly/logs`、`/opt/orderly/data/object-storage`。
- Docker log policy 已设置 `max-size=20m`、`max-file=10`。
- `ORDERLY_REQUIRE_PRE_MIGRATION_BACKUP=true`，migration 由 API 启动时 DbUp 执行，失败则服务不进入可写状态。

## 4. env / secret 模板检查结果

通过。

- `.env.prod.example` 只包含占位符和 `example.invalid`，无真实 secret。
- Postgres、JWT、Bootstrap Admin、OSS、备份保留、迁移前备份、恢复演练等变量已列出。
- 当前服务端没有独立 `ORDERLY_REFRESH_TOKEN_SECRET`；Refresh Token 为随机生成并只保存哈希，已在 `SECRETS_CHECKLIST.md` 明确说明。
- `ORDERLY_BACKUP_ENCRYPTION_KEY` 作为外部备份加密流程占位符记录，不由当前 API 进程消费。
- Bootstrap secret 明确要求首个管理员初始化后清空。

## 5. reverse proxy 模板检查结果

通过。

- `Caddyfile.example` 使用 `{$ORDERLY_DOMAIN}` 和 `{$ORDERLY_TLS_CONTACT_EMAIL}` 占位符。
- 未写入真实域名、证书或 HTTPS 配置。
- `reverse_proxy orderly-server:8080` 保留 SignalR websocket 转发能力。
- 阻断 `/attachments/*`、`/blobs/*`、`/files/*`、`/object-storage/*` 等公开直链路径。
- 管理后台路径保留 VPN / 办公 IP / 上游访问网关注释，应用层 JWT 和管理员权限不弱化。

## 6. migration runbook 检查结果

通过。

- 覆盖首次空库 migration。
- 覆盖后续升级 migration。
- 明确 migration 前备份。
- 明确失败后保留现场、回滚镜像和按备份恢复。
- 明确不重写历史 migration。
- 明确不清空生产库、不删除生产 volume。

## 7. backup / restore runbook 检查结果

通过。

- 覆盖 PostgreSQL 每日备份。
- 明确保留至少 30 天。
- 覆盖 OSS 异地备份占位方案。
- 明确附件元数据在 PostgreSQL，附件文件在 OSS 或本地 object-storage fallback，需要分开备份。
- 覆盖恢复演练步骤。
- 覆盖备份失败、OSS 上传失败、恢复演练失败等告警占位。

## 8. ECS preflight 脚本结果

通过。

命令：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ecs-preflight.ps1
docker compose --env-file deploy/ecs/.env.prod.example -f deploy/ecs/docker-compose.prod.yml config --quiet
```

结果：

- `scripts/ecs-preflight.ps1`：PASS。
- `docker compose config --quiet`：PASS。

检查覆盖：

- Compose 语法。
- 必需 env 是否存在。
- 是否疑似提交真实 secret。
- PostgreSQL / Redis 是否暴露公网端口。
- API 是否绕过 reverse proxy 暴露。
- hardcoded password / token / secret / key。
- 生产危险命令。
- healthcheck。
- backup runbook 说明。

## 9. 剩余 blocker

0

## 10. 剩余 high risk

0

## 11. 是否允许进入 ECS 实际部署

允许。

边界：只允许进入 T10 人工实际部署流程；T10 开始前必须再次人工确认真实 secret、ECS、安全组、DNS、HTTPS、备份和回滚方案。本次 T9 没有执行任何生产部署动作。

## 12. T10 实际部署前必须由人类确认的事项

- 真实 ECS 实例、系统盘 / 数据盘和 Docker 版本。
- 安全组只开放必要端口：80 / 443，SSH 只限管理来源，PostgreSQL 不开放公网。
- 真实域名、DNS 解析和 HTTPS 证书申请窗口。
- 真实 `/opt/orderly/env/orderly.prod.env` 已替换所有 `__SET_*__`。
- Postgres 密码、JWT key、Bootstrap secret、OSS key、备份加密 key 已安全生成和保存。
- OSS bucket 不公开读，附件只能走 API 鉴权下载。
- 首次管理员初始化后清空 Bootstrap secret。
- 生产备份、异地备份、恢复演练和告警已接入。
- 回滚镜像 tag 和升级前数据库 dump 已准备。
- 部署 smoke 负责人和验收窗口已确认。
