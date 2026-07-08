# Orderly Cloud Sync v1 - Thread 5 Integration Audit

审计线程：Thread 5.1 Audit Report Writer  
审计对象：Thread 1/2/3/4/5 的当前集成结果  
审计方式：只读代码与文档审计；仅创建本报告文件  
审计结论时间：2026-07-09

## A. 总体结论

结论：FAIL

当前结果不允许进入合并阶段，不允许进入本地一体化测试。可以进入 Thread 6 修复阶段。

主要原因：

- 用户点名的最高协议文件和 Thread 1-4 handoff 文件在仓库中不存在，无法按指定来源完成闭环比对。
- 写入契约与协议核心字段不一致：协议要求 `Version / BaseVersion / ChangedFields / IdempotencyKey`，实现主要使用 `Revision / ExpectedRevision / ClientRequestId`，缺少 `ChangedFields`。
- 冲突处理仍是整行版本拒绝，没有字段级合并。
- 审计日志 schema / DTO 不满足协议最低字段要求。
- 幂等能力只覆盖 Commerce command 主链路，Auth / Users / Lifecycle / Export / Emergency Draft 等写入口没有统一幂等回放。
- 权限边界存在绕过和过宽问题，尤其是写入角色、敏感成本字段、历史版本和附件访问。

本次没有运行 `dotnet build`、`dotnet test`、空 PostgreSQL migration apply。原因：本任务只允许创建或覆盖 `docs/THREAD5_INTEGRATION_AUDIT.md`，这些命令会写入 `bin/obj`、测试临时文件或数据库状态，不属于只读审计。

## 审计来源状态

用户指定的最高协议来源均未找到：

- `docs/cloud-sync-v1.md`
- `docs/cloud-sync-domain-model.md`
- `docs/cloud-sync-sync-contract.md`
- `docs/cloud-sync-schema.md`
- `docs/cloud-sync-freeze-checklist.md`
- `THREAD1_HANDOFF.md`
- `THREAD2_HANDOFF.md`
- `THREAD3_HANDOFF.md`
- `THREAD4_HANDOFF.md`

实际可用参考文件：

- `docs/Cloud Sync v1.txt`
- `docs/ORDERLY_TEAM_CLOUD_AI_IMPLEMENTATION_PLAN.md`
- `docs/ORDERLY_TEAM_CLOUD_OPERATOR_RUNBOOK.md`

## Thread 默认交接状态

- T1：hand off
- T2：hand off
- T3：hand off
- T4：hand off
- T5：hand off
- T6：暂不标记，线程仍在施工中。

## B. Blockers

### B1. 最高协议和 Thread 交接文件缺失

涉及文件：

- 缺失：`docs/cloud-sync-v1.md`
- 缺失：`docs/cloud-sync-domain-model.md`
- 缺失：`docs/cloud-sync-sync-contract.md`
- 缺失：`docs/cloud-sync-schema.md`
- 缺失：`docs/cloud-sync-freeze-checklist.md`
- 缺失：`THREAD1_HANDOFF.md`
- 缺失：`THREAD2_HANDOFF.md`
- 缺失：`THREAD3_HANDOFF.md`
- 缺失：`THREAD4_HANDOFF.md`
- 实际存在：`docs/Cloud Sync v1.txt`

违反的协议点：

- Thread 5 要求按上述最高协议来源和 handoff 文件判断 Thread 1/2/3/4 是否被破坏。

风险：

- 无法证明当前实现没有偏离 Thread 1-4 已冻结边界。
- 无法确认 schema、DTO、migration 是否被后续线程擅自改坏。

推荐修复方向：

- 恢复或补齐用户指定的协议和 handoff 文件。
- 把实际协议文件命名统一为用户指定的 Markdown 文件，或明确建立映射说明。
- Thread 6 修复前先补齐审计基线，否则后续仍无法验收。

是否阻塞合并：是。

### B2. 同步写入契约字段漂移

涉及文件：

- `docs/Cloud Sync v1.txt`
- `src/Orderly.Contracts/Commerce/WriteCommandBase.cs`
- `src/Orderly.Contracts/Commerce/CloudEntityDto.cs`
- `src/Orderly.Contracts/Offline/CloudOutboxEntryDto.cs`
- `src/Orderly.Contracts/Sync/ChangeLogEntryDto.cs`

违反的协议点：

