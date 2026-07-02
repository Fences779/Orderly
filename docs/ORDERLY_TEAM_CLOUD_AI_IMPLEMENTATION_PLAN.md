# Orderly 团队云端实时协同施工方案（给 AI / 工程代理）

## 1. 目标与边界

本方案把当前 Orderly 从“本地优先 PC 经营管理系统”升级为“正式版默认连接中心服务端”的多人实时协同系统。

最终形态：

```text
Orderly WPF 正式客户端
    -> HTTPS REST API
    -> Orderly.Server
    -> PostgreSQL 中心经营数据库

Orderly WPF 正式客户端
    <- SignalR 实时事件
    <- Orderly.Server
```

已确认的业务边界：

- 第一版只做经营数据实时协同。
- 不接微信支付。
- 不接 AI 生成。
- 不接外部平台回写。
- 不做小程序支付链路。
- 不做用户可见的“本地模式 / 团队模式”切换。
- 正式版以云端数据为准；本地只保留登录缓存、最近数据缓存、断网只读、应急预处理草稿、开发调试数据。
- 阿里云杭州 / 华东节点优先，预算按低成本正式可用版设计。

第一版参与人：

- 运营负责人：1 人，最高权限。
- 投资方：1 人，最高权限，经常国内外移动。
- 员工：1 人，受限权限。

## 2. 当前代码现状

当前仓库是 WPF + .NET 8 + SQLite/SQLCipher 本地系统，解决方案入口为 `Orderly.sln`。

现有项目：

- `src/Orderly.App`：WPF 应用入口、View、ViewModel、组合根。
- `src/Orderly.Core`：核心模型、服务接口、Commerce 通用经营模型。
- `src/Orderly.Data`：SQLite/SQLCipher 仓储、服务实现、本地账号、安全、备份。
- `src/Orderly.Infrastructure`：热键、更新、托盘等桌面基础设施。
- `tests/Orderly.Tests`：现有单元测试、属性测试、UI 合同测试、回归测试。
- `miniprogram/`、`cloudfunctions/`：不是本次范围。

现有经营数据主线：

- Commerce 实体在 `src/Orderly.Core/Commerce/`。
- Commerce 表初始化在 `src/Orderly.Data/Sqlite/CommerceSchemaInitializer.Tables.cs`。
- 现有 18 张 Commerce 表：`CommerceBusinessWorkspaces`、`CommerceBusinessTemplates`、`CommerceCustomFieldDefinitions`、`CommerceUnitDefinitions`、`CommerceProducts`、`CommerceProductVariants`、`CommerceInventoryItems`、`CommerceInventoryMovements`、`CommerceCustomers`、`CommerceCustomerContacts`、`CommerceOrders`、`CommerceOrderItems`、`CommercePaymentRecords`、`CommerceCashFlowEntries`、`CommerceSuppliers`、`CommerceBusinessTasks`、`CommerceBusinessInsights`、`CommerceBusinessMetricSnapshots`。
- 通用仓储接口为 `ICommerceRepository<TEntity>`。
- SQLite 仓储基类为 `CommerceRepositoryBase<TEntity>`，默认 active 查询排除 `DeletedAt != null` 的记录。
- `CommerceEntity` 已有 `Archive()`、`SoftDelete()`、`Recover()`，生命周期枚举已有 `Active / Archived / Deleted`。
- 当前 `DeleteAsync` 是软删除语义，但正式业务文案统一叫“归档”，不要在 UI 对用户说“删除”。

当前页面服务边界：

- `App.WorkspaceComposition.cs` 在登录后装配本地 SQLite 仓储和 Commerce 服务。
- `MainViewModel.AttachCommercePages(...)` 挂载 7 个经营页 VM。
- `WorkbenchPageViewModel` 依赖 `IDashboardService`。
- `OrdersPageViewModel` 依赖 `IOrderService` + `ICommerceOrderRepository`。
- `ProductsPageViewModel` 依赖 `IProductService`。
- `InventoryPageViewModel` 依赖 `IInventoryService` + `IInventoryItemRepository`。
- `CustomersPageViewModel` 依赖 `ICustomerService` + `ICommerceCustomerRepository` + 本地设置仓储。
- `CashflowPageViewModel` 依赖 `ICashFlowService` + `ICashFlowEntryRepository`。
- `BusinessAdvicePageViewModel` 依赖 `IBusinessInsightService`。

当前账号边界：

- 本地账号只有 `LocalAccountRole.Owner` 和 `LocalAccountRole.Member`。
- 这套本地账号继续负责本机解锁、安全、本地数据库密钥。
- 云端权限不能直接复用本地 Owner/Member；第一版只新增云端团队身份、业务标签和固定角色策略。

## 3. 新增项目与引用关系

新增 3 个主项目：

```text
src/Orderly.Contracts
src/Orderly.Server
src/Orderly.Remote
```

### 3.1 `Orderly.Contracts`

类型：`net8.0` 类库。

用途：

- 存放客户端与服务端共享的 API DTO、命令、响应、实时事件、权限枚举。
- 引用 `Orderly.Core`，复用 Commerce 枚举和值对象含义。
- 不引用 `Orderly.Data`、`Orderly.App`、`Orderly.Server`。

建议目录：

```text
src/Orderly.Contracts/Auth
src/Orderly.Contracts/Commerce
src/Orderly.Contracts/Realtime
src/Orderly.Contracts/Permissions
src/Orderly.Contracts/Exports
src/Orderly.Contracts/Offline
```

### 3.2 `Orderly.Server`

类型：ASP.NET Core `net8.0` Web API。

用途：

- 云端唯一业务写入口。
- 管理账号、权限、经营数据、实时事件、操作日志、导出、备份健康检查。
- 使用 PostgreSQL。
- 暴露 REST API + SignalR Hub。

依赖：

- `Orderly.Core`
- `Orderly.Contracts`
- `Npgsql`
- `Microsoft.AspNetCore.Authentication.JwtBearer`
- `ClosedXML`

禁止：

- 不引用 WPF。
- 不引用本地 SQLCipher 数据层。
- 不读取用户电脑上的本地数据库。

### 3.3 `Orderly.Remote`

类型：`net8.0` 类库。

用途：

- WPF 客户端的云端访问层。
- 实现远端版本的 Commerce 服务和仓储适配器。
- 维护登录 token、SignalR 连接、本地缓存、应急草稿队列。

依赖：

- `Orderly.Core`
- `Orderly.Contracts`
- `System.Net.Http.Json`
- `Microsoft.AspNetCore.SignalR.Client`

`Orderly.App` 新增引用：

- `Orderly.Remote`
- `Orderly.Contracts`

不要让 UI 直接拼 HTTP；所有远端访问必须经过 `Orderly.Remote`。

