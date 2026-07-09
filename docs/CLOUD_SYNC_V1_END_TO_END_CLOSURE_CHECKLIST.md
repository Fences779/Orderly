# Cloud Sync v1 End-to-End Closure Checklist

更新时间：2026-07-10

本文档用途：把 Cloud Sync v1 真正上线可用的总收口标准罗列清楚，并对照当前仓库代码、文档和验收报告判断“已具备、部分具备、未具备”。

本文档不代表最终验收通过。最终通过必须另行产出 `CLOUD_SYNC_V1_END_TO_END_ACCEPTANCE_REPORT.md`，并基于真实 ECS、真实域名、真实 HTTPS、真实多设备和连续稳定观察得出 PASS。

## 1. 当前总判定

当前状态：未达到 Cloud Sync v1 真实上线可用。

原因很直接：

- 服务端、协议、权限、设备、同步、冲突、历史、附件、备份等基础能力已经有较多实现。
- 本地集成测试和本地 Docker Compose 验收报告显示多条链路通过。
- ECS 仍未完成 T10-C 实际部署，未启动正式 `orderly-server`，未执行生产 DbUp，未接入正式域名和 HTTPS。
- 没有真实多设备矩阵验收。
- 没有正式环境 72 小时稳定观察。
- 没有生产恢复演练结果。
- 当前服务端不消费 Redis，部署文档只把现有 Redis baseline 保留为只读验证对象。
- 当前角色模型是 `Admin / Employee`，不是目标里的 `Owner / Admin / Editor / Viewer`。

因此当前只能算“工程基础较完整，本地验收较多，生产端到端未收口”。

## 2. Gate 总览

| Gate | 收口目标 | 当前判断 | 主要原因 |
| --- | --- | --- | --- |
| G1 | 云端服务真正可用 | 部分具备，未通过 | 有 Docker/ECS runbook、health、DbUp、SignalR、PostgreSQL 接入；但正式域名、HTTPS、T10-C、24/72 小时稳定观察未完成，Redis health 未进入应用真实健康检查 |
| G2 | 身份、Workspace 与设备闭环 | 部分具备，未通过 | 用户、设备审批、撤销、JWT/refresh token、workspace membership 已有；角色不满足 Owner/Admin/Editor/Viewer，多 Workspace 隔离未证明 |
| G3 | 同步协议完整闭环 | 部分具备，未通过 | 服务端快照、增量、SignalR、幂等、历史和部分客户端缓存/队列已有；但所有实体全矩阵、真实本地到云端队列到其他设备闭环未完全证明 |
| G4 | 冲突逻辑服务器上线可用 | 部分具备，未通过 | BaseVersion、ChangedFields、IdempotencyKey、409 冲突、本地 smoke 已有；缺少覆盖所有实体的 Entity Conflict Policy Matrix |
| G5 | 离线、重试和异常恢复 | 部分具备，未通过 | Outbox、应急草稿、幂等和重试元数据已有；故障矩阵未在真实客户端和正式云端完整跑完 |
| G6 | 真实多设备端到端验收 | 未通过 | 目前证据主要是本地集成测试和本地 Compose，不是真实多设备 + 正式 ECS |
| G7 | 安全上线标准 | 部分具备，未通过 | JWT、设备撤销、服务端权限、审计、附件授权已有；正式 HTTPS、安全组、secret、生产越权测试未完成 |
| G8 | 版本、历史、附件和删除语义 | 部分具备，未通过 | 历史版本、附件元数据、归档、恢复、永久删除已有；生产对象存储和附件备份恢复未最终验收 |
| G9 | 运维、灾备与长期运行 | 部分具备，未通过 | 备份、恢复演练服务、runbook、health/backups 已有；监控指标、告警、生产恢复演练和长期观察未完成 |
| 性能稳定 | 最低稳定性指标 | 未通过 | 未做真实环境 72 小时观察和真实业务规模同步 |
| 客户端兼容 | 协议和版本升级 | 未通过 | 未看到明确 ProtocolVersion、MinimumSupportedClientVersion、SchemaVersion 强制兼容策略 |
| 最终验收证据 | 顶层验收报告 | 未通过 | 尚未产出最终 PASS 报告 |

## 3. G1 云端服务真正可用

