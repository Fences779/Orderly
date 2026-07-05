# Orderly

## 项目定位

Orderly 是一款本地优先的 PC 端经营管理系统，面向小微商家，覆盖成交销售、订单、库存、客户、现金流、数据分析与经营建议七大能力。

它把日常经营流程收口为可追踪、可回看、可执行的桌面工作流：所有业务数据均保存在本机，默认不依赖云端。

主线技术栈为 `WPF + .NET 8 + SQLite/SQLCipher`，围绕一套行业无关的通用领域模型（Universal Domain Model）构建，使 Orderly 能够表达任意小微商家的经营数据，而无需行业专属字段。

## 七大能力

- 成交销售：记录与推进成交，沉淀经营动作
- 订单：订单创建、计算与销售 / 收款 / 履约三维阶段推进
- 库存：出入库、可用量、覆盖天数与补货提示
- 客户：客户档案、联系人、RFM 指标与复购提醒
- 现金流：收入、支出、应收应付与现金流健康评估
- 数据分析：工作台指标与近 7 日经营趋势
- 经营建议：基于本地数据的确定性规则，生成面向小微商家的经营洞察

## 技术栈

- .NET 8
- WPF
- C#
- SQLite / SQLCipher（本地全库加密）
- MVVM
- 本地优先数据存储

## 数据位置

- 应用数据根目录：`D:\OrderlyData`
- 启动器数据库、多账号工作区数据库与身份材料均存放在该单一目录下
- 如检测到来自旧安装目录的本地数据，将以中性的「legacy local data migration」方式处理，不会删除或覆盖现有用户数据

## 工程结构

- `Orderly.sln`：解决方案入口
- `src/Orderly.App`：桌面应用入口、View、ViewModel、交互层
- `src/Orderly.Core`：核心模型、服务接口、`Commerce` 通用领域模型
- `src/Orderly.Data`：SQLite/SQLCipher 数据访问、仓储实现、通用服务实现与导入导出
- `src/Orderly.Infrastructure`：桌面基础设施适配
- `tests/Orderly.Tests`：属性测试、示例 / 单元测试、集成测试与受限词回归测试
- `docs/`：通用模型、数据模型、模板系统与发布验收文档
- `tools/qa/`：QA 数据维护、smoke、regression 脚本

## 文档

- 通用领域模型：`docs/ORDERLY_UNIVERSAL_MODEL.md`
- 数据模型与数据层：`docs/ORDERLY_DATA_MODEL.md`
- 模板与自定义系统：`docs/ORDERLY_TEMPLATE_SYSTEM.md`
- 发布前验收清单：`docs/ORDERLY_RELEASE_CHECKLIST.md`

## 快速运行

1. 安装 .NET 8 SDK。
2. 打开 `Orderly.sln`。
3. 还原依赖：`dotnet restore Orderly.sln`
4. 构建：`dotnet build Orderly.sln -c Debug`
5. 启动：
   - 直接在 IDE 中启动 `Orderly.App`
   - 普通启动：`start-orderly.bat`
   - 管理员自动登录启动（QA 模式）：`start-qa.bat`
   - 管理员自动登录热更新启动：`dev-watch-qa.bat`
   - 等价命令：`dotnet run --project .\src\Orderly.App\Orderly.App.csproj`

## 发布前验收

- 发布前统一入口：`docs/ORDERLY_RELEASE_CHECKLIST.md`
- QA 脚本入口：`tools/qa/README.md`
- 日常开发收口：默认只验证本次修改点相关链条，不默认跑整套 QA 回归

发布前至少执行以下门禁，全部通过方可放行：

```powershell
dotnet build Orderly.sln -c Debug
dotnet test
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-universal-regression.ps1
git status --short
```

门禁覆盖：构建、测试、受限词扫描零命中、P0 安全零回归、QA 脚本终端成功，以及核心业务流（Core_Flow）端到端可运行。

## 关键边界

- 主线以 WPF + SQLite/SQLCipher 通用经营系统为准
- 云端优先：正式版默认连接中心 Orderly.Server，本地 SQLite/SQLCipher 仅保留登录缓存、最近数据缓存、断网只读与开发调试数据
- `miniprogram/` 与 `cloudfunctions/` 不属于本主线交付范围