## 4. 云端数据库设计

PostgreSQL 中心库分两类表：

1. 经营业务表：镜像当前 18 张 `Commerce*` 表，并增加云端协作字段。
2. 云端协作表：账号、权限、操作日志、编辑提示、改价申请、导出任务、离线草稿等。

### 4.1 Commerce 表迁移规则

所有 Commerce 表保留现有字段名和语义，便于和现有 Core 模型互转。

每张云端 Commerce 表额外增加：

```text
Revision BIGINT NOT NULL DEFAULT 1
CreatedByUserId UUID NULL
UpdatedByUserId UUID NULL
ArchivedByUserId UUID NULL
ArchiveReason TEXT NULL
```

说明：

- `Revision` 是并发版本号，每次修改、归档、恢复都加 1。
- 客户端提交修改时必须携带自己读取时拿到的 `Revision`。
- 服务端更新时使用 `WHERE Id = @id AND Revision = @expectedRevision`。
- 更新不到行时返回 `409 Conflict`，并返回最新数据摘要，客户端提示“数据已被他人修改，请刷新后再操作”。
- 归档使用现有 `DeletedAt + Lifecycle = Archived`，不做物理删除。
- `Deleted` 状态第一版不要暴露给业务用户。

### 4.2 云端账号表

`CloudUsers`

```text
Id UUID PRIMARY KEY
Username TEXT NOT NULL UNIQUE
DisplayName TEXT NOT NULL
PasswordHash TEXT NOT NULL
IsEnabled BOOLEAN NOT NULL DEFAULT TRUE
CreatedByUserId UUID NULL
CreatedAt TIMESTAMPTZ NOT NULL
UpdatedAt TIMESTAMPTZ NOT NULL
DisabledAt TIMESTAMPTZ NULL
DisabledByUserId UUID NULL
```

`CloudWorkspaces`

```text
Id UUID PRIMARY KEY
Name TEXT NOT NULL
DefaultCurrencyCode TEXT NULL
CreatedAt TIMESTAMPTZ NOT NULL
UpdatedAt TIMESTAMPTZ NOT NULL
```

`CloudWorkspaceMembers`

```text
WorkspaceId UUID NOT NULL
UserId UUID NOT NULL
CloudRole TEXT NOT NULL
BusinessLabel TEXT NOT NULL
RolePolicyVersion INTEGER NOT NULL DEFAULT 1
IsEnabled BOOLEAN NOT NULL DEFAULT TRUE
CreatedByUserId UUID NULL
CreatedAt TIMESTAMPTZ NOT NULL
UpdatedByUserId UUID NULL
UpdatedAt TIMESTAMPTZ NOT NULL
PRIMARY KEY (WorkspaceId, UserId)
```

`CloudRole` 第一版固定：

- `Admin`
- `Employee`

`BusinessLabel` 第一版固定：

- `运营负责人`
- `投资方`
- `员工`

注意：

- 本地 `Owner / Member` 只对本机安全生效。
- 云端权限以 `CloudWorkspaceMembers` 为准。
- 员工账号是公司团队操作身份，不是单纯本机账号。
- 第一版只允许固定角色 `Admin / Employee`，不做细粒度自定义权限；`RolePolicyVersion` 用来标记服务端固定权限版本。

### 4.3 操作日志

`CloudAuditLogs`

```text
Id UUID PRIMARY KEY
WorkspaceId UUID NOT NULL
ActorUserId UUID NULL
ActorDisplayName TEXT NOT NULL
ActorRole TEXT NOT NULL
Action TEXT NOT NULL
EntityType TEXT NOT NULL
EntityId UUID NULL
BeforeJson TEXT NULL
AfterJson TEXT NULL
Reason TEXT NULL
ClientRequestId TEXT NULL
OccurredAt TIMESTAMPTZ NOT NULL
IpAddress TEXT NULL
UserAgent TEXT NULL
```

必须写日志的动作：

- 登录成功 / 失败。
- 创建账号、停用账号、修改权限。
- 新增、修改、归档、恢复经营数据。
- 订单完成、确认收款、确认履约。
- 商品改价申请、审批、驳回。
- 库存入库、出库、盘点调整。
- 现金流新增 / 修改。
- 导出报表。
- 数据冲突。

### 4.4 编辑提示 / 在线状态

`CloudEditPresences`

```text
WorkspaceId UUID NOT NULL
EntityType TEXT NOT NULL
EntityId UUID NOT NULL
UserId UUID NOT NULL
DisplayName TEXT NOT NULL
ConnectionId TEXT NOT NULL
StartedAt TIMESTAMPTZ NOT NULL
LastHeartbeatAt TIMESTAMPTZ NOT NULL
ExpiresAt TIMESTAMPTZ NOT NULL
PRIMARY KEY (WorkspaceId, EntityType, EntityId, UserId)
```

规则：

- 这是提示，不是硬锁。
- 用户打开编辑面板时上报 `BeginEditing`。
- 用户关闭、离开、保存成功、断开连接时上报 `EndEditing`。
- 服务端每 30 秒清理过期记录。
- UI 显示“某某正在编辑”。
- 真正防覆盖靠 `Revision`。

### 4.5 商品改价申请

`CloudPriceChangeRequests`

```text
Id UUID PRIMARY KEY
WorkspaceId UUID NOT NULL
ProductId UUID NOT NULL
CurrentPrice TEXT NOT NULL
ProposedPrice TEXT NOT NULL
Reason TEXT NULL
Status TEXT NOT NULL
RequestedByUserId UUID NOT NULL
RequestedAt TIMESTAMPTZ NOT NULL
ReviewedByUserId UUID NULL
ReviewedAt TIMESTAMPTZ NULL
ReviewNote TEXT NULL
AppliedProductRevision BIGINT NULL
```

`Status`：

- `Pending`
- `Approved`
- `Rejected`
- `Cancelled`

规则：

- 员工只能创建申请、查看自己的申请状态。
- 管理员可查看全部、通过、驳回。
- 通过时必须在同一事务中更新 `CommerceProducts.DefaultPrice`，写审计日志，广播商品更新事件。
- 员工不能直接修改 `DefaultPrice`。
- 员工永远看不到 `DefaultCost`。

### 4.6 库存调整留痕

`CloudInventoryMovementAudits`

```text
Id UUID PRIMARY KEY
WorkspaceId UUID NOT NULL
InventoryItemId UUID NOT NULL
MovementId UUID NOT NULL
MovementType TEXT NOT NULL
QuantityBefore TEXT NOT NULL
QuantityDelta TEXT NOT NULL
QuantityAfter TEXT NOT NULL
Reason TEXT NULL
IsStocktake BOOLEAN NOT NULL DEFAULT FALSE
ActorUserId UUID NOT NULL
OccurredAt TIMESTAMPTZ NOT NULL
```