### 3.1 收口标准

必须完成：

- 正式域名。
- HTTPS 有效证书。
- API 只能通过 HTTPS 访问。
- SignalR 使用安全连接。
- PostgreSQL、Redis 不开放公网端口。
- API、Admin、后台任务稳定运行。
- 应用容器异常后自动恢复。
- ECS 重启后服务自动恢复。
- 健康检查可真实反映数据库、Redis 和应用状态。
- 部署与回滚不破坏现有数据库和 volume。

运行标准建议：

| 指标 | 收口标准 |
| --- | --- |
| API 健康检查 | 连续稳定通过 |
| 容器重启 | 无异常循环重启 |
| 稳定观察期 | 至少连续 24 小时无严重错误 |
| 数据库公网端口 | 0 个 |
| 未处理 blocker | 0 |
| 未处理 high risk | 0 |

### 3.2 当前代码和文档对照

已具备：

- `src/Orderly.Server/Program.cs` 已配置 PostgreSQL、JWT、CORS、Controller、SignalR、后台服务、DbUp migration。
- `src/Orderly.Server/Program.cs` 已有 `/health`、`/health/db`、`/health/version`、`/health/backups`。
- `deploy/ecs/docker-compose.prod.yml` 生产模板中 `orderly-server` 只 `expose 8080`，Caddy 通过 `edge` profile 才发布 `80/443`。
- `deploy/ecs/docker-compose.prod.yml` 通过 external network 接入既有 PostgreSQL，不声明旧 volume。
- `deploy/ecs/T10_DEPLOYMENT_RUNBOOK.md` 已写明启动 API 会触发 DbUp，回滚只停新应用，不碰旧 PostgreSQL/Redis/volume。
- `THREAD8_DOCKER_COMPOSE_REPORT.md` 显示本地 Compose health、DB health、重启恢复通过。
- `THREAD9_ECS_PREP_REPORT.md` 显示 ECS 部署准备通过。
- `THREAD10_ECS_DEPLOYMENT_REPORT.md` 显示现有 ECS baseline 中 PostgreSQL 16 和 Redis 7 容器均为内部端口，不发布宿主机端口。

未具备或未完成：

- 未完成 T10-C 实际部署。
- 未配置并验收正式域名。
- 未配置并验收真实 HTTPS 证书。
- 未证明 API 只能通过 HTTPS 访问。
- 未证明 SignalR 在正式 HTTPS/WSS 下可用。
- 当前 `Orderly.Server` 不消费 Redis，应用层没有真实 Redis 连接和 Redis health。
- 未完成正式环境 24 小时或 72 小时稳定观察。
- T10 当前整体仍为 FAIL，报告中 high risk = 2。

当前结论：G1 未通过。可以进入后续生产部署验收，但不能宣称云端服务真正可用。

## 4. G2 身份、Workspace 与设备闭环

### 4.1 收口标准

必须存在明确云端身份模型：

```text
User
└── Workspace Membership
    ├── Owner
    ├── Admin
    ├── Editor
    └── Viewer

User
└── Approved Devices
```

验收包括：

- 用户能正常登录和刷新认证状态。
- Token 过期后能安全续期或重新登录。
- 设备首次连接需要注册或批准。
- 被撤销设备不能继续同步。
- Workspace Owner/Admin 可以管理成员和设备。
- Viewer 不能写入。
- Editor 不能执行管理员操作。
- 一个用户加入多个 Workspace 时数据完全隔离。
- 离开 Workspace 后立即失去访问权限。
- 所有敏感拒绝都留下审计记录。
- 不得仅依赖客户端隐藏按钮。
- 权限必须由服务器强制执行。

### 4.2 当前代码和文档对照

已具备：

- `src/Orderly.Server/Controllers/AuthController.cs` 提供 login、refresh、logout-all、me、change-password、reset-password。
- `src/Orderly.Server/Controllers/UsersController.cs` 提供邀请、申请、审批、设备批准/拒绝/撤销、用户创建、禁用、重置密码。
- `src/Orderly.Server/Program.cs` 的 JWT 验证会检查用户启用状态、token_version、device_id、设备访问、membership 是否有效。
- `CloudRefreshTokens` 支持 token family，refresh token 数据库存 hash。
- `CloudDevices`、`CloudUserApplications`、`CloudInvitations` 已由迁移补齐。
- `THREAD7_INTEGRATION_REPORT.md` 和 `THREAD8_DOCKER_COMPOSE_REPORT.md` 记录了用户申请、审批、设备批准/拒绝/撤销、撤销设备失效、越权拒绝等本地验收。
- 服务端控制器里大量入口先执行 workspace access 或 admin permission 检查，不是只靠客户端隐藏按钮。