- 协议要求实体至少包含 `Version`，客户端提交修改必须携带 `BaseVersion / ChangedFields / IdempotencyKey`。
- 当前实现使用 `Revision / ExpectedRevision / ClientRequestId`，没有统一的 `Version / BaseVersion / ChangedFields / IdempotencyKey`。

风险：

- Thread 1 的域模型和 Thread 2/3/4 的 API DTO 语义不一致。
- 客户端无法表达“我只改了哪些字段”，后续冲突处理只能按整行版本判断。
- `ClientRequestId` 与协议 `IdempotencyKey` 语义混用，容易导致 API 文档、客户端 Outbox、服务端幂等表三套说法。

推荐修复方向：

- 统一公开契约字段名，至少在 sync contract 中明确 `Revision == Version`、`ExpectedRevision == BaseVersion`、`ClientRequestId == IdempotencyKey` 是否为正式映射。
- 为可更新 DTO 增加 `ChangedFields` 或等价字段补丁结构。
- 服务端、Remote client、Outbox、文档使用同一套字段名。

是否阻塞合并：是。

### B3. 字段级冲突合并未实现

涉及文件：

- `docs/Cloud Sync v1.txt`
- `src/Orderly.Server/Services/CommerceCommandService.cs`
- `src/Orderly.Server/Services/CommerceCommandService.Products.cs`
- `src/Orderly.Server/Services/CommerceCommandService.Orders.cs`
- `src/Orderly.Server/Services/CommerceCommandService.Customers.cs`
- `src/Orderly.Server/Services/CommerceCommandService.InventoryItems.cs`
- `src/Orderly.Server/Services/CommerceCommandService.CashFlow.cs`
- `src/Orderly.Contracts/Commerce/WriteCommandBase.cs`

违反的协议点：

- 同一字段冲突应拒绝。
- 不同字段并发修改允许服务端字段级自动合并。

风险：

- 当前 `ThrowIfRevisionMismatchAsync` 只比较当前 `Revision` 和 `ExpectedRevision`，版本不一致就抛 `409 Conflict`。
- 不同字段并发修改也会被拒绝，无法达到协议里的字段级合并目标。
- 没有 `ChangedFields`，服务端无法可靠判断字段是否重叠。

推荐修复方向：

- 将更新命令改为字段补丁模型，保存 `BaseVersion` 和 `ChangedFields`。
- 服务端在版本不一致时读取 base/current/proposed，判断字段重叠。
- 同字段冲突返回明确 409；不同字段自动合并并生成新版本。

是否阻塞合并：是。

### B4. AuditLog schema / DTO 字段不完整

涉及文件：

- `docs/Cloud Sync v1.txt`
- `src/Orderly.Server/Migrations/0001_InitialSchema.sql`
- `src/Orderly.Server/Services/AuditLogService.cs`
- `src/Orderly.Contracts/Commerce/CloudAuditLogDto.cs`
- `src/Orderly.Server/Controllers/CommerceReadController.cs`
- `src/Orderly.Server/Controllers/AdminController.cs`

违反的协议点：

- 审计至少包含：`Actor / ActorRole / DeviceId / TargetType / TargetId / Action / Before / After / Reason / Timestamp / IP / Result / CorrelationId`。

风险：

- `CloudAuditLogs` 缺少 `DeviceId`、`Result`、`CorrelationId`。
- `CloudAuditLogDto` 还缺少 `IpAddress`、`UserAgent`、`DeviceId`、`Result`、`CorrelationId`。
- 失败、拒绝、冲突等关键事件无法按协议追踪完整责任链。

推荐修复方向：

- migration 补齐 `DeviceId / Result / CorrelationId`。
- `IAuditLogService` 参数和 DTO 同步补齐。
- 中间件或请求上下文统一生成 `CorrelationId`，并从 token/context 带出 `DeviceId`。

是否阻塞合并：是。

### B5. 幂等覆盖不完整

涉及文件：

- `docs/Cloud Sync v1.txt`
- `src/Orderly.Server/Services/IdempotencyService.cs`
- `src/Orderly.Server/Services/CommerceCommandService.cs`
- `src/Orderly.Server/Controllers/AuthController.cs`
- `src/Orderly.Server/Controllers/UsersController.cs`
- `src/Orderly.Server/Controllers/LifecycleController.cs`
- `src/Orderly.Server/Controllers/ExportController.cs`
- `src/Orderly.Server/Controllers/EmergencyDraftController.cs`
- `src/Orderly.Server/Controllers/ImportController.cs`

违反的协议点：