规则：

- 员工可入库、出库、盘点调整。
- 每次库存变动都必须记录调整前、调整后、差异、原因、操作人。
- 盘点调整 `IsStocktake = true`。
- 库存数量修改不允许直接覆盖字段，必须通过 movement 服务完成。

### 4.7 导出任务

`CloudExportJobs`

```text
Id UUID PRIMARY KEY
WorkspaceId UUID NOT NULL
RequestedByUserId UUID NOT NULL
Scope TEXT NOT NULL
Status TEXT NOT NULL
FileName TEXT NULL
FilePath TEXT NULL
ErrorMessage TEXT NULL
CreatedAt TIMESTAMPTZ NOT NULL
CompletedAt TIMESTAMPTZ NULL
```

第一版也可同步生成 ZIP 后直接下载，不一定必须后台任务化；但必须写导出审计。

导出包内容：

```text
orders.xlsx
products.xlsx
inventory.xlsx
customers.xlsx
cashflow.xlsx
price-change-requests.xlsx
audit-logs.xlsx
archive.xlsx
```

### 4.8 本地缓存与应急草稿

本地正式客户端新增一个云端缓存库，继续使用本机加密目录，不直接污染现有 Commerce 主库。

建议表：

`CloudCacheEntries`

```text
EntityType TEXT NOT NULL
EntityId TEXT NOT NULL
PayloadJson TEXT NOT NULL
Revision BIGINT NOT NULL
CachedAt TEXT NOT NULL
PRIMARY KEY (EntityType, EntityId)
```

`CloudEmergencyDrafts`

```text
Id TEXT PRIMARY KEY
EntityType TEXT NOT NULL
EntityId TEXT NULL
OperationType TEXT NOT NULL
PayloadJson TEXT NOT NULL
BaseRevision BIGINT NULL
CreatedAt TEXT NOT NULL
Status TEXT NOT NULL
LastSubmitError TEXT NULL
```

断网后允许创建草稿：

- 订单备注草稿。
- 客户备注草稿。
- 待办状态草稿。
- 订单状态草稿。

断网后不允许正式提交：

- 库存调整。
- 现金流新增 / 修改。
- 商品改价申请。
- 导出。

联网恢复后显示“应急草稿待提交”面板，用户逐条确认提交；冲突按 `Revision` 规则处理。

## 5. 权限矩阵

### 5.1 管理员：运营负责人 / 投资方

权限相同：

- 查看全部经营数据。
- 查看成本、利润、毛利率。
- 查看经营建议。
- 新增 / 修改现金流。
- 审批 / 驳回商品改价申请。
- 导出全部分类报表。
- 创建 / 停用员工账号。
- 修改员工角色标签、负责对象和启停状态。
- 查看操作日志。
- 查看归档数据。
- 恢复归档数据。

### 5.2 员工

可做：

- 看订单。
- 新建 / 处理订单。
- 完成订单。
- 确认收款状态。
- 确认履约状态。
- 看商品售价。
- 提交商品改价申请。
- 修改库存、入库、出库、盘点调整。
- 看客户资料。
- 写客户备注。
- 看订单相关收款状态：已收、未收、应收。
- 归档自己负责或自己创建的数据。

不可做：

- 看成本。
- 看利润。
- 看毛利率。
- 看经营建议。
- 新增 / 修改现金流明细。
- 导出数据。
- 真删除数据。
- 修改系统设置。
- 直接改商品价格。

员工可见数据必须由服务端过滤，不能只靠 WPF 隐藏。

员工 DTO 脱敏规则：

- Product：不返回 `DefaultCost`。
- Order：不返回 `Cost`、`GrossProfit`、`GrossMargin`。
- Dashboard：不返回 `GrossProfit`、`CashInflow`、`CashOutflow`、`NetCashFlow`；只返回订单数、完成数、应收/已收/未收、客户数、低库存数。
- CashFlow：不返回现金流明细列表；只返回订单相关收款状态。
- BusinessInsight：接口直接返回 403。
- Export：接口直接返回 403。

## 6. API 设计

所有 API 路径加 `/api` 前缀。

### 6.1 Auth

```text
POST /api/auth/login
POST /api/auth/logout
GET  /api/auth/me
POST /api/auth/change-password
```

登录成功返回：

```text
AccessToken
RefreshToken
User
WorkspaceMembership
ServerTimeUtc
```

### 6.2 Users

```text
GET  /api/users
POST /api/users
PATCH /api/users/{userId}/disable
```

规则：

- 只有管理员可调用。
- 创建员工账号时记录创建人。
- 如果未来创建新的管理员账号，也记录创建人和业务标签。
- 第一版不提供自定义权限修改接口；角色只允许 `Admin / Employee`。

### 6.3 Commerce 查询

```text
GET /api/workspaces/{workspaceId}/dashboard
GET /api/workspaces/{workspaceId}/orders
GET /api/workspaces/{workspaceId}/orders/{orderId}
GET /api/workspaces/{workspaceId}/products
GET /api/workspaces/{workspaceId}/inventory/items
GET /api/workspaces/{workspaceId}/customers
GET /api/workspaces/{workspaceId}/cashflow/summary
GET /api/workspaces/{workspaceId}/cashflow/entries
GET /api/workspaces/{workspaceId}/insights
GET /api/workspaces/{workspaceId}/archive/{entityType}
GET /api/workspaces/{workspaceId}/audit-logs
```

每个返回实体必须带：

```text
Id
Revision
UpdatedAt
UpdatedBy
Lifecycle
```

### 6.4 Commerce 写操作

```text
POST  /api/workspaces/{workspaceId}/orders
PUT   /api/workspaces/{workspaceId}/orders/{orderId}
POST  /api/workspaces/{workspaceId}/orders/{orderId}/complete
POST  /api/workspaces/{workspaceId}/orders/{orderId}/stage
POST  /api/workspaces/{workspaceId}/orders/{orderId}/payment-status
POST  /api/workspaces/{workspaceId}/orders/{orderId}/fulfillment-status

POST  /api/workspaces/{workspaceId}/inventory/movements
POST  /api/workspaces/{workspaceId}/inventory/stocktake-adjustments

POST  /api/workspaces/{workspaceId}/customers
PUT   /api/workspaces/{workspaceId}/customers/{customerId}
POST  /api/workspaces/{workspaceId}/customers/{customerId}/notes

POST  /api/workspaces/{workspaceId}/cashflow/income
POST  /api/workspaces/{workspaceId}/cashflow/expense
POST  /api/workspaces/{workspaceId}/cashflow/receivable
POST  /api/workspaces/{workspaceId}/cashflow/payable
POST  /api/workspaces/{workspaceId}/cashflow/{entryId}/settle
```