未具备或未完成：

- `src/Orderly.Contracts/Permissions/CloudRole.cs` 当前只有 `Admin` 和 `Employee`，不满足 `Owner / Admin / Editor / Viewer`。
- `src/Orderly.Server/Services/ICloudPermissionService.cs` 当前权限模型偏粗，主要是 Admin 与 Employee 分层。
- Viewer 不能写、Editor 不能管理这两个目标角色当前不存在。
- 多 Workspace 成员身份和跨 Workspace 完整隔离未作为真实多工作区矩阵验收完成。
- 离开 Workspace 后立即失去访问权限需要明确接口和验收证据，目前主要看到 disable / membership enabled / token version 类能力。
- 敏感拒绝都有审计这一点未证明覆盖所有拒绝路径。

当前结论：G2 部分具备，但角色模型和多 Workspace 闭环未通过。

## 5. G3 同步协议完整闭环

### 5.1 收口标准

每种云同步实体都必须支持：

- 创建。
- 修改。
- 归档。
- 恢复。
- 删除或墓碑。
- 增量拉取。
- 全量恢复。
- 版本历史。
- 幂等重试。

服务器必须是最终可信源。

必须验证的同步路径：

```text
1. 本地到云端
本地修改
→ 写入本地事务
→ 写入待同步队列
→ 上传云端
→ 云端确认版本
→ 本地标记同步成功

2. 云端到其他设备
云端提交成功
→ SignalR 通知其他设备
→ 设备拉取变更
→ 本地事务应用
→ 更新同步游标

3. 断线补偿
设备断线
→ 其他设备产生多个变更
→ 设备重新联网
→ 根据游标拉取所有缺失变更
→ 顺序应用
→ 恢复实时订阅
```

SignalR 只能负责“通知有变化”，不能成为唯一数据来源。真正数据必须通过带版本和游标的 API 拉取。

### 5.2 当前代码和文档对照

已具备：

- `src/Orderly.Server/Services/WorkspaceSyncService.cs` 负责按 workspace 分配 sequence 并写入 `CloudChangeLog`。
- `src/Orderly.Server/Controllers/SyncController.cs` 提供 snapshot、snapshot page、changes。
- `src/Orderly.Server/Services/WorkspaceSyncQueryService.cs` 使用服务端 sequence、snapshot token、分页、增量拉取，并支持 `FullResyncRequired`。
- `src/Orderly.Server/Hubs/WorkspaceHub.cs` 使用已认证用户加入自己 workspace group。
- `src/Orderly.Server/Services/CommerceCommandService.cs` 在事务提交后通过 SignalR 通知变更。
- `src/Orderly.Contracts/Offline/ICloudOutboxStore.cs` 和 `src/Orderly.Data/Cloud/CloudOutboxStore.cs` 提供客户端本地 outbox 基础设施。
- `THREAD7_INTEGRATION_REPORT.md` 与 `THREAD8_DOCKER_COMPOSE_REPORT.md` 记录了 Sync A/B/C、断线期间补拉、SignalR 只通知不传主数据、幂等等本地验收。

未具备或未完成：

- “每种云同步实体”是否都完整覆盖创建、修改、归档、恢复、删除/墓碑、增量、全量、历史、幂等，没有一张完整矩阵证明。
- 当前离线主链路并非所有写操作都能进入统一 outbox；仓库里同时存在 CloudOutbox、EmergencyDraft、Remote 服务降级等多条路径。
- 真实设备 A/B/C 之间的本地事务、云确认、本地标记同步成功、其他设备拉取应用未在正式 ECS 环境验收。
- 本地库丢失后从云端完整恢复未完成真实验收。

当前结论：G3 部分具备，但全实体矩阵和真实多设备链路未通过。