- 所有写入请求必须带 `IdempotencyKey`。
- 重复请求只能执行一次，并返回原处理结果。

风险：

- Commerce command 主链路已有事务内 `CloudIdempotencyKeys` 和原结果回放。
- Auth / Users / Lifecycle / Export / Emergency Draft 等写入口大多只传或记录 `ClientRequestId`，没有进入统一幂等表。
- 附件上传、邀请创建、用户审批、设备审批、导出创建、应急草稿提交等重复请求可能重复创建数据或重复写审计。

推荐修复方向：

- 抽出统一写入幂等装饰层或服务入口。
- 所有 POST / PUT / PATCH / DELETE 写接口统一要求 `IdempotencyKey`。
- 业务写入、审计、change log、幂等结果在同一事务内提交。

是否阻塞合并：是。

### B6. 写入权限和权限绕过存在缺口

涉及文件：

- `docs/Cloud Sync v1.txt`
- `src/Orderly.Server/Controllers/CommerceWriteController.cs`
- `src/Orderly.Server/Controllers/AuthController.cs`
- `src/Orderly.Server/Controllers/UsersController.cs`
- `src/Orderly.Server/Services/CommerceCommandService.Orders.cs`
- `src/Orderly.Server/Services/CommerceCommandService.Customers.cs`
- `src/Orderly.Server/Services/CommerceCommandService.BusinessTasks.cs`
- `src/Orderly.Server/Services/ICloudPermissionService.cs`
- `src/Orderly.Contracts/Commerce/CreateOrderItemCommand.cs`

违反的协议点：

- 每个写入 API 必须校验 user / device / workspace / role / version / idempotency。
- 权限要求包含字段级权限。

风险：

- `CommerceWriteController` 普遍只做 workspace 访问校验，细粒度 role 依赖服务层；订单、客户、任务写入服务没有角色限制。
- `CreateOrderItemCommand.UnitCost` 可由订单创建提交，`CreateOrderAsync` 会据此计算 `Cost/GrossProfit`，普通 workspace 成员可能写入成本相关字段。
- `AuthController` 的 `/api/auth/reset-password` 只校验 `IsAdmin`，而 `UsersController` 使用更严格的 `CanManageUsers`；这形成重复入口和权限口径不一致。

推荐修复方向：

- 每个写入口明确角色要求，不只依赖 workspace membership。
- 成本、利润、现金流等字段只允许 `CanViewCosts/CanManageCashFlow` 对应角色写入。
- 删除或收敛重复的 reset-password 入口，统一走 `CanManageUsers`。

是否阻塞合并：是。

### B7. 历史版本、附件列表和敏感正文访问过宽且缺少访问审计

涉及文件：

- `docs/Cloud Sync v1.txt`
- `src/Orderly.Server/Controllers/LifecycleController.cs`
- `src/Orderly.Server/Services/CloudDataLifecycleService.cs`
- `src/Orderly.Server/Controllers/CommerceReadController.cs`
- `src/Orderly.Server/Controllers/AdminController.cs`

违反的协议点：

- 管理员查看业务正文、附件或用户数据时必须记录访问审计。
- 附件读取必须走云端授权。
- 所有读取、附件下载、历史恢复、设备授权均由云端二次校验。

风险：

- `ListHistoryAsync` 只校验 workspace membership，然后返回 `PayloadJson`；历史快照可能包含订单成本、利润、现金流、客户正文。
- `ListAttachmentsAsync` 只校验 workspace membership，没有细粒度业务权限，也没有访问审计。
- 附件下载有审计，但附件列表、历史版本列表、业务详情读取、管理员敏感查看没有完整访问审计。

推荐修复方向：

- 历史版本和附件列表增加业务权限判断。
- 历史 `PayloadJson` 按角色脱敏，或拆分敏感字段。
- 管理员查看敏感正文、附件、用户数据时统一写 `SensitiveAccessViewed` 类审计。

是否阻塞合并：是。

### B8. JWT 签名 key 缺失时自动降级为固定开发 key

涉及文件：

- `src/Orderly.Server/Program.cs`
- `src/Orderly.Server/Models/ServerOptions.cs`

违反的协议点：

- 云端服务必须保证认证与权限边界可靠，不能在配置缺失时静默降低安全等级。

风险：

- `JwtSigningKey` 缺失或过短时，服务端自动使用固定字符串 `ORDERLY_DEV_ONLY_JWT_SIGNING_KEY_MUST_BE_32B`。
- 生产环境配置错误时不会失败停止，而是使用可预测签名 key。

