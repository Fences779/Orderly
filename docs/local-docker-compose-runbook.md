# Orderly Cloud Sync v1 Local Docker Compose Runbook

本 runbook 只用于本地 Docker Compose 验收，不用于 ECS、生产域名、HTTPS 证书、生产数据库或真实对象存储桶。

## 前置条件

- Docker Desktop 已启动。
- .NET 8 SDK 可用。
- 当前目录：`D:\Dev\Orderly`。
- 不要把本地 `.env` 或 `.env.t8-*` 提交到 Git。

## 一键验收

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-local-docker-compose-smoke.ps1
```

脚本会使用专用 Compose 项目名 `orderly-cloud-t8`，不会删除默认 `orderly-cloud` 项目的 volume。

脚本会自动执行：

- `dotnet build Orderly.sln --nologo`
- `docker compose --env-file .env.t8-empty config --quiet`
- `docker compose --env-file .env.t8-empty build orderly-server`
- 删除 `orderly-cloud-t8` 测试 volume，启动空库 PostgreSQL + API。
- 验证空库 DbUp migration 后不创建真实用户或工作区。
- 删除空库测试 volume，重新启动带一次性本地 bootstrap secret 的 Compose 环境。
- 运行 `ComposeSmokeTests`，覆盖 API、权限、设备、Sync A/B/C、冲突、幂等、Cursor、SignalR、附件、审计、永久删除。
- 重启 API 容器并验证 health。
- `docker compose down / up` 后验证 PostgreSQL volume 保留。
- 再跑一次 `ComposeSmokeTests`，确认 smoke 可重复执行。

## 手动本地启动

复制本地模板：

```powershell
Copy-Item .\.env.example .\.env
```

替换 `.env` 中的本地随机值后启动：

```powershell
docker compose --env-file .env config --quiet
docker compose --env-file .env build orderly-server
docker compose --env-file .env up -d --no-build postgres orderly-server
```

本地访问：

```powershell
Invoke-RestMethod http://127.0.0.1:18082/health
Invoke-RestMethod http://127.0.0.1:18082/health/db
```

查看状态：

```powershell
docker compose --env-file .env ps
docker compose --env-file .env logs -f orderly-server
```

停止但保留数据 volume：

```powershell
docker compose --env-file .env down
```

清空本地测试数据 volume：

```powershell
docker compose --env-file .env down --volumes --remove-orphans
```

## 本地附件存储

本地 Compose 默认使用 `ORDERLY_LOCAL_BLOB_DIR=/opt/orderly/blobs` 和 `orderly-blobs` volume 验证附件闭环。

这只用于本地容器验收，不连接生产 OSS，不写入真实对象存储桶。未配置 `ORDERLY_LOCAL_BLOB_DIR` 且未配置 OSS 时，附件上传会保持禁用。

## 边界

- 不部署 ECS。
- 不配置真实域名。
- 不申请 HTTPS。
- 不连接生产数据库。
- 不连接生产对象存储桶。
- 不提交真实 secret。
- 不修改 Cloud Sync v1 协议。