## 6. G4 冲突逻辑服务器上线可用

### 6.1 收口标准

服务器至少要保存：

- EntityId。
- WorkspaceId。
- Version。
- UpdatedAt。
- UpdatedBy。
- SourceDeviceId。
- IsDeleted / ArchivedAt。
- PreviousVersion 或 History。

客户端提交修改时必须携带：

- BaseVersion。
- OperationId。
- DeviceId。

冲突判定例子：

```text
云端当前版本：8
设备提交 BaseVersion：8
→ 正常接受，生成版本 9

云端当前版本：9
设备提交 BaseVersion：8
→ 检测到陈旧写入，禁止直接覆盖
```

最低安全要求：

- 不允许无条件 last write wins 覆盖所有数据。
- 不允许客户端用旧版本覆盖新版本。
- 不允许相同请求重试产生重复记录。
- 冲突结果必须确定、可追踪、可重放。
- 冲突必须保留双方数据或足够的历史证据。
- 冲突处理结果必须同步到所有设备。

每种实体必须明确采用哪种策略：

| 类型 | 可选策略 |
| --- | --- |
| 简单状态字段 | 服务器规则或明确的最后写入规则 |
| 独立字段修改 | 经验证后自动字段级合并 |
| 同一字段同时修改 | 生成冲突，不静默覆盖 |
| 删除与修改并发 | 明确删除优先或生成冲突 |
| 集合成员 | 按元素操作合并，避免整集合覆盖 |
| 金额、权限、关键状态 | 禁止自动合并，必须严格版本检查 |

收口前必须有一份 `Entity Conflict Policy Matrix`，覆盖所有同步实体。

### 6.2 当前代码和文档对照

已具备：

- `src/Orderly.Contracts/Commerce/WriteCommandBase.cs` 已有 `BaseVersion`、`ChangedFields`、`IdempotencyKey` 等字段。
- `src/Orderly.Server/Services/CommerceCommandService.cs` 通过幂等服务和 revision 控制写入。
- `src/Orderly.Server/Services/IdempotencyService.cs` 提供按 `ClientRequestId` 去重和结果回放。
- `src/Orderly.Server/Services/CloudDataLifecycleService.cs` 和 `CloudEntityVersions` 支持版本历史。
- `THREAD7_INTEGRATION_REPORT.md` 和 `THREAD8_DOCKER_COMPOSE_REPORT.md` 记录了同字段并发修改 409、不同字段字段级合并、同一 IdempotencyKey 只写一次。

未具备或未完成：

- 目前没有独立的 `Entity Conflict Policy Matrix` 文档。
- 没有看到覆盖所有实体的删除与修改并发策略矩阵。
- 没有看到集合成员按元素操作合并的全实体策略说明。
- 金额、权限、关键状态的严格版本检查需要逐实体列证据。
- `SourceDeviceId` 在审计和 token 中有设备概念，但每条业务实体是否都保存 SourceDeviceId 未完整证明。
- 冲突结果同步到所有真实设备尚未正式验收。

当前结论：G4 部分具备，但缺少全实体冲突策略矩阵和真实上线验证。

## 7. G5 离线、重试和异常恢复

### 7.1 收口标准

必须主动模拟以下故障：

| 场景 | 正确结果 |
| --- | --- |
| 上传过程中断网 | 操作保留在本地队列，联网后重试 |
| 云端已成功但客户端未收到响应 | 重试不重复创建 |
| SignalR 断开 | 自动重连并用游标补齐 |
| 客户端崩溃 | 重启后继续未完成同步 |
| 服务器重启 | 客户端自动恢复 |
| 请求超时 | 不直接认定失败并重复写入 |
| 变更乱序到达 | 按版本安全处理 |
| 同一操作重复发送 | 由 OperationId 幂等去重 |
| 本地游标损坏 | 能重新执行安全的全量或增量恢复 |
| 客户端本地库丢失 | 能从云端重新恢复 |

必须证明：

```text
至少一次投递
+
幂等处理
+
版本控制
=
不会因为网络异常重复或覆盖数据
```

### 7.2 当前代码和文档对照

已具备：

