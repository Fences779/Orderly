# Orderly Cloud Sync v1 Local Integration Runbook

本 runbook 只用于本地开发和测试环境，不用于 ECS、生产域名、HTTPS 证书或生产数据库。

## 前置条件

- Windows PowerShell 7+
- .NET 8 SDK
- Docker Desktop 已启动
- 当前目录：`D:\Dev\Orderly`

## 一键 smoke

```powershell
.\scripts\run-local-integration-smoke.ps1
```

脚本会检查 Docker daemon，执行 `dotnet build Orderly.sln --nologo`，然后运行：

```powershell
dotnet test tests/Orderly.Tests/Orderly.Tests.csproj --no-build --nologo --filter "FullyQualifiedName~LocalIntegrationSmokeTests"
```

该测试组覆盖用户申请与审批、设备批准/拒绝/撤销、权限快照、越权拒绝、审计、Sync A/B/C、字段冲突、幂等、Cursor、SignalR 通知边界、附件授权、永久删除、Admin/Ops health/backup/sync endpoints。

## 空库 migration 验证

本仓库 Cloud Sync v1 正式迁移路线是 DbUp + SQL migration，不引入第二套 EF Core migration。

本地空库验证流程：

```powershell
docker run --rm -d --name orderly-t7-postgres `
  -e POSTGRES_DB=orderly_t7 `
  -e POSTGRES_USER=orderly `
  -e POSTGRES_PASSWORD=orderly_t7_pw `
  -p 15439:5432 postgres:16-alpine
```

启动服务端触发 DbUp：

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
$env:ASPNETCORE_URLS="http://127.0.0.1:18081"
$env:ORDERLY_POSTGRES_HOST="127.0.0.1"
$env:ORDERLY_POSTGRES_PORT="15439"
$env:ORDERLY_POSTGRES_DB="orderly_t7"
$env:ORDERLY_POSTGRES_USER="orderly"
$env:ORDERLY_POSTGRES_PASSWORD="orderly_t7_pw"
$env:ORDERLY_JWT_SIGNING_KEY="ORDERLY_LOCAL_T7_JWT_SIGNING_KEY_32B"
$env:ORDERLY_BOOTSTRAP_ADMIN_TOKEN=""
$env:ORDERLY_REQUIRE_PRE_MIGRATION_BACKUP="false"
$env:ORDERLY_REQUIRE_PRE_IMPORT_BACKUP="false"
$env:ORDERLY_RESTORE_DRILL_ENABLED="false"
dotnet run --project src/Orderly.Server/Orderly.Server.csproj --no-build --no-launch-profile
```

另开 PowerShell 验证：

```powershell
Invoke-RestMethod http://127.0.0.1:18081/health
Invoke-RestMethod http://127.0.0.1:18081/health/db
Invoke-WebRequest http://127.0.0.1:18081/hubs/workspace/negotiate?negotiateVersion=1
```

最后清理：

```powershell
docker stop orderly-t7-postgres
```

## Docker Compose 本地方式

`docker-compose.yml` 已提供 PostgreSQL、Orderly Server、Caddy 结构。复制 `deploy/orderly.env.example` 为本地 `.env` 后，填入本地强随机值再启动：

```powershell
docker compose --env-file .env config
docker compose --env-file .env up --build
```

本地开发不使用生产域名、生产数据库、真实生产对象存储桶或真实业务数据。
