# Orderly ECS Deployment Preparation

本目录只放 ECS 部署前准备材料。不要在本阶段登录生产服务器改动，不要连接生产库，不要配置真实域名、DNS、HTTPS、证书或真实 secret。

## 目标目录

ECS 上按以下目录规划：

- `/opt/orderly/compose`：`docker-compose.prod.yml`、`Caddyfile`
- `/opt/orderly/env`：`orderly.prod.env`，只在服务器上保存真实 secret
- `/opt/orderly/logs`：Docker、Caddy、API 日志落点
- `/opt/orderly/backups`：PostgreSQL `.dump` 与迁移前备份
- `/opt/orderly/data/postgres`：PostgreSQL 数据目录
- `/opt/orderly/data/object-storage`：仅作本地附件 fallback；生产优先使用 OSS

## 文件

- `docker-compose.prod.yml`：生产 Compose 模板。PostgreSQL 不暴露公网端口，API 不暴露公网端口，只通过 reverse proxy 对外。
- `.env.prod.example`：生产环境变量模板，只含占位符。
- `Caddyfile.example`：Caddy 生产代理模板，域名和 HTTPS 只保留占位符。
- `SECRETS_CHECKLIST.md`：上线前 secret 与环境变量检查表。
- `MIGRATION_RUNBOOK.md`：首次空库和后续升级的 migration 操作策略。
- `BACKUP_RESTORE_RUNBOOK.md`：备份、异地备份、附件备份和恢复演练策略。
- `T10_DEPLOYMENT_RUNBOOK.md`：下一阶段人工部署步骤，不会自动执行。

## 预检

在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ecs-preflight.ps1
```

预检只读取本地模板和脚本，不连接生产服务器，不连接生产数据库。

## 边界

- 不写入真实 secret。
- 不启动生产服务。
- 不 SSH 到 ECS。
- 不申请或配置真实 HTTPS 证书。
- 不修改 Cloud Sync v1 协议。
- 不弱化权限、设备、审计、冲突、幂等、Cursor、附件授权、永久删除规则。