- `CloudOutboxStore` 记录待上传变更、失败原因、重试次数和下次重试时间。
- `EmergencyDraftController`、`EmergencyDraftRepository`、`EmergencyDraftProcessor`、`EmergencyDraftBackgroundService` 提供应急草稿兜底。
- `IdempotencyService` 可以处理重复请求和结果回放。
- `WorkspaceSyncQueryService` 使用服务端 sequence，支持缺口过大时 FullResync。
- `RemoteWorkspaceSyncClient` 和客户端状态里已有同步尝试、成功、失败、全量重同步等状态。
- `THREAD8_DOCKER_COMPOSE_REPORT.md` 记录了服务容器重启后 health 恢复、down/up 后 PostgreSQL volume 保留、再次 smoke 通过。

未具备或未完成：

- 这些故障没有全部在真实客户端和正式 ECS 环境主动模拟。
- 本地游标损坏后的恢复策略未形成验收证据。
- 客户端本地库丢失后完整从云端恢复未形成验收证据。
- 请求超时、云端成功但客户端未收到响应、客户端崩溃等场景未看到独立验收结果。
- Outbox 与 EmergencyDraft 的边界需要收敛，否则容易出现“部分操作可离线、部分只能应急”的体验不一致。

当前结论：G5 部分具备，但故障矩阵未通过。

## 8. G6 真实多设备端到端验收

### 8.1 收口标准

不能只用单元测试和本地脚本。

最低设备矩阵：

- 设备 A：Orderly 正常客户端。
- 设备 B：另一台真实设备或独立客户端环境。
- 云端：正式 ECS 环境。
- 两个不同用户。
- 至少两个 Workspace。
- 至少三种角色。

必测用例：

- A 创建，B 收到。
- B 修改，A 收到。
- A 归档，B 同步归档。
- B 恢复，A 同步恢复。
- 新设备 C 首次登录并完整恢复。
- A 离线修改多条，B 在线修改其他数据，A 恢复联网，双方最终一致。
- A/B 同时基于同一版本修改同一条数据，云端产生预期冲突结果，两端最终获得相同处理结果。
- Viewer 修改被服务器拒绝。
- 已撤销设备继续请求被服务器拒绝。
- Workspace A 用户访问 Workspace B 被服务器拒绝。
- 管理操作产生审计日志。

### 8.2 当前代码和文档对照

已具备：

- 本地集成测试和 Compose smoke 覆盖了很多多用户、多设备、权限、同步场景。
- `THREAD7_INTEGRATION_REPORT.md` 和 `THREAD8_DOCKER_COMPOSE_REPORT.md` 记录了本地环境里的 A/B 同步、冲突、设备撤销、越权拒绝。

未具备或未完成：

- 没有正式 ECS 环境上的真实多设备验收。
- 没有两台真实设备或独立客户端环境的验收报告。
- 没有至少两个 Workspace、至少三种角色的真实验收。
- 当前角色模型只有 Admin/Employee，不能满足三种角色。
- 新设备 C 完整恢复未在真实环境验收。

当前结论：G6 未通过。

## 9. G7 安全上线标准

### 9.1 收口标准

必须通过：

- 全链路 HTTPS。
- 数据库和 Redis 仅内部网络可达。
- Secret 不在 Git、日志、README、镜像中。
- 客户端不保存管理员级云端凭据。
- Token 有有效期和撤销能力。
- WorkspaceId 不能仅由客户端自由指定后直接信任。
- 所有查询和写入都进行 Workspace 权限校验。
- 文件附件使用授权访问，而不是永久公开 URL。
- 上传文件限制类型、大小和归属。
- 日志脱敏。
- 登录、设备批准、角色修改、冲突解决均有审计。
- 安全组 SSH 只允许受控 IP 或受控管理方式。

必须进行一次越权测试：

```text
使用合法账号和合法 Token
请求不属于该账号的 Workspace 数据
结果必须始终为拒绝，且不能泄露记录是否存在
```

### 9.2 当前代码和文档对照

已具备：