所有修改请求必须携带：

```text
ClientRequestId
ExpectedRevision
Reason 可选
```

### 6.5 归档 / 恢复

```text
POST /api/workspaces/{workspaceId}/archive/{entityType}/{entityId}
POST /api/workspaces/{workspaceId}/archive/{entityType}/{entityId}/recover
```

规则：

- 归档不是删除。
- 归档后默认列表不显示。
- 管理员可看归档列表和恢复。
- 员工可归档自己负责或自己创建的数据。
- 服务端必须记录归档原因、操作者、归档前状态。

### 6.6 改价申请

```text
GET  /api/workspaces/{workspaceId}/price-change-requests
POST /api/workspaces/{workspaceId}/price-change-requests
POST /api/workspaces/{workspaceId}/price-change-requests/{requestId}/approve
POST /api/workspaces/{workspaceId}/price-change-requests/{requestId}/reject
```

### 6.7 编辑提示

```text
POST /api/workspaces/{workspaceId}/editing/begin
POST /api/workspaces/{workspaceId}/editing/end
```

### 6.8 导出

```text
POST /api/workspaces/{workspaceId}/exports/business-package
GET  /api/workspaces/{workspaceId}/exports/{exportId}
```

导出只允许管理员。

### 6.9 健康检查

```text
GET /health
GET /health/db
GET /health/version
```

## 7. SignalR 实时事件

Hub 路径：

```text
/hubs/workspace
```

连接后加入 workspace group。

客户端连接时传 JWT，不允许匿名。

事件：

```text
EntityCreated
EntityUpdated
EntityArchived
EntityRecovered
InventoryChanged
DashboardInvalidated
PriceChangeRequestCreated
PriceChangeRequestReviewed
AuditLogCreated
EditingPresenceChanged
UserOnline
UserOffline
EmergencyDraftSubmitted
```

事件 payload 必须包含：

```text
WorkspaceId
EntityType
EntityId
Revision
ActorUserId
ActorDisplayName
OccurredAtUtc
```

客户端收到事件：

- 如果当前列表页包含该实体，刷新该实体或刷新当前页。
- 如果当前正在编辑该实体，显示“数据已变化”提示。
- 如果事件来自自己，可静默更新本地 revision。
- 工作台相关事件触发 Dashboard 刷新。

## 8. 客户端改造方案

### 8.1 组合根改造

在 `App.WorkspaceComposition.cs` 中增加云端装配分支。

正式版默认使用云端装配：

```text
CloudAuthSession
RemoteCommerceClient
RemoteDashboardService
RemoteOrderService
RemoteProductService
RemoteInventoryService
RemoteCustomerService
RemoteCashFlowService
RemoteBusinessInsightService
Remote*Repository adapters
WorkspaceRealtimeClient
CloudCacheStore
EmergencyDraftQueue
```

开发环境允许本地装配，但不做用户可见开关。

配置来源建议：

```text
ORDERLY_CLOUD_BASE_URL
ORDERLY_RUNTIME=Cloud|LocalDev
```

正式安装包内默认写入 Cloud；开发机不配置时可走 LocalDev，方便测试现有 SQLite 逻辑。

### 8.2 Remote 服务适配

`Orderly.Remote` 需要实现这些接口：

- `IDashboardService`
- `IOrderService`
- `IProductService`
- `IInventoryService`
- `ICustomerService`
- `ICashFlowService`
- `IBusinessInsightService`
- `ICommerceOrderRepository`
- `IInventoryItemRepository`
- `ICommerceCustomerRepository`
- `ICashFlowEntryRepository`

原则：

- 页面 VM 尽量少改。
- 现有 Commerce Page VM 能继续依赖接口。
- 远端实现负责 HTTP、权限错误、缓存、冲突、SignalR 刷新。

### 8.3 页面刷新策略

当前 `MainViewModel.CommercePages.cs` 成功加载后会把页面加入 `_loadedCommercePages`，再次进入不刷新。

云端实时协同后必须调整：

- SignalR 收到当前页相关事件时，清除该页 loaded 标记并触发刷新。
- 用户切换页面时，如果上次数据已被事件标脏，重新加载。
- 刷新失败时保留最后有效数据，并显示离线/错误状态。

### 8.4 员工 UI 隐藏

员工登录后：

- 商品页不显示成本列。
- 工作台不显示利润、现金净流。
- 现金流页改成“订单收款状态视图”，不显示现金流明细。
- 经营建议入口隐藏或显示无权限提示。
- 导出入口隐藏。
- 商品改价按钮改成“提交改价申请”。

注意：

- UI 隐藏只是体验层。
- 服务端仍必须 403 或脱敏。

### 8.5 归档 UI

所有列表默认只显示 active。

新增：

- 归档按钮。
- 归档原因输入。
- “归档数据”入口，仅管理员可看全部。
- 员工可看自己归档的数据，是否恢复由管理员决定；第一版恢复仅管理员。

实现时复用 `CommerceEntity.Archive()` / `Recover()` 语义，不使用物理删除。

### 8.6 冲突 UI

当服务端返回 409：

显示：

```text
这条数据已被 {ActorDisplayName} 在 {UpdatedAt} 修改。
你的修改没有覆盖对方内容。
请刷新后重新确认。
```

按钮：

- 刷新
- 取消
- 查看最新数据

第一版不要做自动合并。

## 9. 服务端业务规则

### 9.1 写操作统一流程

每个写接口必须按顺序执行：

1. 验证 JWT。
2. 加载 workspace membership。
3. 检查权限。
4. 校验输入。
5. 读取当前实体和 revision。
6. 校验 `ExpectedRevision`。
7. 执行业务变更。
8. 写 `CloudAuditLogs`。
9. 提交事务。
10. 广播 SignalR 事件。
11. 返回最新实体 DTO。

### 9.2 订单完成

保留现有 `CommerceOrderService.CompleteOrderAsync` 的核心语义：

- 按库存项聚合扣减。
- 库存不足整体失败。
- 订单完成、库存扣减、库存流水、客户统计在一个事务里提交。
- 不能重复扣减。

云端实现可以复用现有算法，但不能依赖 `SqliteConnectionFactory`；需要在 server 侧重写 PostgreSQL 事务实现，或者抽出纯算法后分别落库。

### 9.3 现金流

员工：

- 只能看订单相关的收款状态。
- 不能调用现金流 entries 明细接口。
- 不能新增 / 修改现金流。

管理员：

- 可新增收入、支出、应收、应付。
- 可结算。
- 可导出。

### 9.4 经营建议

第一版仍使用确定性规则，不接 AI。

员工：

- 403。

管理员：

- 可看。
- 可确认已处理 / 忽略，状态变化写审计和实时事件。

## 10. 导出方案