推荐修复方向：

- 生产模式下 key 缺失或过短必须启动失败。
- 仅允许显式 `Development` 环境使用开发 key，并在日志中标红。
- 部署示例要求强随机 32 字节以上 key。

是否阻塞合并：是。

## C. High Risk

### H1. Bootstrap admin 默认密码硬编码

涉及文件：

- `src/Orderly.Server/Services/CloudAuthService.cs`

违反的协议点：

- 生产云端账号和密码不能有固定默认口令。

风险：

- `EnsureBootstrapAdminAsync` 创建 `admin` 时固定使用 `OrderlyAdmin@123`。
- 即使需要 bootstrap token，也容易在初始化后遗留弱口令。

推荐修复方向：

- bootstrap 时要求一次性传入随机初始密码，或生成只显示一次的随机密码。
- 首次登录强制改密。

是否阻塞合并：是，安全项必须修复或有明确临时豁免。

### H2. Snapshot token 未签名，客户端可伪造

涉及文件：

- `src/Orderly.Server/Services/WorkspaceSyncQueryService.cs`

违反的协议点：

- Cursor / Snapshot token 应由服务端可信生成，客户端不能篡改。

风险：

- token 是 base64 JSON，`DecodeToken` 直接反序列化，没有签名或服务端存储校验。
- 客户端可改 `Sequence / ExpiresAtUtc / EntityType`。
- 当前 membership 校验能挡住跨 workspace，但挡不住伪造序号和延长有效期。

推荐修复方向：

- 使用 DataProtection / HMAC 签名 token。
- 或把 snapshot token 落服务端表，以 token id 查服务端快照状态。

是否阻塞合并：是，影响同步可信边界。

### H3. Migration 方案与“EF Core migration”验收项不一致

涉及文件：

- `src/Orderly.Server/Orderly.Server.csproj`
- `src/Orderly.Server/Data/MigrationRunner.cs`
- `src/Orderly.Server/Migrations/*.sql`

违反的协议点：

- 工程检查要求 EF Core migration 可应用到空 PostgreSQL。

风险：

- 当前使用 DbUp + embedded SQL，不是 EF Core migration。
- 不能按用户指定方式证明 EF Core migration 可应用。

推荐修复方向：

- 明确正式选择：继续 DbUp 则修改验收口径；坚持 EF Core 则补 DbContext 和 EF migrations。
- Thread 6 前应由用户确认 migration 技术路线。

是否阻塞合并：是，验收口径未统一。

### H4. DbUp migration runner 不使用事务

涉及文件：

- `src/Orderly.Server/Data/MigrationRunner.cs`

违反的协议点：

- schema 变更应可控、可回滚、可验证。

风险：

- `MigrationRunner` 使用 `.WithoutTransaction()`。
- migration 中途失败时可能留下半套表或半套索引，影响空库应用和升级安全。

推荐修复方向：

- 对支持事务的 PostgreSQL migration 使用事务。
- 如必须拆事务，逐个脚本标明原因，并提供失败恢复脚本。

是否阻塞合并：不单独阻塞，但必须在 migration 验证前处理或解释。

### H5. 成本字段脱敏口径不一致

涉及文件：

- `src/Orderly.Server/Mapping/CommerceDtoMapper.cs`
- `src/Orderly.Server/Services/CommerceCommandService.InventoryItems.cs`
- `src/Orderly.Server/Services/CommerceCommandService.Archive.cs`
- `src/Orderly.Server/Controllers/CommerceReadController.cs`
- `src/Orderly.Server/Services/WorkspaceSyncQueryService.cs`

违反的协议点：

- 字段级权限必须覆盖成本、利润、现金流等敏感字段。

风险：

- Read / Sync controller 的 inventory mapper 会按 `canViewCosts` 脱敏。
- 共享 `CommerceDtoMapper.ToInventoryItemDto(dynamic r)` 没有 `canViewCosts` 参数，直接返回 `UnitCost`。
- 当前主要风险在服务内部路径和未来复用，容易出现成本字段漏出。

推荐修复方向：

- 所有 DTO mapper 统一要求传入权限上下文。
- 删除无权限参数的敏感 mapper，或默认脱敏。

是否阻塞合并：不单独阻塞，但必须在权限修复中一起收敛。

### H6. build / test / 空库 migration 未执行验证

涉及文件：

- `Orderly.sln`
- `src/Orderly.Server/Orderly.Server.csproj`
- `tests/Orderly.Tests/*`
- `src/Orderly.Server/Migrations/*.sql`