- `Program.cs` 强制 JWT 认证、token_version、device_id、membership 和设备访问校验。
- `CloudControllerBase` 及各 Controller 入口有 workspace access 校验。
- `WorkspaceHub` 只能加入 token 所属 workspace。
- 附件下载通过 `LifecycleController` 和 workspace 授权，不直接暴露永久公开 URL。
- `CloudAuditLogs` 包含 Actor、Action、Entity、DeviceId、Result、CorrelationId 等字段。
- `THREAD7_INTEGRATION_REPORT.md` 和 `THREAD8_DOCKER_COMPOSE_REPORT.md` 记录了跨 workspace sync、普通用户越权、撤销设备等拒绝。
- `deploy/ecs/SECRETS_CHECKLIST.md` 和 `scripts/ecs-preflight.ps1` 覆盖 secret 和危险配置检查。

未具备或未完成：

- 正式 HTTPS 未验收。
- ECS 安全组未完成真实配置验收。
- Secret 不在镜像和日志中需要生产构建与日志实际验证。
- Redis 只在现有 baseline 中保留，应用层没有 Redis 使用与权限验证。
- 越权测试尚未在正式 ECS + 正式 HTTPS 下执行。
- 审计覆盖“所有敏感拒绝”还需要逐接口确认。
- 上传文件类型限制需要进一步确认，目前看到大小、配额、归属和 hash，类型策略未完整列出。

当前结论：G7 部分具备，但安全上线未通过。

## 10. G8 版本、历史、附件和删除语义

### 10.1 收口标准

历史：

- 每次关键修改可追溯。
- 能知道谁、哪台设备、何时修改。
- 冲突前后版本可查看。
- 历史不能被普通客户端随意篡改。

删除：

- 必须明确是软删除、墓碑同步、延迟物理清理。
- 不能直接物理删除后让离线设备再次把旧数据上传回来。

附件：

- 元数据与业务实体同步一致。
- 上传失败不会产生永久脏引用。
- 删除实体时附件进入明确保留或清理流程。
- 附件不使用生产对象存储时，只能算测试环境，不算最终生产收口。
- 最终上线必须接入确定的持久化存储方案。

### 10.2 当前代码和文档对照

已具备：

- `src/Orderly.Server/Migrations/0006_LifecycleAttachmentsAndHistory.sql` 增加历史、附件等生命周期表。
- `CloudDataLifecycleService` 记录实体版本历史，支持附件上传、下载、归档、永久删除。
- `LifecycleController` 提供 history、attachments、download、archive attachment、permanent delete。
- `ArchiveController` 提供归档和恢复。
- 永久删除要求管理员、确认、已归档、达到保留期。
- `THREAD7_INTEGRATION_REPORT.md` 记录了历史、附件授权、下载审计、永久删除验收。
- `THREAD8_DOCKER_COMPOSE_REPORT.md` 记录了附件不进入 PostgreSQL bytea，未授权不能下载，响应不返回 BlobKey 或公开 URL。

未具备或未完成：

- 生产对象存储最终接入未在正式 ECS 验收。
- 附件存储备份和恢复未在生产环境演练。
- 删除与离线重放并发时的墓碑策略没有完整实体矩阵。
- 冲突前后版本查看需要结合冲突策略矩阵一起验收。
- 上传文件类型限制和失败清理策略需要独立证据。

当前结论：G8 部分具备，但最终生产附件和删除语义未通过。

## 11. G9 运维、灾备与长期运行

### 11.1 收口标准

监控必须覆盖：

- API 错误率。
- 容器状态。
- 数据库连接。
- Redis 连接。
- 同步失败数量。
- 冲突数量。
- 待处理队列积压。
- SignalR 连接异常。
- 磁盘空间。
- 备份是否成功。

日志每次同步请求至少可以关联：

- RequestId。
- OperationId。
- UserId。
- WorkspaceId。
- DeviceId。
- EntityType。
- EntityId。
- Result。

但不得记录真实密码、完整 Token 或敏感内容。

备份与恢复必须具备：

- PostgreSQL 自动备份。
- 附件存储备份。
- 备份有保留周期。
- 备份有校验。
- 实际完成一次恢复演练。
- 恢复后客户端可以重新同步。
- 恢复操作不会让版本号和游标失效到不可修复。

没有恢复演练，不能称为可用。

### 11.2 当前代码和文档对照

已具备：