服务端生成 ZIP，里面按类拆 Excel。

文件：

```text
orders.xlsx
products.xlsx
inventory.xlsx
customers.xlsx
cashflow.xlsx
price-change-requests.xlsx
audit-logs.xlsx
archive.xlsx
```

导出要求：

- 只有管理员可导出。
- 员工不可导出。
- 导出动作写审计。
- 每个 Excel 第一行写导出时间、导出人、工作区。
- 金额字段保留两位小数。
- 时间使用北京时间显示，同时保留 UTC 原始列。
- 操作日志单独一个文件，不混入常规业务报表。

## 11. 备份方案

第一版正式可用要求：

- PostgreSQL 每天自动备份。
- 保留 30 天。
- 至少每周做一次恢复演练。
- 数据库不直接暴露公网。

部署建议：

- 同一台阿里云 ECS 上跑 PostgreSQL + Orderly.Server。
- 使用 Docker Compose。
- 每天凌晨执行 `pg_dump -Fc`。
- 备份目录：`/opt/orderly/backups`。
- 保留策略：删除 30 天前备份。
- 同时上传阿里云 OSS 异地备份。
- 每周自动恢复检查一次备份。
- 每月人工做一次恢复演练。

## 12. 测试计划

### 12.1 单元测试

新增覆盖：

- 权限矩阵。
- 员工 DTO 脱敏。
- 归档 / 恢复。
- 改价申请审批。
- 库存调整前后留痕。
- 409 冲突。
- 操作日志写入。
- 导出权限。

### 12.2 服务端集成测试

建议使用 Testcontainers 或测试 PostgreSQL。

覆盖：

- 登录 -> 查询 dashboard。
- 管理员创建员工。
- 员工处理订单。
- 员工确认收款状态。
- 员工提交改价申请。
- 管理员通过改价申请后商品价格生效。
- 员工库存盘点调整后写 movement + audit。
- 员工访问经营建议返回 403。
- 员工导出返回 403。
- 管理员导出 ZIP 包含 8 个 Excel。
- 两个客户端同 revision 修改同一订单，第二个 409。

### 12.3 WPF 集成验收

覆盖：

- 三个账号登录：运营负责人、投资方、员工。
- 运营负责人和投资方看到同样最高权限。
- 员工看不到成本、利润、毛利率。
- 员工看不到经营建议。
- 员工不能导出。
- 员工改价只能提交申请。
- 管理员审批后所有客户端实时刷新。
- 一人打开订单时，另一人看到“正在编辑”提示。
- 断网后能看缓存，不能正式提交。
- 应急草稿联网后可确认提交。

## 13. 推荐施工顺序

### 阶段 1：骨架

1. 新增 `Orderly.Contracts`、`Orderly.Server`、`Orderly.Remote`。
2. 加入解决方案。
3. Server 接 PostgreSQL，完成健康检查。
4. 写 PostgreSQL schema initializer。
5. 写账号、登录、JWT、workspace membership。

验收：

- `dotnet build Orderly.sln -c Debug` 通过。
- `/health` 返回正常。
- 可以创建首个管理员并登录。

### 阶段 2：只读云端经营数据

1. 实现 dashboard、orders、products、inventory、customers、cashflow summary、insights 查询。
2. 实现员工脱敏。
3. 实现 `Orderly.Remote` 只读服务。
4. WPF 正式装配远端服务。

验收：

- 三个账号可登录。
- 三类权限看到不同数据。
- 员工看不到成本利润。

### 阶段 3：写操作与冲突

1. 增加 revision 并发控制。
2. 实现订单状态、完成订单、客户备注、库存 movement。
3. 实现 409 冲突响应。
4. 客户端冲突提示。

验收：

- 第一个提交成功。
- 第二个提交同旧 revision 时失败并提示。
- 库存调整有前后数量留痕。

### 阶段 4：实时协同

1. 增加 SignalR Hub。
2. 实现实体变更广播。
3. 实现编辑提示。
4. 客户端接收事件并刷新相关页。

验收：

- A 改订单，B 实时看到。
- A 正在编辑，B 看到提示。

### 阶段 5：归档、改价申请、操作日志

1. 归档 / 恢复。
2. 改价申请 / 审批。
3. 操作日志页面。

验收：

- 员工可归档自己负责数据。
- 默认列表不显示归档。
- 管理员可恢复。
- 员工提交改价申请，管理员批准后价格生效。

### 阶段 6：离线缓存、应急草稿、导出、备份

1. 云端缓存库。
2. 应急草稿队列。
3. Excel ZIP 导出。
4. Docker Compose 部署。
5. 30 天本机备份 + OSS 异地备份 + 周恢复检查。

验收：

- 断网可读缓存。
- 断网只能写允许范围内的草稿。
- 联网后草稿逐条确认提交。
- 管理员可导出 ZIP。
- 员工导出 403。
- 备份文件按天生成、上传 OSS，并清理 30 天前本机与 OSS 旧文件。

## 14. 不允许做的事

- 不要把 GitHub 当业务数据同步工具。
- 不要让客户端直接连 PostgreSQL。
- 不要把数据库端口开放公网。
- 不要只在 UI 隐藏成本利润，服务端也必须过滤。
- 不要做物理删除。
- 不要让库存数量直接覆盖，必须通过 movement。
- 不要自动合并并发冲突。
- 不要把员工的离线草稿自动静默提交。
- 不要把微信支付、AI、外部平台回写混进本阶段。

## 15. 待人工确认后再细化的点

这些不阻塞第一版施工，但后续 UI 文案需要确认：

- 员工现金流受限视图具体命名：建议叫“收款状态”。
- 归档原因是否必填：建议员工必填，管理员可选。
- 首个正式域名。
- 管理员账号初始用户名。

## 16. 正式开工前强制补充设计

本节覆盖前文中任何较粗或含糊的表述。后续施工必须按本节执行。

### 16.1 本地旧数据一次性导入云端

现有本地 SQLite/SQLCipher 数据不能靠手工复制。必须新增一次性导入工具或导入流程。

推荐实现：

```text
Orderly WPF 管理员入口
    -> 读取当前本机已登录账号的本地 Commerce 数据
    -> 生成导入预检查报告
    -> 管理员确认
    -> 调用云端 Import API
    -> PostgreSQL 事务导入
    -> 写导入审计
```

也可以补一个内部 CLI，但正式用户路径必须能从管理员登录后的 Orderly 里触发。

导入必须分两步：

1. `DryRun`：只检查、只生成报告，不写云端业务表。
2. `Commit`：用户确认后才写 PostgreSQL。

导入报告至少包含：

