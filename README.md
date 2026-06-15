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

- 应用数据根目录：`%LocalAppData%\Orderly`
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

- `start-qa.bat` (新增)：提供以管理员（Owner）角色免登录直接启动应用的开发/QA模式脚本。
- `dev-watch-qa.bat` (新增)：提供具备热更新（dotnet watch）能力的管理员免密登录启动脚本。
- `AGENTS.md` (已修改)：在验收工作流中强制加入运行 UIA 自动点击测试进行管理员登录后实战验收的要求；追加关于防范 WPF 项目在 `dotnet watch` 模式下因临时文件监视导致挂起（pause）的开发规约。
- `tools/qa/qa-common.ps1` (已修改)：修改 `Assert-NoRunningOrderlyProcess`，增加进程缓冲等待和强杀兜底机制，解决测试时的竞态条件。
- `tools/qa/run-p1-write-smoke.ps1` (已修改)：修复在 SQLCipher 加密库下直连报错的 bug，改用 `New-QaConnectionFactory` 以便带密码连接数据库。
- `tools/qa/run-uia-smoke.ps1` (已修改)：为 `Save-WindowScreenshot` 里的 `CopyFromScreen` 增加 try-catch，避免在无 GUI 交互后台会话中运行时因截图失败而中断测试；同时将寻找 Tab 的超时等待时间延长至 15 秒以降低后台启动竞态失败率。
- `src/Orderly.App/ViewModels/SensitivePageGuardViewModel.cs` (已修改)：为 QA 模式的伪造内存账号放行敏感财务页面的 PIN 码解锁，以支持 UIA 冒烟与本地自动免密调试。
- `src/Orderly.App/Helpers/FontSizeHelper.cs` (新增)：全局字号缩放管理器，提供内存资源字典覆盖与热更新机制。
- `src/Orderly.App/Views/Sections/SettingsTabAppearance.xaml` (已修改)：字号调节组件从三档分段按钮重构为精致的连续滑动条（Slider），支持实时缩放比例指示与视觉无延迟更新。
- `src/Orderly.App/ViewModels/MainViewModel.SettingsP0.cs` & `SettingsViewModel.cs` (已修改)：字号配置参数调整为 double 比例类型，新增拖动防抖延迟 500 毫秒后自动保存机制。
- `src/Orderly.App/Orderly.App.csproj` (已修改)：在配置中排除 `dotnet watch` 针对 `*_wpftmp.csproj` 临时文件与 `bin/obj` 目录的监视，解决热重载频繁闪退挂起问题。