- `DatabaseBackupService` 使用 `pg_dump -Fc`。
- `BackupBackgroundService` 提供后台备份。
- `DatabaseRestoreDrillService` 提供恢复演练能力。
- `/health/backups` 暴露备份和恢复演练状态。
- `AdminController` 提供 admin health、backups、audit-logs、sync-issues。
- `deploy/ecs/BACKUP_RESTORE_RUNBOOK.md` 已覆盖 PostgreSQL、Redis baseline、附件、恢复流程。
- `THREAD7_INTEGRATION_REPORT.md` 记录本地 Admin/Ops health/backup/sync endpoints 可读取。

未具备或未完成：

- 没有正式环境恢复演练结果。
- 没有生产监控接入结果。
- Redis 连接健康不在应用真实 health 内。
- API 错误率、冲突数量、队列积压、SignalR 异常、磁盘空间等未形成统一监控面板或告警。
- 恢复后客户端重新同步未在正式环境验收。
- 日志字段链路需要按真实请求抽样核验，不能只看表结构。

当前结论：G9 部分具备，但运维灾备未通过。

## 12. 性能与稳定性最低线

### 12.1 收口标准

V1 不要求大型互联网规模，但必须定义底线：

| 项目 | 建议标准 |
| --- | --- |
| 普通同步延迟 | 在线设备通常 5 秒内看到变化 |
| 断线重连 | 自动完成，不需要手工修数据库 |
| 单次批量同步 | 至少支持正常真实业务规模 |
| API 错误 | 不允许持续性 5xx |
| 数据丢失 | 0 |
| 静默冲突覆盖 | 0 |
| Workspace 越权 | 0 |
| 重复实体 | 0 |
| 连续稳定运行 | 至少 72 小时真实环境观察 |
| 严重未处理告警 | 0 |

具体容量数字要根据真实 Orderly 数据规模设定，不能只做三条测试数据就宣布完成。

### 12.2 当前代码和文档对照

已具备：

- 本地测试覆盖了一些同步、冲突、幂等和重启恢复场景。
- 服务端有分页、limit、sequence、snapshot token、导出后台任务和健康检查。

未具备或未完成：

- 未定义真实业务规模容量指标。
- 未完成真实环境 72 小时稳定观察。
- 未完成同步延迟统计。
- 未完成持续 5xx、队列积压、冲突数量、SignalR 异常等监控验收。

当前结论：性能与稳定性最低线未通过。

## 13. 客户端兼容和升级标准

### 13.1 收口标准

必须处理：

- 老客户端遇到新服务器协议时的行为。
- 新客户端连接旧服务器时的行为。
- 必须升级的最低客户端版本。
- 数据结构升级。
- 同步协议版本。
- 不兼容客户端的明确拒绝和提示。
- 客户端本地数据库 migration 失败后的恢复方案。

推荐明确：

- ProtocolVersion。
- ClientVersion。
- MinimumSupportedClientVersion。
- SchemaVersion。

服务器不能默认所有客户端永远使用同一个版本。

### 13.2 当前代码和文档对照

已具备：

- `/health/version` 返回当前服务版本。
- DbUp 管理服务端数据库 schema 迁移。
- 客户端本地 SQLite/SQLCipher 有 schema initializer 和迁移相关代码。

未具备或未完成：

- 未看到服务端强制 `ProtocolVersion`。
- 未看到 `MinimumSupportedClientVersion` 的拒绝策略。
- 未看到新旧客户端兼容矩阵。
- 未看到客户端本地数据库 migration 失败后的云同步恢复验收报告。
- 未看到协议升级时的灰度、拒绝和提示链路。

当前结论：客户端兼容和升级标准未通过。

## 14. 最终验收证据

最终必须产生顶层报告：

```text
CLOUD_SYNC_V1_END_TO_END_ACCEPTANCE_REPORT.md
```

报告至少包含：

- 生产架构图。
- 服务和资源所有权。
- 同步协议说明。
- 冲突策略矩阵。
- 权限矩阵。
- 多设备测试矩阵。
- 离线与故障测试结果。
- 安全测试结果。
- 数据隔离测试。
- 备份恢复演练。
- 性能与稳定性观察。
- 已知限制。
- 客户端兼容范围。
- 实际版本和 commit。
- 最终 PASS/FAIL。

最终收口判定只有下面全部成立，才算阶段完成：