- 本地数据库永久 `SourceInstanceId`。
- 源数据库路径指纹，不显示完整敏感路径。
- 源工作区数量。
- 商品、库存、客户、订单、订单项、收款记录、现金流、任务、建议数量。
- 可导入数量。
- 疑似重复数量。
- 缺字段或非法数据数量。
- 金额/数量精度异常数量。
- 预计创建的云端工作区。

云端新增表：

`CloudImportBatches`

```text
Id UUID PRIMARY KEY
WorkspaceId UUID NOT NULL
SourceInstanceId UUID NOT NULL
SourceFingerprint TEXT NOT NULL
SourceReportJson TEXT NOT NULL
Status TEXT NOT NULL
RequestedByUserId UUID NOT NULL
DryRunAt TIMESTAMPTZ NOT NULL
CommittedAt TIMESTAMPTZ NULL
RolledBackAt TIMESTAMPTZ NULL
ErrorMessage TEXT NULL
```

`CloudImportEntityMap`

```text
WorkspaceId UUID NOT NULL
SourceInstanceId UUID NOT NULL
EntityType TEXT NOT NULL
SourceLocalEntityId TEXT NOT NULL
TargetEntityId UUID NOT NULL
FirstImportBatchId UUID NOT NULL
LastImportBatchId UUID NOT NULL
CreatedAt TIMESTAMPTZ NOT NULL
UpdatedAt TIMESTAMPTZ NOT NULL
PRIMARY KEY (WorkspaceId, SourceInstanceId, EntityType, SourceLocalEntityId)
```

来源身份规则：

- 每个本地数据库必须有永久 `SourceInstanceId`，首次创建后固定不变，保存在本地安全数据目录。
- `SourceFingerprint` 只用于 `DryRun` 到 `Commit` 之间的数据一致性校验，不作为长期来源身份。
- `Commit` 前必须重新计算并核对 `SourceFingerprint`；如果和 `DryRun` 报告不一致，拒绝 `Commit`，要求重新 `DryRun`。
- `ImportBatchId` 只用于追溯某一次导入历史，不作为长期去重身份。
- 长期去重唯一键必须是 `WorkspaceId + SourceInstanceId + EntityType + SourceLocalEntityId`。

本地 workspace 到云端 workspace 的映射规则：

- 第一版只允许把当前本地账号正在使用的一个本地 workspace 导入当前管理员服务端会话所属的一个云端 workspace。
- 目标云端 `WorkspaceId` 必须从当前管理员服务端会话推导，不能相信客户端传入。
- 第一版不在导入流程中自动创建多个云端 workspace。
- 如果本地存在多个 workspace，导入界面必须让管理员选择一个源 workspace，并明确导入到当前云端 workspace。

重复执行不能重复造数据。稳定键规则用于 DryRun 识别疑似重复和辅助映射，但长期去重以 `CloudImportEntityMap` 的来源唯一键为准：

- Product：优先 `Code`，否则 `Name + ProductType`。
- ProductVariant：`ProductStableKey + Sku/Name`。
- InventoryItem：优先 `Sku`，否则 `Name + ProductStableKey`。
- Customer：优先手机号标准化值，其次 `Name + WeChat/Email`。
- Order：优先 `OrderNo`，否则源本地 `Id` + `OrderedAt` + `Total`。
- OrderItem：`OrderStableKey + 源本地行 Id`。
- PaymentRecord：`OrderStableKey + BusinessKey`，无 BusinessKey 时用源本地 Id。
- CashFlowEntry：优先 `BusinessKey`，否则源本地 Id。

Commit 必须在 PostgreSQL 单事务内完成：

- 目标 `WorkspaceId` 必须从当前管理员服务端会话推导，不能相信客户端传入的 workspace。
- 导入业务数据使用一个 PostgreSQL 事务。
- 任何一类业务写入失败，业务事务整体回滚。
- 业务事务回滚后，再开启独立小事务，把 `CloudImportBatches.Status` 标记为 `Failed`，写失败摘要，并写 `CloudAuditLogs`。
- 不留下半批业务数据。
- 疑似重复但无法自动确认的数据必须阻止 Commit，或要求管理员在导入确认页明确选择“确认合并/跳过”；不能静默合并。

重复 Commit：

- 如果 `CloudImportBatches.Status = Committed`，直接返回上次结果。
- 如果同源稳定键已存在于同 workspace，复用目标记录，不新增重复。

### 16.2 SignalR 只是提醒，不能保证绝对同步

SignalR 不能作为最终同步保证。必须新增服务端连续变化游标。

新增表：

`CloudWorkspaceSyncState`

```text
WorkspaceId UUID PRIMARY KEY
LastSequence BIGINT NOT NULL DEFAULT 0
UpdatedAt TIMESTAMPTZ NOT NULL
```

`CloudChangeLog`

```text
WorkspaceId UUID NOT NULL
Sequence BIGINT NOT NULL
EntityType TEXT NOT NULL
EntityId UUID NULL
Action TEXT NOT NULL
Revision BIGINT NULL
ActorUserId UUID NULL
OccurredAt TIMESTAMPTZ NOT NULL
PayloadHintJson TEXT NULL
PRIMARY KEY (WorkspaceId, Sequence)
```

规则：

- 不能使用 PostgreSQL 全局 `BIGSERIAL` 直接当客户端补同步游标，因为 sequence 分配顺序和事务提交顺序可能不一致。
- 每次业务写入必须在同一个 PostgreSQL 事务内 `SELECT ... FOR UPDATE` 锁定该 workspace 的 `CloudWorkspaceSyncState` 行。
- 服务端在该锁内把 `LastSequence + 1` 分配给本次变化，更新 `CloudWorkspaceSyncState.LastSequence`，再写 `CloudChangeLog`。
- 业务数据、`CloudWorkspaceSyncState`、`CloudChangeLog` 必须同一事务提交。
- `Sequence` 是 workspace 内已提交的连续补同步游标，客户端只保存最后处理到的 sequence。
- SignalR 事件必须带 `Sequence`，但它只是提醒客户端“有变化”。
- 客户端收到 SignalR 后调用补拉接口，不直接信任 SignalR payload 当最终数据。

新增接口：

```text
POST /api/workspaces/{workspaceId}/sync/snapshots
GET /api/workspaces/{workspaceId}/sync/snapshots/{snapshotToken}?entityType=orders&page=1&pageSize=100
GET /api/workspaces/{workspaceId}/sync/changes?afterSequence=123&limit=500
```

首次登录：

1. 调 `POST /sync/snapshots`，服务端读取当前 `CloudWorkspaceSyncState.LastSequence`，生成 `SnapshotToken` 和固定 `SnapshotSequence`。
2. 客户端后续每一页 snapshot 都必须带同一个 `SnapshotToken`。
3. 服务端所有 snapshot 分页查询都以同一个 `SnapshotSequence` 为准。
4. 客户端分页写入本地云缓存。
5. 拉完所有页后，再从 `SnapshotSequence` 开始补拉 changes。
6. 补拉完成后保存 `LastSeenSequence`。

