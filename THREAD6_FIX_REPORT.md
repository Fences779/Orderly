# Thread 6 Fix Report

结论时间：2026-07-09

## 修复的 Blocker 编号

- B1：补齐 `docs/cloud-sync-*.md` 协议入口与 `THREAD1_HANDOFF.md` - `THREAD4_HANDOFF.md` 基线文件。
- B2：写入契约补齐 `Version / BaseVersion / ChangedFields / IdempotencyKey` 兼容字段。
- B3：Commerce 更新命令补字段级冲突判断；同字段冲突继续 409，不同字段允许合并。
- B4：`CloudAuditLogs`、DTO、审计服务补齐 `DeviceId / Result / CorrelationId`。
- B5：用户、邀请、审批、设备、导出、应急草稿、导入 dry-run 等写入口补幂等或重复提交去重。
- B6：订单、客户、任务写入补显式角色校验；成本字段写入限制为成本权限；reset-password 入口统一到 `CanManageUsers`。
- B7：历史版本、附件列表、附件下载、附件上传/归档增加服务端二次权限校验和访问审计。
- B8：非 Development 环境 JWT key 缺失或过短时启动失败；Development 才允许固定开发 key 并输出警告。

## 修复的 High Risk 编号

- H1：Bootstrap admin 不再使用固定 `OrderlyAdmin@123`，必须显式配置 `ORDERLY_BOOTSTRAP_ADMIN_PASSWORD`。
- H2：Snapshot token 改为 HMAC 签名格式，篡改 payload 会被拒绝。
- H3：确认当前仓库正式迁移路线为 DbUp + SQL migration，不引入第二套 EF migration；空 PostgreSQL 迁移按 DbUp 验证。
- H4：DbUp runner 改为逐脚本事务；脚本内手写 `BEGIN/COMMIT` 壳已移除。
- H5：库存 DTO mapper 必须传入成本权限，默认不再无条件返回 `UnitCost`。
- H6：已补 build、定向 test、空库 migration 验证。

## 修改文件清单

- `README.md`
- `THREAD1_HANDOFF.md`
- `THREAD2_HANDOFF.md`
- `THREAD3_HANDOFF.md`
- `THREAD4_HANDOFF.md`
- `THREAD6_FIX_REPORT.md`
- `docs/cloud-sync-v1.md`
- `docs/cloud-sync-domain-model.md`
- `docs/cloud-sync-sync-contract.md`
- `docs/cloud-sync-schema.md`
- `docs/cloud-sync-freeze-checklist.md`
- `deploy/orderly.env.example`
- `src/Orderly.Contracts/Auth/*.cs`
- `src/Orderly.Contracts/Commerce/*.cs`
- `src/Orderly.Contracts/Offline/CloudOutboxEntryDto.cs`
- `src/Orderly.Contracts/Sync/ChangeLogEntryDto.cs`
- `src/Orderly.Server/Controllers/*.cs`
- `src/Orderly.Server/Data/MigrationRunner.cs`
- `src/Orderly.Server/Mapping/CommerceDtoMapper.cs`
- `src/Orderly.Server/Migrations/*.sql`
- `src/Orderly.Server/Models/ServerOptions.cs`
- `src/Orderly.Server/Program.cs`
- `src/Orderly.Server/Services/*.cs`

## 未修复问题及原因

- Medium / Low Risk 未处理，按 Thread 6 范围禁止处理。
- EF Core migration 未引入。原因：仓库当前正式实现是 DbUp + embedded SQL；临时加入 EF 会形成第二套迁移系统。Thread 6 已用空 PostgreSQL 验证 DbUp migration。
- 全量 `dotnet test Orderly.sln --no-build --nologo` 被中断。原因：运行超过 5 分钟无新增输出，疑似卡在非本次改动相关测试；已改跑服务器和 Cloud 定向测试。

## build 结果

- `dotnet build Orderly.sln --nologo`
- 结果：通过，0 warning，0 error。

## test 结果

- `dotnet test tests/Orderly.Tests/Orderly.Tests.csproj --no-build --nologo --filter "FullyQualifiedName~Server"`
- 结果：通过，25/25。

- `dotnet test tests/Orderly.Tests/Orderly.Tests.csproj --no-build --nologo --filter "FullyQualifiedName~Cloud"`
- 结果：通过，19/19。

- `dotnet test Orderly.sln --no-build --nologo`
- 结果：超过 5 分钟无新增输出后中断，未作为通过结果。

## migration 验证结果

- 启动临时 `postgres:16-alpine` 空库容器。
- 使用 `dotnet run --project src/Orderly.Server/Orderly.Server.csproj --no-build --no-launch-profile` 触发真实服务端 DbUp migration。
- 结果：`0001` - `0007` 全部执行，`Upgrade successful`，服务端成功监听 `http://127.0.0.1:18081`。
- 临时容器已删除。

## 是否允许进入本地一体化测试

允许进入本地一体化测试。