| 项目 | 目标 |
| --- | --- |
| Cloud Sync v1 End-to-End | PASS |
| Infrastructure blocker | 0 |
| Application blocker | 0 |
| Security blocker | 0 |
| Data integrity blocker | 0 |
| High risk | 0 |
| Real multi-device sync | PASS |
| Offline catch-up | PASS |
| Full device restore | PASS |
| Conflict detection | PASS |
| Conflict resolution | PASS |
| Idempotency | PASS |
| Workspace isolation | PASS |
| Role enforcement | PASS |
| Device revocation | PASS |
| SignalR realtime | PASS |
| Incremental pull | PASS |
| History and audit | PASS |
| Attachment lifecycle | PASS |
| Backup restore drill | PASS |
| 72-hour stability | PASS |
| Silent data loss | 0 |
| Silent overwrite | 0 |
| Cross-workspace leakage | 0 |
| Public PostgreSQL/Redis exposure | 0 |
| Ready for controlled real use | YES |

当前结论：最终验收证据未完成。

## 15. 当前已实现能力清单

按现有代码和报告，可以认定当前仓库已经有这些基础能力：

- .NET 8 `Orderly.Server` 云端服务。
- PostgreSQL 16 + Dapper + DbUp migration。
- JWT access token。
- Refresh token hash 存储、轮换、撤销、token_version 失效。
- 用户、工作区成员、邀请、申请、设备审批。
- 设备绑定登录、设备撤销。
- SignalR workspace hub。
- 云端写入幂等键。
- Workspace sequence。
- ChangeLog 增量拉取。
- Snapshot token 和分页全量恢复入口。
- 业务实体云端读写 API。
- 归档、恢复、永久删除。
- 版本历史。
- 附件元数据、上传、下载、归档。
- 管理员 health、audit logs、sync issues。
- 应急草稿。
- 本地 CloudOutbox 基础设施。
- 本地集成 smoke。
- 本地 Docker Compose smoke。
- ECS deployment runbook 和 preflight。
- PostgreSQL 备份和恢复演练服务。
- `/health`、`/health/db`、`/health/backups`。

## 16. 当前明确缺口清单

必须收口的缺口：

- 正式 ECS T10-C 未完成。
- 正式域名未完成。
- 正式 HTTPS 未完成。
- 正式 WSS/SignalR 未完成。
- 生产 DbUp 接管未完成。
- 正式多设备验收未完成。
- 72 小时稳定观察未完成。
- Redis 仅为 baseline，应用层不消费 Redis，没有真实 Redis health。
- 角色模型不满足 Owner/Admin/Editor/Viewer。
- 多 Workspace 隔离没有真实矩阵验收。
- 全实体同步能力矩阵缺失。
- 全实体冲突策略矩阵缺失。
- 离线和故障矩阵缺失真实验收。
- 正式对象存储和附件备份恢复未验收。
- 生产监控和告警未接入。
- 生产恢复演练未完成。
- 客户端协议版本和最低版本策略缺失。
- 顶层最终验收报告未产出。

## 17. 阶段边界

这个阶段完成后，Orderly 应达到：

可以在多个真实设备之间，通过正式云端长期同步真实数据。出现断网、重复请求、并发修改、设备更换和服务器重启时，系统仍然保持数据安全与最终一致。

不能把以下状态当成阶段完成：

- API 能启动。
- 本地脚本通过。
- 两个测试客户端偶尔同步成功。
- 只做了 Docker Compose。
- 只写了 runbook。
- 只保留了 Redis 容器但应用没有真实使用。
- 只靠客户端隐藏按钮控制权限。
- 只测试三条数据。

最终目标是：不是演示版，不是“API 通了”，而是真实可控上线。

## 18. 下一步建议

按最短收口路径，下一步不是继续补业务功能，而是补验收闭环：

1. 先完成 G1：T10-C，正式 ECS 启动 API，允许 DbUp，接入域名和 HTTPS。
2. 再补 G2/G4 文档矩阵：权限矩阵、角色模型、Entity Conflict Policy Matrix。
3. 再跑 G6/G5：真实多设备、离线、冲突、恢复矩阵。
4. 再跑 G7/G9：安全越权、备份恢复、监控告警。
5. 最后做 72 小时观察并产出最终验收报告。
