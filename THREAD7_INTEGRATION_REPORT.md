# Thread 7 Integration Report

结论时间：2026-07-09

## 1. 总体结论

PASS

blocker = 0，high risk = 0。

允许进入 Docker Compose 验收：是。  
允许进入 ECS 部署准备：是，仅限准备，不允许直接部署生产。

## 2. T6 遗留问题确认

- 已读取 `THREAD6_FIX_REPORT.md`。
- T6 明确修复 B1-B8、H1-H6。
- T6 明确结论：允许进入本地一体化测试。
- T7 未发现 T6 blocker 继续遗留。

## 3. 修改文件清单

- `src/Orderly.Server/Controllers/AdminController.cs`
- `src/Orderly.Server/Controllers/UsersController.cs`
- `src/Orderly.Server/Services/CloudAuthService.cs`
- `src/Orderly.Server/Services/ICloudAuthService.cs`
- `tests/Orderly.Tests/Server/Integration/ServerWebApplicationFactory.cs`
- `tests/Orderly.Tests/Server/Integration/LocalIntegrationSmokeTests.cs`
- `scripts/run-local-integration-smoke.ps1`
- `docs/local-integration-runbook.md`
- `README.md`
- `THREAD7_INTEGRATION_REPORT.md`

## 4. build 结果

命令：

```powershell
dotnet build Orderly.sln --nologo
```

结果：通过，0 warning，0 error。

## 5. test 结果

命令：

```powershell
dotnet test Orderly.sln --no-build --nologo
```

结果：通过，757/757。

T7 smoke 入口：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-local-integration-smoke.ps1
```

结果：通过，build 0 warning / 0 error，LocalIntegrationSmokeTests 4/4。

## 6. migration 空库验证结果

验证方式：临时 `postgres:16-alpine` 空库 + `dotnet run --project src/Orderly.Server/Orderly.Server.csproj --no-build --no-launch-profile`。

结果：

- DbUp migration `0001` - `0007` 全部执行。
- `Upgrade successful`。
- `/health`：Healthy。
- `/health/db`：Healthy。
- `/health/version`：`0.2.0-cloud-preview`。
- `/health/backups`：可访问，状态 `NoLocalBackup`。
- `/hubs/workspace/negotiate` 未带 token 返回 401，说明 SignalR Hub 已挂载且强制鉴权。
- 空库未启用 bootstrap 时：`CloudUsers=0`，`CloudWorkspaces=0`。
- 临时容器和本地服务已清理。

额外 bootstrap 验证：

- 使用临时空库和一次性 `ORDERLY_BOOTSTRAP_ADMIN_TOKEN` / `ORDERLY_BOOTSTRAP_ADMIN_PASSWORD`。
- migration 7 条通过。
- bootstrap 后：`CloudUsers=1`，`CloudWorkspaces=1`，`CloudWorkspaceMembers=1`，`CloudDevices=1`。
- `admin` 可登录，`/api/auth/me` 返回 `Admin / 运营负责人` 权限快照。
- 临时容器和本地服务已清理。

## 7. API smoke test 结果

结果：通过。

覆盖：

- 邀请码创建。
- 用户申请。
- 云端管理员审批用户。
- 用户登录激活账号。
- 首台设备注册。
- 新设备 Pending。
- 设备批准、拒绝、撤销。
- Workspace 初始化和成员授权。
- 权限快照下发。
- 普通用户无法越权读取用户列表、现金流摘要、跨 workspace sync。
- 普通用户无法越权写入商品。
- 被撤销设备无法继续访问。
- 关键写入有 audit log。

## 8. Sync A/B/C smoke test 结果

结果：通过。

覆盖：

- A 创建实体，B 按增量拉取可见。
- B 离线期间 A 连续修改，B 重连后按 server sequence Cursor 补齐。
- A/B 同时修改同一字段，后提交方 409。
- A/B 修改不同字段，服务端字段级合并成功。
- 同一 IdempotencyKey 重复提交只写入一次，重复请求返回原结果。
- Cursor 使用 `Sequence > afterSequence`，不依赖客户端时间，不使用 offset。
- SignalR 只通知 entity/sequence/revision/action，不传主数据。
- 未授权 workspace 的变更无法 pull。

## 9. 权限 / 设备 / 审计 / 附件 / 永久删除验证结果

结果：通过。

覆盖：

- 用户审批写审计。
- 设备审批、拒绝、撤销写审计。
- 成员授权写 `WorkspaceMemberAuthorized` 审计。
- 管理员查看历史版本写 `EntityHistoryViewed` 审计。
- 管理员恢复归档数据写 `Recovered` 审计。
- 附件二进制不进入 PostgreSQL bytea 字段。
- 附件只返回 metadata/hash/version/owner 信息，不返回 BlobKey 或公开 URL。
- 未授权用户无法列表或下载附件。
- 附件下载写 `AttachmentDownloaded` 审计。
- 普通用户可归档自己创建的客户。
- 普通用户不能永久删除。
- 管理员永久删除必须满足保留期 + Confirm + 审计。
- Admin health / backups / sync-issues / audit-logs 可读取。
- 普通用户不能访问 Admin/Ops endpoint。
- backup / restore 结构不影响主业务创建。

## 10. 剩余 blocker

0

## 11. 剩余 high risk

0

## 12. 是否允许进入 Docker Compose 验收

是。

已验证：

```powershell
docker compose --env-file deploy/orderly.env.example config
```

结果：通过。

## 13. 是否允许进入 ECS 部署准备

是，仅允许进入部署准备，不允许直接连生产库、申请生产 HTTPS、配置生产域名或部署 ECS。

## 14. 下一步建议

1. 进入 Docker Compose 验收，使用本地 `.env` 和临时本地数据卷。
2. Docker Compose 验收通过后，再准备 ECS 部署材料。
3. ECS 准备阶段只做配置、runbook、安全组和密钥清单，不连接生产数据库。