违反的协议点：

- 工程检查要求 `dotnet build`、`dotnet test`、migration 应用到空 PostgreSQL。

风险：

- 本次只读范围禁止生成构建、测试、数据库副作用，所以没有执行。
- 当前报告不能证明代码可编译、测试通过、空库 migration 可用。

推荐修复方向：

- Thread 6 修复完成后，在允许写入产物的上下文中运行：
  - `dotnet build Orderly.sln --nologo`
  - `dotnet test Orderly.sln --no-build --nologo`
  - 用空 PostgreSQL 启动 `Orderly.Server` 验证 DbUp migration。

是否阻塞合并：是，合并前必须补验证。

## D. Medium / Low Risk

### M1. 当前审计基于 dirty working tree

涉及文件：

- `README.md`
- `docs/Cloud Sync v1.txt`
- `src/Orderly.Server/Controllers/CommerceReadController.cs`
- `src/Orderly.Server/Program.cs`
- `src/Orderly.Server/Services/AliyunOssBlobStorage.cs`
- `src/Orderly.Server/Services/CommerceCommandService.cs`
- 多个 `tests/Orderly.Tests/Ui/*`

风险：

- 审计基于当前未提交工作区，不能代表稳定 commit。

建议：

- Thread 6 修复前先确认这些变更属于哪个线程，避免修复时覆盖他人工作。

### M2. Changes cursor 基本符合，但 Snapshot 分页仍使用 offset

涉及文件：

- `src/Orderly.Server/Services/WorkspaceSyncQueryService.cs`

判断：

- `GetChangesAsync` 使用 `Sequence > afterSequence`，没有用 offset，也不依赖客户端时间。
- `GetSnapshotPageAsync` 使用 `LIMIT/OFFSET`，这是 snapshot 分页，不是增量 cursor。当前不算 cursor 违规，但大数据量下性能一般。

建议：

- 增量 cursor 保持 sequence。
- Snapshot 后续可改成 keyset 分页。

### M3. SignalR 基本符合“只通知不传主数据”

涉及文件：

- `src/Orderly.Server/Hubs/WorkspaceHub.cs`
- `src/Orderly.Server/Services/SignalRNotifier.cs`
- `src/Orderly.Contracts/Realtime/RealtimeEventPayload.cs`
- `src/Orderly.Server/Services/CommerceCommandService.cs`

判断：

- SignalR payload 主要包含 `WorkspaceId / EntityType / EntityId / Revision / Sequence / Actor / Action`。
- 没有直接推送业务主数据。

建议：

- 保持 SignalR 只作为提醒。
- 避免未来把 DTO 主体塞进 `HintJson`。

### M4. 永久删除入口目前没有普通用户直通

涉及文件：

- `src/Orderly.Server/Controllers/LifecycleController.cs`
- `src/Orderly.Server/Services/CloudDataLifecycleService.cs`

判断：

- `PermanentlyDeleteAsync` 要求 `Permissions.CanManageUsers(membership)`。
- 服务层要求 `Confirm`、归档状态、保留期，并写审计和 change log。

建议：

- 保持该入口只允许云端管理员。
- 后续增加二次确认文本或操作员确认人字段。

### M5. 附件没有发现公开直链

涉及文件：

- `src/Orderly.Server/Controllers/LifecycleController.cs`
- `src/Orderly.Server/Services/CloudDataLifecycleService.cs`
- `src/Orderly.Server/Services/AliyunOssBlobStorage.cs`
- `src/Orderly.Contracts/Commerce/CloudAttachmentDto.cs`

判断：

- `CloudAttachmentDto` 不返回 `BlobKey` 或公开 URL。
- 下载走 `GET lifecycle/attachments/{attachmentId}/download`，服务端读取 OSS stream。

建议：

- 保持不返回对象存储 key。
- 补齐附件列表访问审计和细粒度权限。

### M6. 未发现明文 refresh token 落库

涉及文件：

- `src/Orderly.Server/Migrations/0001_InitialSchema.sql`
- `src/Orderly.Server/Services/CloudAuthService.cs`

判断：

- `CloudRefreshTokens` 存 `TokenHash`。
- 用户密码和申请密码存 `PasswordHash`。

风险：

- 仍存在 H1 的固定 bootstrap 默认密码。

## E. 人工决策问题