重连后：

1. 读取本地 `LastSeenSequence`。
2. 调 `/sync/changes?afterSequence=LastSeenSequence`。
3. 按 sequence 顺序补拉实体最新值或标记归档。
4. 成功后推进本地 `LastSeenSequence`。

如果客户端落后太多，服务端可返回 `FullResyncRequired`，客户端重新执行 snapshot。

Snapshot 一致性规则：

- 所有云端 Commerce 行额外维护 `LastChangeSequence BIGINT NOT NULL`。
- Snapshot 分页只返回 `LastChangeSequence <= SnapshotSequence` 的当前行。
- 如果某行在 snapshot 期间被更新或归档，它可以不出现在 snapshot 页里，但后续 `changes > SnapshotSequence` 必须把最新更新或归档事件补给客户端。
- `SnapshotToken` 默认 30 分钟过期，过期后客户端必须重新创建 snapshot。
- `CloudChangeLog` 至少保留 30 天；如果 `afterSequence` 早于保留窗口，服务端返回 `FullResyncRequired`。

FullResync 本地缓存规则：

- FullResync 时 snapshot 先写入该 workspace 的 staging cache，不直接覆盖正式缓存。
- 全部分页拉取成功，并校验页数、实体计数、`SnapshotToken` 和 `SnapshotSequence` 后，原子替换该 workspace 的正式缓存。
- 替换完成后，再从 `SnapshotSequence` 开始补拉 changes。
- 如果使用同库缓存，也可给每条缓存记录写 `SeenInSnapshotToken`；snapshot 完成后删除本次未出现的旧缓存。
- 必须确保已归档、已删除或当前账号已失去权限的数据不会残留在客户端正式缓存里。

### 16.3 `ClientRequestId` 必须真正防重复执行

所有写接口必须做服务端幂等，不能只靠客户端按钮禁用。

新增表：

`CloudIdempotencyKeys`

```text
WorkspaceId UUID NOT NULL
UserId UUID NOT NULL
Action TEXT NOT NULL
ClientRequestId TEXT NOT NULL
RequestHash TEXT NOT NULL
Status TEXT NOT NULL
ResponseStatusCode INTEGER NULL
ResponseBodyJson TEXT NULL
ResourceType TEXT NULL
ResourceId UUID NULL
CreatedAt TIMESTAMPTZ NOT NULL
CompletedAt TIMESTAMPTZ NULL
PRIMARY KEY (WorkspaceId, UserId, Action, ClientRequestId)
```

执行规则：

1. 写接口进来后先计算 `RequestHash`。
2. 在同一个 PostgreSQL 事务内尝试插入 `CloudIdempotencyKeys`。
3. 插入成功后，在同一个事务内执行业务写入、审计、`CloudChangeLog`。
4. 在同一个事务内写入可重放的 `Completed` 响应。
5. 一次性提交事务。
6. 同 `ClientRequestId` 的并发请求由唯一键阻塞；首次请求成功提交后，重复请求读取 `Completed` 并返回第一次结果。
7. 首次请求如果回滚，幂等记录也一起回滚，后续请求可安全重试。
8. `ResponseBodyJson` 必须保存可重放的最终响应；若响应过大，则必须保存 `ResourceType + ResourceId`，重复请求时重新读取资源生成同等响应。
9. 异步导出只需在事务内创建 `CloudExportJobs`，幂等重复请求返回同一个 `ExportJobId`，不要把异步处理状态混入同步幂等逻辑。
10. 相同 `(WorkspaceId, UserId, Action, ClientRequestId)` 再来时：
   - RequestHash 一致：直接返回第一次结果。
   - RequestHash 不一致：返回 409，提示同一个 ClientRequestId 被错误复用。

必须覆盖：

- 完成订单。
- 确认收款状态。
- 确认履约状态。
- 库存入库 / 出库 / 盘点调整。
- 现金流新增 / 结算。
- 改价申请 / 审批 / 驳回。
- 归档 / 恢复。
- 导出创建。

### 16.4 库存扣减必须靠 PostgreSQL 事务硬保证

库存不能只靠客户端检查，也不能只靠 Revision。

完成订单必须在同一个 PostgreSQL 事务中完成：

1. 校验幂等键。
2. `SELECT ... FOR UPDATE` 锁住订单。
3. 读取订单项。
4. 按 `InventoryItemId` 聚合需要扣减数量。
5. 将涉及的 `InventoryItemId` 按 UUID 字符串升序排序。
6. 按固定排序逐行 `SELECT ... FOR UPDATE` 锁住库存行，避免不同请求反向加锁造成死锁。
7. 检查库存是否足够。
8. 库存不足：整笔订单失败，事务回滚。
9. 库存足够：扣库存。
10. 写 `CommerceInventoryMovements`。
11. 写 `CloudInventoryMovementAudits`。
12. 更新订单完成状态。
13. 更新客户统计。
14. 写操作日志。
15. 写 change log。
16. 提交事务。

扣库存 SQL 必须保证不扣成负数：

```text
UPDATE CommerceInventoryItems
SET QuantityAvailable = QuantityAvailable - @required,
    Revision = Revision + 1,
    UpdatedAt = @now,
    UpdatedByUserId = @actor
WHERE Id = @inventoryItemId
  AND QuantityAvailable >= @required
```

如果影响行数不是 1，视为库存不足或并发冲突，整笔订单失败。

不允许任何接口直接覆盖 `QuantityAvailable`；所有库存变化只能通过库存流水。

死锁和序列化失败处理：

- 遇到 PostgreSQL `deadlock_detected` 或 `serialization_failure` 时，服务端最多安全重试 2 次。
- 重试必须复用同一个 `ClientRequestId`，不能生成新的幂等键。
- 订单完成状态、库存流水、库存审计、客户统计、审计日志、`CloudChangeLog` 仍必须保持单事务提交。

### 16.5 登录、刷新、停用和密码重置

必须补齐 refresh token 规则。

新增表：

`CloudRefreshTokens`

```text
Id UUID PRIMARY KEY
UserId UUID NOT NULL
TokenFamilyId UUID NOT NULL
TokenHash TEXT NOT NULL
CreatedAt TIMESTAMPTZ NOT NULL
ExpiresAt TIMESTAMPTZ NOT NULL
RevokedAt TIMESTAMPTZ NULL
RevokedReason TEXT NULL
ReplacedByTokenId UUID NULL
```

`CloudUsers` 增加：

```text
TokenVersion INTEGER NOT NULL DEFAULT 1
PasswordChangedAt TIMESTAMPTZ NULL
FailedLoginCount INTEGER NOT NULL DEFAULT 0
LockedUntil TIMESTAMPTZ NULL
```

