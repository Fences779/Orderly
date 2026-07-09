# Thread 8 Docker Compose Report

结论时间：2026-07-09

## 1. 总体结论

PASS

blocker = 0，high risk = 0。

允许进入 ECS 部署准备：是，仅限准备。  
允许进入 ECS 实际部署：否。T8 只完成本地 Docker Compose 验收，没有配置生产域名、HTTPS、ECS、安全组或生产 secret。

## 2. 修改文件清单

- `.env.example`
- `.gitignore`
- `Dockerfile`
- `docker-compose.yml`
- `src/Orderly.Server/Models/ServerOptions.cs`
- `src/Orderly.Server/Program.cs`
- `src/Orderly.Server/Services/LocalFileBlobStorage.cs`
- `tests/Orderly.Tests/Server/Integration/ComposeSmokeTests.cs`
- `scripts/run-local-docker-compose-smoke.ps1`
- `docs/local-docker-compose-runbook.md`
- `README.md`
- `THREAD8_DOCKER_COMPOSE_REPORT.md`

Checkpoint commits:

- `a3e9a51 T8 local compose baseline`
- `fb49e38 T8 compose smoke harness`

## 3. docker compose config 结果

命令：

```powershell
docker compose --env-file .env.t8-empty config --quiet
```

结果：通过。

说明：`.env.t8-empty` 为脚本生成的本地临时文件，未提交，不包含生产 secret。

## 4. compose up 结果

命令：

```powershell
docker compose --env-file .env.t8-empty build orderly-server
docker compose --env-file .env.t8-smoke up -d --no-build postgres orderly-server
docker compose --env-file .env.t8-smoke ps
```

结果：通过。

最终状态：

- `orderly-cloud-t8-postgres-1`：healthy，`127.0.0.1:15442 -> 5432`
- `orderly-cloud-t8-orderly-server-1`：healthy，`127.0.0.1:18082 -> 8080`
- `/health`：Healthy
- `/health/db`：Healthy

## 5. 空库 migration 结果

命令：

```powershell
docker compose --env-file .env.t8-empty down --volumes --remove-orphans
docker compose --env-file .env.t8-empty up -d --no-build postgres orderly-server
```

结果：通过。

验证：

- DbUp migration `0001` - `0007` 已应用。
- 空库未启用 bootstrap 时：`CloudUsers=0`，`CloudWorkspaces=0`。
- smoke 库最终 `schemaversions=7`。
- seed 未创建真实管理员账号。

## 6. compose 下 API smoke test 结果

命令：

```powershell
dotnet test tests/Orderly.Tests/Orderly.Tests.csproj --no-build --nologo --filter "FullyQualifiedName~ComposeSmokeTests"
```

结果：通过，重复执行 2 次均通过。

覆盖：

- API health / DB health / version。
- SignalR negotiate 未带 token 返回 401。
- 本地一次性 bootstrap admin 登录。
- 用户申请、管理员审批、员工登录。
- 设备注册、待审批、审批、拒绝、撤销。
- Workspace / Membership / PermissionSnapshot。
- 普通用户越权访问 Admin、用户列表、现金流摘要、跨 workspace sync、商品写入失败。
- 被撤销设备 token 访问失败。

## 7. compose 下 Sync A/B/C smoke test 结果

结果：通过。

覆盖：

- A 创建商品，B 增量拉取可见。
- B 离线期间 A 连续修改，B 使用 server sequence cursor 补齐。
- A/B 同字段并发修改返回 409。
- A/B 不同字段修改字段级合并成功。
- 同一 IdempotencyKey 重复请求只写一次。
- Cursor 使用 `Sequence > afterSequence`。
- SignalR 只通知实体、序号、版本、动作，不传主数据。
- 未授权 workspace pull 返回 Forbidden。

## 8. 权限 / 设备 / 审计 / 附件 / 永久删除验证结果

结果：通过。

数据库复核：

- `CloudUsers=3`
- `CloudWorkspaceMembers=3`
- `CloudDevices=7`
- `CommerceProducts=12`
- `CommerceCustomers=2`
- `CloudAttachments=2`
- `CloudIdempotencyKeys=42`
- `CloudAuditLogs=75`

审计动作包含：

- `UserApplicationSubmitted`
- `UserApplicationApproved`
- `WorkspaceMemberAuthorized`
- `DeviceApprovalRequired`
- `DeviceApproved`
- `DeviceRejected`
- `DeviceRevoked`
- `LoginFailed`
- `AttachmentUploaded`
- `AttachmentDownloaded`
- `Archived`
- `PermanentlyDeleted`
- `EntityHistoryViewed`
- `Recovered`

附件验证：

- 本地 Compose 使用 `ORDERLY_LOCAL_BLOB_DIR=/opt/orderly/blobs`，不连接生产 OSS。
- 附件响应不返回 `BlobKey` 或公开 URL。
- 未授权用户不能列表或下载附件。
- 下载写审计。
- `CloudAttachments` 无 `bytea` 字段。

永久删除验证：

- 普通用户永久删除返回 Forbidden。
- 管理员未满足保留期返回 BadRequest。
- 管理员满足保留期 + Confirm 后返回 NoContent。
- 永久删除写审计。

## 9. 重启恢复验证结果

结果：通过。

覆盖：

- `docker compose restart orderly-server` 后 `/health` 恢复 Healthy。
- `docker compose down` 后再 `up -d`，PostgreSQL volume 保留。
- down/up 后再次执行 `ComposeSmokeTests` 通过。
- 日志可看到真实 HTTP 请求、403/400/204/200 等关键结果。

## 10. 剩余 blocker

0

## 11. 剩余 high risk

0

## 12. 是否允许进入 ECS 部署准备

是，仅限准备。

允许准备内容包括：ECS runbook、环境变量清单、secret 注入方式、安全组清单、域名/HTTPS 操作步骤、回滚步骤。

## 13. 是否允许进入 ECS 实际部署

否。

原因：T8 明确只做本地 Docker Compose 验收，尚未完成 ECS 部署准备评审、生产 secret 注入、域名、HTTPS、安全组和生产数据库连接验证。