## 文件状态
- `src/Orderly.App/ViewModels/MainViewModel.CloudImportCommands.cs`、`src/Orderly.Data/Cloud/LocalImportPackageBuilder.cs`、`src/Orderly.App/App.WorkspaceComposition.cs`、`src/Orderly.Data/Cloud/CloudCacheStore.cs`、`src/Orderly.Server/Services/CommerceCommandService*.cs`、`src/Orderly.Server/Controllers/CommerceReadController.cs`、`src/Orderly.Server/Services/CommerceListQueryHelper.cs`、`src/Orderly.App/Views/Sections/SettingsTabDataSecurity.xaml`、`src/Orderly.App/ViewModels/MainViewModel.SettingsP0.Commands.cs`（新增/已修改）：收口团队云端方案 P0 验收缺口。本地旧数据导入云端 Commit 前重新计算来源指纹，若 DryRun 后本地数据变化则拒绝提交；云模式缓存由内存实现切换为 SQLCipher 工作库 `CloudCacheStore`，全量同步 `ReplaceAllAsync` 先写入 staging 表再原子替换正式缓存，并删除旧的 `SqliteCloudCacheStore` 与 `Orderly.Remote/Cache/CloudCacheStore` 内存实现；服务端事务命令统一通过 `NotificationCollector` 收集 SignalR 通知，待 `CommitAsync` 成功后再广播，覆盖订单完成/库存变动与改价审批；Commerce 列表接口补齐白名单排序、关键字搜索与阶段/状态筛选，并返回 `Sort`/`FilterSummary`；设置页数据安全卡片的硬编码色值/字号/圆角/边距改为命名资源，UI 契约测试通过；数据库优化增加 `busy_timeout` 与占用冲突提示，完成后刷新健康状态。
- `src/Orderly.Server/Services/DatabaseRestoreDrillService.cs`、`src/Orderly.Server/Services/BackupBackgroundService.cs`、`src/Orderly.Server/Services/BackupHealthState.cs`、`src/Orderly.Server/Models/ServerOptions.cs`、`src/Orderly.Server/Program.cs`、`docker-compose.yml`、`deploy/orderly.env.example`、`tools/ops/run-cloud-backup-restore-drill.ps1`、`README.md`（新增/已修改）：备份恢复继续收口。每日备份成功后会按配置间隔自动把最新 `.dump` 恢复到临时库，执行基础查询后删除临时库；恢复演练 Passed/Failed/Running、错误原因和最近演练时间都会写入健康状态；`/health/backups` 能区分无本地备份、演练缺失、失败、进行中、过期和健康；手工恢复演练脚本失败时也会写入失败状态。
- `src/Orderly.Remote/Sync/RemoteWorkspaceSyncClient.cs`、`src/Orderly.App/ViewModels/MainViewModel.CloudState.cs`、`src/Orderly.App/App.WorkspaceComposition.cs`、`README.md`（已修改）：多人同步继续收口。客户端同步状态会记录最近尝试、最近成功、最近全量重同步、连续失败次数和最后错误；断网或超时不会中断后台同步循环，会继续使用本地缓存并自动重试；同步恢复成功会清掉错误状态并刷新页面；离线草稿提交器一旦确认网络恢复，会主动触发一次补同步。
- `src/Orderly.Server/Services/CloudImportService.cs`、`src/Orderly.Server/Services/BackupHealthState.cs`、`src/Orderly.Server/Models/ServerOptions.cs`、`src/Orderly.Server/Program.cs`、`src/Orderly.Server/Migrations/0001_InitialSchema.sql`、`src/Orderly.Server/Migrations/0004_CloudImportReliability.sql`、`docker-compose.yml`、`deploy/orderly.env.example`、`README.md`（新增/已修改）：旧数据导入云端继续收口。Commit 对已成功批次改为直接返回上次结果，不会重复写入业务数据；真正提交前默认先生成 `orderly_pre_import_*.dump` 备份并写入备份健康状态；成功和失败结果都会保存到导入批次表，失败也会写审计，方便页面重复查看和后台排查。
- `src/Orderly.Server/Services/ExportService.cs`、`src/Orderly.Server/Controllers/ExportController.cs`、`src/Orderly.Server/Models/ServerOptions.cs`、`src/Orderly.Server/Program.cs`、`src/Orderly.Contracts/Commerce/CloudExportJobDto.cs`、`src/Orderly.Server/Migrations/0001_InitialSchema.sql`、`src/Orderly.Server/Migrations/0003_ExportReliability.sql`、`Dockerfile`、`docker-compose.yml`、`deploy/orderly.env.example`、`README.md`（新增/已修改）：导出可靠性继续收口。业务导出改用可配置持久目录 `/opt/orderly/exports` 作为本地 fallback，不再依赖系统临时目录；导出任务记录重试次数和最近尝试时间，后台最多重试 2 次；成功导出文件默认保留 24 小时并清理过期文件；本地导出目录默认 2GB 容量上限，超限会拒绝新导出；导出创建、成功、失败和下载都会写入审计日志。
- `src/Orderly.App/Views/LoginOwnerCreatePanel.xaml`、`src/Orderly.App/Views/LoginOwnerCreatePanel.xaml.cs`、`README.md`（已修改）：初始化系统的 Recovery Key 弹窗新增“复制密钥”按钮，点击后只把当前显示的密钥复制到系统剪贴板，并在弹窗内显示复制结果提示；账号创建、密钥生成、确认进入系统逻辑保持不变。
- `src/Orderly.App/Orderly.App.csproj`、`src/Orderly.App/Views/MainWindow.xaml`、`dev-watch-qa.bat`、`scripts/create-desktop-shortcuts.ps1`、`README.md`（已修改）：开发版默认版本号对齐为正式版 `v0.1.3` 的下一小版本 `0.1.4`；桌面“Orderly开发模式”指向隔离 QA 热更新入口；主窗口底部不再显示“已加载 X 个客户、X 个订单”等状态栏文字。
- `src/Orderly.Data/Sqlite/CloudCacheSchemaInitializer.cs`、`src/Orderly.Data/Cloud/LocalImportPackageBuilder.cs`、`src/Orderly.Data/Cloud/SqliteCloudCacheStore.cs`、`src/Orderly.Remote/Cache/CloudCacheStore.cs`、`src/Orderly.Remote/Sync/RemoteWorkspaceSyncClient.cs`、`src/Orderly.Remote/Clients/RemoteCommerceClient.cs`、`src/Orderly.Contracts/Offline/IEmergencyDraftSubmitter.cs`、`src/Orderly.Remote/Offline/RemoteEmergencyDraftSubmitter.cs`、`src/Orderly.Remote/Services/RemoteOrderService.cs`、`src/Orderly.Remote/Services/RemoteCustomerService.cs`、`src/Orderly.Remote/Services/RemoteBusinessTaskService.cs`、`src/Orderly.App/ViewModels/MainViewModel.CloudState.cs`、`src/Orderly.App/Views/Sections/SettingsTabDataAudit.xaml`、`src/Orderly.App/App.WorkspaceComposition.cs`、`src/Orderly.Server/Data/MigrationRunner.cs`、`src/Orderly.Server/Services/BackupHealthState.cs`、`src/Orderly.Server/Services/BackupBackgroundService.cs`、`src/Orderly.Server/Models/ServerOptions.cs`、`src/Orderly.Server/Program.cs`、`docker-compose.yml`、`deploy/orderly.env.example`、`tools/ops/run-cloud-backup-restore-drill.ps1`、`README.md`（新增/已修改）：继续收口云端未达标点。旧数据导入来源身份改为本地 SQLCipher 状态表永久保存，不再按数据库路径推导；客户端同步改为有变更时只刷新受影响实体与列表，FullResync 使用 staging 后替换正式缓存，并在状态栏显示补同步/失败/全量重同步；在线保存遇到云端版本冲突时解析服务端提示，不再把技术 JSON 直接给用户；离线草稿不再后台自动提交，设置页新增草稿列表、刷新、提交选中、放弃选中，提交前先比对云端最新版本，离线保存提示也改为“联网后到设置页确认”；服务端迁移前会先生成 `pg_dump` 备份，失败则阻止迁移，并新增 `/health/backups` 暴露最近本地备份、OSS 上传、迁移前备份与恢复演练状态。
- `src/Orderly.Server/Services/WorkspaceSyncQueryService.cs`、`src/Orderly.Server/Services/CloudAuthService.cs`、`src/Orderly.Server/Controllers/UsersController.cs`、`src/Orderly.Server/Migrations/0002_LoginFailures.sql`、`src/Orderly.Contracts/Auth/CloudLoginFailureDto.cs`、`src/Orderly.Remote/Sync/RemoteWorkspaceSyncClient.cs`、`src/Orderly.Contracts/Offline/ICloudCacheStore.cs`、`src/Orderly.Data/Cloud/SqliteCloudCacheStore.cs`、`src/Orderly.Remote/Cache/CloudCacheStore.cs`、`src/Orderly.Data/Cloud/LocalImportPackageBuilder.cs`、`src/Orderly.App/ViewModels/MainViewModel.CloudImport*.cs`、`src/Orderly.App/Views/Sections/SettingsTabDataAudit.xaml`、`src/Orderly.App/App.WorkspaceComposition.cs`、`tools/ops/run-cloud-backup-restore-drill.ps1`、`tools/ops/test-orderly-cloud-deploy.ps1`、`README.md`（新增/已修改）：云端 8 个缺口继续收口。员工通过同步接口不再能拉取现金流/经营洞察敏感数据，未知用户名登录失败也会落库并提供管理员查询入口；客户端云模式启动后台快照/增量同步，离线缓存按整批替换；客户备注和任务状态离线草稿接入安全队列，库存/现金流仍拒绝离线自动提交；设置页新增本地旧数据导入云端的预检查与确认导入入口；新建远程订单允许先建基础订单；补齐备份恢复演练和 Docker/Caddy 部署包检查脚本。
- `Dockerfile`、`.dockerignore`、`docker-compose.yml`、`deploy/Caddyfile`、`deploy/orderly.env.example`、`README.md`（新增/已修改）：云端第 8 个缺口已收口。仓库已补齐服务端 Docker 镜像、PostgreSQL、Caddy HTTPS 反代和上线环境变量样例；容器内已安装 `pg_dump` 所需 PostgreSQL 客户端，备份目录挂载到 `/opt/orderly/backups`，上线前复制 `deploy/orderly.env.example` 为根目录 `.env` 并替换域名、密码、JWT key、OSS 配置即可启动。
- `src/Orderly.Server/Services/DatabaseBackupService.cs`、`src/Orderly.Server/Services/BackupBackgroundService.cs`、`src/Orderly.Server/Models/ServerOptions.cs`、`src/Orderly.Server/Program.cs`、`README.md`（已修改）：云端第 7 个缺口已收口。服务端备份改为 `pg_dump -Fc` 自定义格式 `.dump` 文件；本机默认保留目录为 `/opt/orderly/backups`，可用 `ORDERLY_LOCAL_BACKUP_DIR` 覆盖；每天备份后本机文件不再立即删除，OSS 上传和本机目录都会按 `BackupRetentionDays` 清理旧备份，并兼容清理历史 `.sql.gz` 文件。
- `src/Orderly.Server/Services/ExportService.cs`、`src/Orderly.Server/Controllers/ExportController.cs`、`src/Orderly.Contracts/Commerce/CloudExportJobDto.cs`、`README.md`（已修改）：云端第 6 个缺口已收口。业务导出 ZIP 现在包含订单、客户、商品、库存、现金流、改价申请、审计日志、归档记录 8 个 Excel 文件；导出任务的 `FileName` 存真实 zip 文件名，`FilePath` 存 OSS key 或本地落盘路径，`DownloadUrl` 只返回下载 API；下载接口按 `FilePath` 取文件，本地 fallback 还会限制路径必须在导出目录内。
- `src/Orderly.Server/Controllers/EmergencyDraftController.cs`、`README.md`（已修改）：云端第 5 个缺口已收口。离线应急草稿仍只允许客户备注、订单备注、订单阶段、业务任务状态 4 类；服务端提交入口现在会当场校验允许类型必须带有效目标实体 Id，非法或格式错误直接返回明确 400，避免用户以为草稿已提交、实际后台才失败。
- `src/Orderly.Server/Services/WorkspaceSyncQueryService.cs`、`src/Orderly.Server/Controllers/SyncController.cs`、`README.md`（已修改）：云端第 4 个缺口已收口。Snapshot token 改为 30 分钟过期；同一个 token 可按实体分页拉取，分页查询固定在创建 token 时的 `SnapshotSequence`，只返回 `LastChangeSequence <= SnapshotSequence` 的数据；`changes` 支持文档里的 `limit` 参数，并在本地序号太旧、超出 30 天保留窗口或变更日志缺口时返回 `FullResyncRequired`；业务任务也补齐 snapshot DTO 映射。
- `src/Orderly.Server/Services/CloudImportService.cs`、`README.md`（已修改）：云端第 3 个缺口已收口。本地旧数据导入的 DryRun 现在只生成批次报告和问题清单，不再提前写正式导入映射；Commit 会先做一次无副作用校验，发现缺失本地 ID、订单明细找不到订单、付款记录找不到订单/现金流等问题时直接失败，不写业务数据；重复导入时正确区分已有映射和新增记录数量。
- `src/Orderly.Server/Services/CloudAuthService.cs`、`src/Orderly.Server/Controllers/UsersController.cs`、`src/Orderly.Server/Hubs/WorkspaceHub.cs`、`src/Orderly.Server/Controllers/PriceChangeController.cs`、`src/Orderly.App/ViewModels/Pages/ProductsPageViewModel.cs`、`WorkbenchPageViewModel.cs`、`src/Orderly.App/Views/Sections/ProductsView.xaml`、`WorkbenchView.xaml`、`src/Orderly.App/Helpers/BindingProxy.cs`、`tests/Orderly.Tests/Ui/ProductsViewContractTests.cs`、`WorkbenchViewContractTests.cs`（新增/已修改）：云端第 1 个缺口已收口。管理员可通过服务端接口重置成员密码；登录失败、账号创建、禁用与重置密码进入审计；refresh token 被重复使用时整组会话作废并提升 token 版本；SignalR 只能加入自己 token 所属工作区；店员只能看自己的改价申请；WPF 店员视角隐藏商品成本、毛利、净现金流与趋势现金流列。
- `src/Orderly.Contracts/Commerce/OrderNoteCommand.cs`、`BusinessTaskStatusCommand.cs`、`UpdateBusinessTaskStatusCommand.cs`、`CloudBusinessTaskDto.cs`，`src/Orderly.Server/Services/CommerceCommandService.BusinessTasks.cs`、`CommerceCommandService.Orders.cs`、`EmergencyDraftProcessor.cs`，`src/Orderly.Server/Controllers/BusinessTasksController.cs`、`CommerceWriteController.cs`、`CommerceReadController.cs`，`src/Orderly.Server/Mapping/CommerceDtoMapper.cs`，`src/Orderly.Remote/Services/RemoteBusinessTaskService.cs`、`RemoteOrderService.cs`、`RemoteCashFlowService.cs`、`RemoteInventoryService.cs`、`RemoteCustomerService.cs`、`RemoteEntityMapper.cs`、`RemoteImportService.cs`（新增/已修改）：Stage 3 完成，收紧应急草稿规则与冲突离线处理。`EmergencyDraftAllowedOperations` 已允许 `order/note` 与 `businessTask/status`；服务端补齐 `AddOrderNoteAsync` 与 `UpdateBusinessTaskStatusAsync` 命令处理器，`EmergencyDraftProcessor` 支持这两类后台重放并继续拒绝库存/现金流/改价/导出等不允许类型；`CommerceReadController` 补齐业务任务列表与单条 GET 端点及映射；客户端新增 `RemoteBusinessTaskService`，离线时把任务状态保存为应急草稿，`RemoteOrderService` 支持订单备注与阶段离线草稿，`RemoteCashFlowService`/`RemoteInventoryService` 明确拒绝库存与现金流离线保存，`RemoteCustomerService` 支持客户备注离线草稿；同时修复 `TaskStatus` 命名冲突、`CustomerNoteCommand` 字段映射、`RemoteImportService` 可空返回等编译问题。
- `src/Orderly.Contracts`、`src/Orderly.Server`、`src/Orderly.Remote`、`src/Orderly.Data`、`Orderly.sln`、`src/Orderly.App/App.WorkspaceComposition.cs`、`src/Orderly.App/App.xaml.cs`、`src/Orderly.App/App.Composition.cs`、`src/Orderly.App/App.SessionLock.cs`、`src/Orderly.App/ViewModels/MainViewModel.CommercePages.cs`、`src/Orderly.App/ViewModels/LoginViewModel*.cs`、`src/Orderly.App/Views/LoginSignInPanel.xaml*`、`README.md`（新增/已修改）：按团队云端正式版方案完成阶段 1/2/3/4 与客户端离线能力（2+3），新增 Contracts 共享 DTO/命令/事件/权限/缓存与草稿接口，ASP.NET Core 服务端（PostgreSQL + DbUp 迁移 + JWT/Refresh/账号权限/审计 + Commerce 读写查询与员工脱敏 + 事务内幂等/版本冲突/审计/变更日志 + SignalR Hub + 健康检查），Remote HTTP/SignalR 客户端、服务与仓储适配器、`RemoteAuthClient`、DPAPI 保护的 refresh token / 本地缓存 data key 存储；WPF 登录页在 `ORDERLY_RUNTIME=Cloud` 时显示云登录面板并支持静默刷新登录；本地 SQLCipher 数据库新增 `CloudCacheEntries` 与 `EmergencyDrafts` 表，`SqliteCloudCacheStore` 与 `SqliteEmergencyDraftQueue` 替代内存实现，远程仓储读操作在网络失败时自动回退到本地缓存，订单完成/库存变动/现金记录与结算在网络失败时自动保存为应急草稿；新增 `RemoteEmergencyDraftSubmitter` 后台提交器，每 60 秒扫描客户端 `EmergencyDrafts` 表并统一 POST 到 `api/workspaces/{id}/emergency-drafts`；服务端新增 `EmergencyDraftController`、`CloudEmergencyDrafts` 表、`EmergencyDraftProcessor` 与后台 `EmergencyDraftBackgroundService`，校验并仅重放允许的草稿类型（客户备注、订单阶段等），拒绝库存/现金流/改价/导出等不允许类型，执行成功标记 Submitted、失败标记 Failed；**生产就绪阶段 1 已完成**：同步快照/增量按 `CanViewCosts` 对员工脱敏（订单/商品成本、库存单价等字段置空），应急草稿记录 `SubmittedByUserId` 并在后台重放时恢复提交人身份与权限；补齐单条 GET 端点（products/{id}、inventory/items/{id}、customers/{id}、cashflow/entries/{id}）；补齐 Commerce 写端点（products POST/PUT、inventory/items POST/PUT、cashflow/{id} PUT）；补齐改价申请列表查询；Remote 层实现 `RemoteProductService` 与 `RemoteInventoryItemRepository`、`RemoteCashFlowEntryRepository` 的创建/更新/删除（通过最新 GET 取版本号、PUT/POST + 归档接口）。**生产就绪阶段 2 已完成**：补齐本地 SQLite 旧数据一次性导入云端的 API、Service 与 Remote 客户端，新增 `ImportController`（`POST /import/dry-run`、`POST /import/commit`、`GET /import/batches/{id}`）、`CloudImportService` 与 `RemoteImportService`；DryRun 校验管理员权限、按稳定键（商品 Code/客户 Phone/库存 Sku/订单 OrderNo/现金流 BusinessKey）检测重复并生成 `CloudImportEntityMap`，将整包数据写入 `CloudImportBatches.SourceReportJson`；Commit 时校验指纹不变，单事务插入新实体、记录变更日志与审计日志，失败回滚并标记 `Failed`。同步补拉服务端实现 `SyncController` 与 `WorkspaceSyncQueryService`，支持首次全量快照 + 后续增量变更日志（读 `CloudChangeLog`）。导出实现 `ExportController` + `ExportService` + `ExportBackgroundService`，用 ClosedXML 生成 Excel 工作簿并打包 ZIP，上传到阿里云 OSS 后提供下载链接，员工导出由 `CanExport` 返回 403。备份实现 `BackupBackgroundService` + `DatabaseBackupService` + `AliyunOssBlobStorage`，每日自动 `pg_dump` 数据库、压缩上传 OSS 并清理超过保留期的旧备份；OSS 配置通过 `ORDERLY_OSS_*` 环境变量注入。`MainWindow` 状态栏实时显示离线/待同步草稿数。`ORDERLY_RUNTIME=LocalDev` 时保持原有本地账号登录不变。WPF 组合根支持 `ORDERLY_RUNTIME=Cloud|LocalDev` 分支，SignalR 云端事件已按实体类型细化页面失效。
- `docs/ORDERLY_TEAM_CLOUD_AI_IMPLEMENTATION_PLAN.md`、`docs/ORDERLY_TEAM_CLOUD_OPERATOR_RUNBOOK.md`、`README.md`（新增/已修改）：新增并补强 Orderly 团队云端正式版落地方案，一份面向后续 AI/工程代理施工，覆盖当前 WPF + Commerce 本地架构到中心服务端、PostgreSQL、SignalR、权限、冲突、归档、离线草稿、导出和备份的实施细节，并补入本地旧数据一次性导入、永久 SourceInstanceId 导入来源身份、工作区级提交顺序游标补同步、FullResync staging cache 原子替换、ClientRequestId 单事务幂等、库存事务硬扣减与死锁重试、token 立即失效与 refresh token family 重放保护、固定 Admin/Employee 角色、负责数据归档边界、金额/数量精度、历史订单价格快照、数据库迁移、OSS 备份恢复、导出和列表分页规则；一份面向人工购买阿里云、配置域名/安全组、上线账号、日常权限、备份与验收操作。
- `src/Orderly.Data/Sqlite/DatabasePaths.cs`、`src/Orderly.Data/Sqlite/LocalDataFileSecurity.cs`、`src/Orderly.Data/Sqlite/LauncherDatabaseKeyStore.cs`、`src/Orderly.Data/Services/LocalCredentialSecretStore.cs`、`src/Orderly.Data/Services/CredentialAttemptTracker.cs`、`src/Orderly.Data/Sqlite/LegacyAppDataMigrationService.cs`、`src/Orderly.Data/Sqlite/CommerceSchemaInitializer.cs`、`README.md`（已修改）：将桌面端业务数据根目录切到 `D:\OrderlyData`，本地数据 ACL 加固时保留 `26911` 与 `XinglanOps` 的数据读写能力；启动器数据库密钥、账号元数据密钥与登录失败计数改为机器级 DPAPI 保护并兼容旧当前用户级密钥迁移；项目源码目录已设置为 `26911` 可修改、`XinglanOps` 只读运行，旧占位业务数据库已清理。
- `.github/workflows/release.yml`、`scripts/release/build-velopack-release.ps1`、`src/Orderly.App/Program.cs`、`src/Orderly.Infrastructure/Services/VelopackAppUpdateService.cs`、`src/Orderly.App/ViewModels/MainViewModel.Updates.cs`、`README.md`（已修改）：将发布架构收敛为同仓库公开 GitHub Releases，默认更新源固定为 `https://github.com/Fences779/Orderly`；发布工作流改为仅允许正式 tag `vX.Y.Z` 继续执行，使用 GitHub Actions 自带 `GITHUB_TOKEN` 和 `contents: write` 直接向当前仓库发布完整 Velopack 资产（含 `Orderly-stable-Setup.exe`、`Orderly-*.nupkg`、`RELEASES-stable`、`releases.stable.json`、`assets.stable.json`），并清理 `Orderly-Releases`、`ORDERLY_RELEASES_PAT` 和跨仓库发布口径；同 tag 重跑发布会先删除旧同名资产再上传，默认跳过旧资产下载并发布 full 包，Velopack 内部校验命令处理后会直接退出，应用内点击“检查更新”后发现新版本会直接下载，下载完成后再确认是否重启安装。
- `src/Orderly.App/Views/Sections/SettingsTabDataAudit.xaml`、`src/Orderly.App/ViewModels/MainViewModel.BackupRestoreState.cs`、`src/Orderly.App/ViewModels/MainViewModel.BackupCommands*.cs`（已修改）：将“数据校验与导入恢复”卡片重排为选择文件、检查备份、确认恢复三步流程，收拢恢复状态、检查结论、风险确认和按钮文案，真实备份恢复服务与安全规则保持不变。
- `src/Orderly.Data/Services/QuickLoginService.cs`、`src/Orderly.App/Services/WindowsHelloService.cs`、`src/Orderly.App/ViewModels/LoginViewModel*.cs`、`src/Orderly.App/Views/LoginSignInPanel.xaml*`、`src/Orderly.App/Views/PinUnlockView.xaml*`、`src/Orderly.App/App.SessionLock.cs`、`src/Orderly.App/Views/Sections/SettingsTabDataSecurity.xaml`（新增/已修改）：新增“本次开机允许快速登录（PIN / Windows Hello）”，使用 Windows 当前用户加密且绑定系统启动标识的临时票据恢复账号数据密钥；设置页已开启时登录页隐藏重复选择框，并始终保留主密码登录入口；修复主密码登录竞态误关闭设置项的问题，并让 PIN 锁定页支持 Windows Hello 解锁当前会话。
- `tests/Orderly.Tests/AssemblyInfo.cs`、`tests/Orderly.Tests/Support/PbtConfig.cs`（新增/已修改）：同时关闭 xUnit 测试类并行和 CsCheck 样本并行，保留每项至少 100 次属性覆盖，避免 SQLite 测试调用全局 `ClearAllPools()` 时相互释放连接造成随机假失败。
- `tests/Orderly.Tests/Security/LegacyAccountDatabasePathRepairTests.cs` (已修改)：账号旧数据路径修复测试继续覆盖当前 Windows 用户旧路径和外部用户旧路径，外部路径改用合成盘符，避免发布守卫误判为真实用户目录硬编码。
- `start-qa.bat` (新增)：提供以管理员（Owner）角色免登录直接启动应用的开发/QA模式脚本。
- `dev-watch-qa.bat` (新增)：提供具备热更新（dotnet watch）能力的管理员免密登录启动脚本。
- `AGENTS.md`、`tools/qa/README.md`、`docs/ORDERLY_RELEASE_CHECKLIST.md`、`docs/ORDERLY_QA_AUTOMATION.md`、`README.md` (已修改)：统一验收口径为“日常开发默认只验本次修改点相关链条；整套 QA 回归、全页面 UIA 点检与业务闭环回归下沉到发布前或专项回归”，并清理 `tools/qa/README.md` 中关于旧基线与 `dotnet test` 的过时描述。
- `tools/qa/qa-common.ps1` (已修改)：修改 `Assert-NoRunningOrderlyProcess`，增加进程缓冲等待和强杀兜底机制，解决测试时的竞态条件。
- `tools/qa/run-p1-write-smoke.ps1` (已修改)：修复在 SQLCipher 加密库下直连报错的 bug，改用 `New-QaConnectionFactory` 以便带密码连接数据库。
- `tools/qa/run-uia-smoke.ps1` (已修改)：为 `Save-WindowScreenshot` 里的 `CopyFromScreen` 增加 try-catch，避免在无 GUI 交互后台会话中运行时因截图失败而中断测试；寻找 Tab 的超时等待时间延长至 15 秒，并精确选择“Orderly 商家工作台”，避免误将悬浮入口球识别为主窗口。
- `src/Orderly.App/ViewModels/SensitivePageGuardViewModel.cs` (已修改)：为 QA 模式的伪造内存账号放行敏感财务页面的 PIN 码解锁，以支持 UIA 冒烟与本地自动免密调试。
- `src/Orderly.App/Views/Sections/SettingsView.xaml` (已修改)：将右侧承载内容容器由 `Border` 替换为 `ContentControl`，使其能被 UIA 树识别以公开 `Pane_SettingsContent` 标志，通过冒烟测试校验。
- `src/Orderly.App/Helpers/FontSizeHelper.cs` (新增)：全局字号缩放管理器，提供内存资源字典覆盖与热更新机制。
- `src/Orderly.App/Views/Sections/SettingsTabAppearance.xaml` (已修改)：字号调节组件从三档分段按钮重构为精致的连续滑动条（Slider），支持实时缩放比例指示与视觉无延迟更新。
- `src/Orderly.App/ViewModels/MainViewModel.SettingsP0.cs` & `SettingsViewModel.cs` (已修改)：字号配置参数调整为 double 比例类型，新增拖动防抖延迟 500 毫秒后自动保存机制。
- `src/Orderly.App/ViewModels/MainViewModel.SettingsRuntime.cs` (新增)：集中承接设置页运行时消费，覆盖开机自启、自动备份、通知调度、调试日志、隐私脱敏复制、窗口位置保存和快捷键动作。
- `src/Orderly.App/ViewModels/RuntimeHotkeyAction.cs`、`src/Orderly.App/App.xaml.cs`、`src/Orderly.App/App.WorkspaceComposition.cs` (新增/已修改)：将设置页的全部快捷键配置注册到真实全局热键，并在设置保存前进行运行时重绑与失败回滚。
- `src/Orderly.App/App.WindowSettings.cs`、`src/Orderly.App/App.SessionLock.cs`、`src/Orderly.App/Views/MainWindow.xaml.cs`、`src/Orderly.App/Helpers/ThemeHelper.cs` (新增/已修改)：让主题强调色、默认窗口模式、关闭窗口后最小化到托盘、浮窗启动和窗口位置记忆在应用启动/退出/保存时生效。
- `src/Orderly.Core/Models/AppPreferences.cs`、`src/Orderly.Core/Models/AppSettingKeys.cs`、`src/Orderly.Data/Repositories/AppSettingRepository.cs` (已修改)：补齐窗口位置与自动备份时间戳等偏好字段的持久化读写。
- `src/Orderly.App/Views/FloatingWindow.xaml`、`src/Orderly.App/Views/FloatingWindow.xaml.cs`、`src/Orderly.App/App.xaml.cs`、`src/Orderly.App/App.WorkspaceComposition.cs`、`src/Orderly.App/Views/Sections/SettingsTabAppearance.xaml`、`src/Orderly.App/ViewModels/MainViewModel.SettingsP0.cs`、`src/Orderly.App/ViewModels/MainViewModel.SettingsP0.Mapping.cs`、`src/Orderly.App/ViewModels/SettingsSearchIndex.cs`、`src/Orderly.Core/Models/AppPreferences.cs`、`src/Orderly.Core/Models/AppSettingKeys.cs`、`src/Orderly.Data/Repositories/AppSettingRepository.cs`、`src/Orderly.Data/Sqlite/DatabaseInitializer.Seed.cs` (已修改)：将原桌面悬浮助手重做为可拖动的 Orderly 悬浮入口球，新增“悬浮球”总开关与启动显示从属开关，支持左键打开主窗口、右键快捷菜单、页面快速进入、隐藏、退出、透明度调整与位置/透明度持久化；悬浮球退出复用托盘退出流程。
- `src/Orderly.Data/Repositories/SettingsAwareActivityLogRepository.cs`、`src/Orderly.Core/Repositories/ISyncRecordRepository.cs`、`src/Orderly.Data/Repositories/SyncRecordRepository.cs` (新增/已修改)：让操作日志开关和同步失败提醒不再停留在 UI 保存层。
- `src/Orderly.Core/Services/IBackupService.cs`、`src/Orderly.Data/Services/LocalBackupService.Export.cs`、`src/Orderly.Data/Services/LocalBackupService.Shared.cs`、`src/Orderly.App/ViewModels/MainViewModel.BackupCommands.cs` (已修改)：让“导出时包含敏感信息”和自动备份频率/保留数量进入真实备份链路。
- `src/Orderly.Core/Models/AiSuggestionRequest.cs`、`src/Orderly.Data/Services/LocalAiAssistantService.cs`、`src/Orderly.Data/Services/AiProviderOptions.cs`、`src/Orderly.Data/Services/ChatCompletionSuggestionSupport.cs`、`src/Orderly.Data/Services/LocalAiSuggestionProvider.cs` (已修改)：让 AI 助手开关、模型、超时、上下文范围、语气长度、自动摘要和支付交易号遮蔽影响真实生成请求。
- `src/Orderly.App/Orderly.App.csproj` (已修改)：在配置中排除 `dotnet watch` 针对 `*_wpftmp.csproj` 临时文件与 `bin/obj` 目录的监视，解决热重载频繁闪退挂起问题。
- `src/Orderly.Infrastructure/Services/VelopackAppUpdateService.cs`、`scripts/e2e/Run-OrderlyLocalE2E.ps1`、`scripts/e2e/README.md`（新增/已修改）：`ORDERLY_UPDATE_SOURCE_URL` 现已同时支持 Web 更新源、本地绝对路径和 `file://` 更新源；本地目录会严格校验存在性与 `releases.stable.json`，无效配置直接返回清晰错误；本地 E2E 脚本增加 `-ValidateOnly` 无副作用自检、Windows PowerShell 5.1/StrictMode 对象安全读取、卸载注册表字段全量诊断、可重跑安装清理保护、失败报告兜底和卸载保留 `%LOCALAPPDATA%\OrderlyData` 验证入口；当前测试账号安装验收已切到 `0.1.2` 基线，会清理旧 `%LOCALAPPDATA%\Orderly`，从 `https://github.com/Fences779/Orderly` 的 Release 下载 `0.1.2` 安装包并启动应用交由人工测试。