1. 协议文件以哪套为准：恢复用户指定的 `docs/cloud-sync-*.md`，还是把 `docs/Cloud Sync v1.txt` 正式改名为协议源。
2. migration 技术路线：继续 DbUp，还是按验收项改为 EF Core migration。
3. 公开 API 字段命名：是否接受 `Revision/ExpectedRevision/ClientRequestId` 作为 `Version/BaseVersion/IdempotencyKey` 的正式别名。
4. 字段级合并范围：v1 必须覆盖哪些实体和字段，哪些字段仍按整行冲突拒绝。
5. 管理员敏感查看审计粒度：所有详情读取都记，还是只记历史版本、附件、现金流、成本/利润字段。

## F. 推荐修复顺序

1. 补齐或恢复最高协议和 Thread 1-4 handoff 文件。
2. 统一写入契约：`Version/BaseVersion/ChangedFields/IdempotencyKey` 与实现字段对齐。
3. 实现字段级冲突判断和不同字段自动合并。
4. 补齐 `CloudAuditLogs` schema、DTO、审计服务和 correlation/device/result 字段。
5. 把所有写 API 纳入统一幂等框架。
6. 收敛权限：写入口角色、成本字段、重复 reset-password 入口、历史/附件访问权限。
7. 修复 JWT dev key 降级和 bootstrap 默认密码。
8. 决定 migration 技术路线并完成空 PostgreSQL 验证。
9. 运行 build / test / migration / 定向本地预览。

## G. 是否允许进入本地一体化测试

不允许。

允许进入 Thread 6 修复阶段。Thread 6 修复完成并通过 build/test/空库 migration 后，才能进入本地一体化测试。

## Thread 1 检查结论

状态：FAIL / 证据不足。

- 无法确认 Thread 1 schema 是否被后续线程破坏，因为 handoff 和 schema 协议文件缺失。
- 当前 schema 使用 DbUp SQL migration，未发现重复 DbContext。
- 未发现明显同名 DTO 重复定义；存在 DTO 和 server record 分层，属于正常分层。
- `Version / BaseVersion / Cursor / IdempotencyKey` 语义没有统一命名。
- `AuditLog` 字段不完整。

## Thread 2 Sync Engine 检查结论

状态：FAIL。

- Outbox DTO 存在 `BaseRevision / ClientRequestId`，但不含 `ChangedFields / IdempotencyKey`。
- 增量 pull 使用 workspace `Sequence`，符合“不用 offset 作为 cursor”。
- Cursor 不依赖客户端时间。
- 同字段冲突能拒绝，不同字段并发修改不能字段级合并。
- Commerce 幂等可返回原结果；非 Commerce 写入口未覆盖。

## Thread 3 API + SignalR 检查结论

状态：FAIL。

- `SyncController` 只调用 sync query service，未内嵌同步核心逻辑。
- Commerce write controller 调用 command service，未直接写 SQL。
- SignalR 基本只发通知，不推主数据。
- 每个写入 API 的 role / version / idempotency 校验不完整。
- 存在重复 reset-password 入口，权限口径不一致。

## Thread 4 Admin + Ops 检查结论

状态：CONDITIONAL FAIL。

- Admin health / backups / sync issues 以只读为主，未发现直接触碰 Sync 核心写逻辑。
- 用户审批、设备审批、邀请创建、永久删除有审计。
- 管理员查看业务正文、历史版本、附件列表缺访问审计。
- 永久删除具备保留期、确认、管理员限制和审计，当前未发现普通用户永久删除入口。

## 工程检查结论

状态：未通过验收。

- `dotnet build`：未运行，原因是只读范围和写入产物冲突。
- `dotnet test`：未运行，原因是只读范围和测试产物冲突。
- 空 PostgreSQL migration：未运行，原因是会写数据库状态。
- EF Core migration：未发现 EF Core DbContext / migration，当前为 DbUp SQL。
- 重复 DbContext：未发现。
- 重复实体 / DTO：未发现明显同名重复；存在 DTO / Record 分层。
- TODO / fake / in-memory：Server 主链路未发现明显 fake / in-memory 替代实现；测试目录存在正常 fake。
- hardcoded password：发现 bootstrap 默认密码 `OrderlyAdmin@123`。
- 公开附件 URL：未发现附件公开直链。
- 普通用户 permanent delete：未发现。
- 本地配置扩大云端权限：未发现直接扩大权限配置；但 JWT dev key 降级是严重风险。
- 明文密码或 token 落库：未发现明文 refresh token；密码为 hash；但 bootstrap 默认密码风险必须修复。
