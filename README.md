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
- 本地优先：业务数据保存在本机，云端同步不在主线默认范围内
- `miniprogram/` 与 `cloudfunctions/` 不属于本主线交付范围

## 文件状态
- `src/Orderly.Contracts`、`src/Orderly.Server`、`src/Orderly.Remote`、`src/Orderly.Data`、`Orderly.sln`、`src/Orderly.App/App.WorkspaceComposition.cs`、`src/Orderly.App/App.xaml.cs`、`src/Orderly.App/App.Composition.cs`、`src/Orderly.App/App.SessionLock.cs`、`src/Orderly.App/ViewModels/MainViewModel.CommercePages.cs`、`src/Orderly.App/ViewModels/LoginViewModel*.cs`、`src/Orderly.App/Views/LoginSignInPanel.xaml*`、`README.md`（新增/已修改）：按团队云端正式版方案完成阶段 1/2/3/4 与客户端离线能力（2+3），新增 Contracts 共享 DTO/命令/事件/权限/缓存与草稿接口，ASP.NET Core 服务端（PostgreSQL + DbUp 迁移 + JWT/Refresh/账号权限/审计 + Commerce 读写查询与员工脱敏 + 事务内幂等/版本冲突/审计/变更日志 + SignalR Hub + 健康检查），Remote HTTP/SignalR 客户端、服务与仓储适配器、`RemoteAuthClient`、DPAPI 保护的 refresh token / 本地缓存 data key 存储；WPF 登录页在 `ORDERLY_RUNTIME=Cloud` 时显示云登录面板并支持静默刷新登录；本地 SQLCipher 数据库新增 `CloudCacheEntries` 与 `EmergencyDrafts` 表，`SqliteCloudCacheStore` 与 `SqliteEmergencyDraftQueue` 替代内存实现，远程仓储读操作在网络失败时自动回退到本地缓存，订单完成/库存变动/现金记录与结算在网络失败时自动保存为应急草稿；新增 `RemoteEmergencyDraftSubmitter` 后台提交器，每 60 秒扫描 `EmergencyDrafts` 表并统一 POST 到 `api/workspaces/{id}/emergency-drafts`（服务端端点待下一阶段实现），失败时保持 Pending 重试、业务异常时标记 Failed；`MainWindow` 状态栏实时显示离线/待同步草稿数。`ORDERLY_RUNTIME=LocalDev` 时保持原有本地账号登录不变。WPF 组合根支持 `ORDERLY_RUNTIME=Cloud|LocalDev` 分支，SignalR 云端事件已按实体类型细化页面失效。导出、同步/补拉、OSS 备份脚本待后续阶段完成。
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