接口：

```text
POST /api/auth/refresh
POST /api/auth/reset-password
POST /api/auth/logout-all
```

规则：

- AccessToken 短有效期，建议 15 分钟。
- RefreshToken 长有效期，建议 30 天。
- RefreshToken 只保存 hash，不保存明文。
- RefreshToken 每次刷新都轮换，旧 token 立刻作废。
- RefreshToken 轮换必须检测旧 token 重放：已被替换或已撤销的 refresh token 再次出现时，按 `TokenFamilyId` 撤销整条会话链，并要求重新登录。
- 改密码后 `TokenVersion + 1`，旧 access token 和 refresh token 全部失效。
- 停用员工后该员工所有登录立刻失效，并通过 SignalR 通知客户端退出。
- 每次 API 请求和 SignalR Hub 鉴权时，都必须从服务端读取或校验用户 `IsEnabled` 与当前 `TokenVersion`；旧 AccessToken 即使未过 15 分钟，也必须被拒绝。
- 客户端 token 不明文保存，Windows 下使用 DPAPI / Windows Credential Manager。
- 不能停用最后一个 Admin。
- Admin 不能把自己停用到系统无人可管。
- 密码重置只能由 Admin 发起，写审计日志，不记录新密码。
- 云端登录必须有失败限流/锁定策略：建议同账号连续 5 次失败锁定 15 分钟，并写安全审计。
- `PasswordHash` 必须使用明确安全算法：推荐 Argon2id，参数不少于 64 MiB memory、3 iterations、parallelism 1、16 字节 salt、32 字节 hash，PHC 字符串格式存储；不允许自定义弱哈希。

### 16.6 权限最终定义

第一版固定角色，不做细粒度权限配置。

固定角色：

- `Admin`：运营负责人、投资方。
- `Employee`：员工。

第一版不落库 `PermissionsJson`，也不提供自定义权限编辑。所有权限由服务端固定策略判断。

“员工可以归档自己负责的数据”的负责规则：

第一版只有以下实体支持员工按“负责”归档：

- `CommerceOrders`
- `CommerceCustomers`
- `CommerceBusinessTasks`

这些实体增加：

```text
AssignedToUserId UUID NULL
CreatedByUserId UUID NULL
```

员工允许归档的条件：

```text
CurrentUser.Role == Employee
AND (
    Entity.CreatedByUserId == CurrentUser.Id
    OR Entity.AssignedToUserId == CurrentUser.Id
)
```

如果实体暂时没有 `AssignedToUserId`，则只允许归档自己创建的数据。

管理员可归档和恢复全部。

员工第一版不能归档商品、库存项、库存流水、现金流、经营建议、系统配置实体。

客户端隐藏按钮不算权限控制；所有接口必须由服务端重新判断。

### 16.7 金额、数量、时间和历史价格

PostgreSQL 不能把金额和库存数量存成 TEXT。

金额字段：

```text
NUMERIC(18, 2)
```

适用：

- 商品售价。
- 商品成本。
- 订单金额。
- 订单成本。
- 利润。
- 收款金额。
- 现金流金额。

数量字段：

```text
NUMERIC(18, 4)
```

适用：

- 库存数量。
- 订单项数量。
- 库存流水数量。
- 覆盖天数计算输入。

舍入规则：

- 金额：服务端统一保留 2 位。
- 数量：服务端统一保留 4 位。
- 中间计算使用 `decimal`。
- 最终落库使用 `MidpointRounding.AwayFromZero`。

时间规则：

- 服务端统一存 UTC：`TIMESTAMPTZ`。
- API 返回 UTC。
- WPF 客户端显示北京时间。

历史价格快照规则：

- `OrderItem.UnitPrice`、`OrderItem.UnitCost`、`OrderItem.LineTotal` 必须在创建订单项时写入并固定。
- 商品改价只影响未来订单。
- 历史订单必须保留成交时的 `OrderItem.UnitPrice`、`UnitCost`、`LineTotal`。
- 后续商品默认价格变化不影响已有订单项，除非有明确、受审计的订单项修改操作。
- 审批商品改价时只能更新 `CommerceProducts.DefaultPrice`。
- 不允许批量重算历史订单价格。

### 16.8 数据库迁移、备份、恢复、回滚

不要只靠 schema initializer。服务端必须有正式数据库迁移机制。

推荐使用：

```text
DbUp 或等价 .NET migration runner
```

要求：

- 每个数据库变更是一份有序 SQL migration。
- 数据库保存已执行版本。
- 应用启动时可检查 migration 状态。
- 生产环境执行 migration 前必须自动备份。
- migration 失败时应用不得继续启动为可写状态。

上线前迁移流程：

1. 服务进入维护状态或停止写入。
2. 执行 `pg_dump -Fc` 本机备份。
3. 上传备份到 OSS。
4. 校验备份文件存在且大小正常。
5. 执行 migration。
6. migration 成功后启动新版本。
7. migration 失败则保留旧版本，数据库从备份恢复或保持原状态。

回滚策略：

- 第一版禁止破坏性 migration。
- 字段删除、表删除、不可逆数据改写都不能直接上生产。
- 应用版本回滚优先要求数据库向后兼容。
- 如果数据库已执行不可兼容变更，只能通过备份恢复，不允许强行跑旧应用。

备份策略：

- 每天本机 `pg_dump -Fc`。
- 每天上传阿里云 OSS。
- 本机和 OSS 都保留 30 天。
- 每周自动拉取最近备份恢复到临时数据库并运行健康检查。
- 每月人工做一次恢复演练。

### 16.9 导出、审计、列表接口运行细节

导出：

- 导出文件默认保留 24 小时。
- 下载导出文件必须再次鉴权。
- 不生成公开下载链接。
- 导出失败最多重试 2 次。
- 导出目录设置磁盘上限，建议 2GB 或磁盘 10%，先到为准。
- 超过上限拒绝新导出并提示管理员清理。
- 导出成功、失败、下载都写审计。

审计：

- 禁止记录密码。
- 禁止记录 token。
- 禁止记录 refresh token。
- 禁止记录数据库连接串。
- 禁止记录明文密钥。
- 审计记录只存操作摘要、脱敏前后差异、业务实体 ID。

列表接口：

- 所有列表必须分页，不能一次性全拉。
- 默认 `pageSize = 50`。
- 最大 `pageSize = 200`。
- 必须支持服务端筛选。
- 必须支持白名单排序字段。
- 不允许客户端传任意 SQL 字段名。

统一列表响应：

```text
Items
Page
PageSize
TotalCount
Sort
FilterSummary
LatestSequence
```

客户端首次同步如果需要全量数据，也必须分页拉取 snapshot。
