# OPUS4.8 God-File Refactor Candidates

- 日期：2026-05-29
- 用途：为「下一独立任务」提供 God-file 排名与**单个最安全的首拆候选**。
- 本文件**不执行任何重构**。本次收尾审计明确不开始 God-file 重构。
- 度量口径：`src/**/*.cs` + `src/**/*.xaml` 行数（不含生成产物 `obj/`、`*.g.cs` 与 `src/scratch/` 临时件，这些不应作为重构目标）。

---

## 1. 排名（按行数，排除生成件/临时件）

| 排名 | 文件 | 行数 | 类型 | 受保护状态 | 拆分风险 |
|---|---|---|---|---|---|
| 1 | `src/Orderly.App/Views/MainWindow.xaml` | 7975 | UI/XAML | 首页重构目标，但 UI 改动受 AGENTS.md 规则 0 限制 | 高（视觉回归风险，受 UI 禁改约束） |
| 2 | `src/Orderly.Core/Models/StringNarrationOrderModels.cs` | 1837 | 纯模型（POCO/DTO） | 否 | **低** |
| 3 | `src/Orderly.App/Views/Resources/MainWindowResources.xaml` | 1768 | UI 资源 | 含履约页资源（部分受保护） | 中-高 |
| 4 | `src/Orderly.Data/Services/StringNarrationGatewayOrderService.cs` | 1696 | 服务 | 与订单/履约链路接近 | 中-高 |
| 5 | `src/Orderly.App/Views/LoginView.xaml` | 1645 | UI/XAML | 登录页，规则 1 禁改 | 禁止 |
| 6 | `src/Orderly.App/ViewModels/MainViewModel.StringNarrationOrders.cs` | 1612 | ViewModel | 订单履约页相关，规则 3 禁改 | 高（受保护） |
| 7 | `src/Orderly.Data/Services/LocalBackupService.cs` | 1491 | 服务 | 否 | 中 |
| 8 | `src/Orderly.Data/Services/QaDataSeeder.cs` | 1345 | QA 工具 | 否 | 低-中 |
| 9 | `src/Orderly.App/ViewModels/MainViewModel.SettingsP0.cs` | 1183 | ViewModel | 设置页，规则 2 禁改 | 禁止 |
| 10 | `src/Orderly.Data/Services/StringNarrationGatewayBusinessService.cs` | 1086 | 服务 | 与履约/业务链路接近 | 中-高 |

> `MainWindow.xaml.cs`(777)、`App.xaml.cs`(627)、各 `MainViewModel.*` 分部类同属候选，但行数与/或耦合度不及上表，且多数与受保护页面（设置/履约/异常/登录）强相关。

---

## 2. 单个最安全 / 最高价值的首拆候选

### 候选：`src/Orderly.Core/Models/StringNarrationOrderModels.cs`（1837 行）

为何是**最安全**：

- **纯数据模型聚合**：该文件是一组互相独立的 `public sealed class` POCO/DTO（query、page info、list result、fulfillment 指标、exception snapshot、order summary/detail、production/work order snapshot、各类 request/result 等），无业务流程逻辑、无状态机、无 IO。
- **拆分是纯机械的「按类型搬家」**：可按主题把每个类移到独立文件（如 `StringNarrationOrderSummary.cs`、`StringNarrationFulfillmentModels.cs`、`StringNarrationExceptionModels.cs`、`StringNarrationProductionModels.cs` 等），命名空间保持 `Orderly.Core.Models` 不变，**无需修改任何引用方**。
- **不触碰受保护链路代码**：它是被支付/履约链路*引用*的模型定义，拆分只移动声明、不改字段语义、不改序列化行为（`JsonSerializerOptions`、snapshot 解析逻辑随所属类整体迁移即可），因此不构成对受保护流程「行为」的修改。
- **零 UI 影响**：不属于 XAML / ViewModel 交互，规避 AGENTS.md 规则 0（UI 禁改）。
- **可验证性强**：拆分后只需 `dotnet build` 0 警告/0 错误 + 现有 QA smoke 全绿即可确认无回归，验证成本低。

为何**高价值**：

- 它是排除 UI 与受保护页面后**体量最大**的单文件，拆分后对「订单/履约/异常/生产」模型的可读性与可维护性提升最直接。

### 建议的首拆动作（留待下一独立任务执行，本次不做）

1. 在 `src/Orderly.Core/Models/` 下按主题新建文件，逐个迁移类声明，保持命名空间不变。
2. 优先先切出边界最清晰、依赖最少的一组（建议先切 `StringNarration*Request` / `*Result` 这类无嵌套依赖的请求/响应模型）作为最小首步。
3. 每迁移一组即 `dotnet build -c Debug` + 运行发布前 QA smoke，确认 0 警告/0 错误且全绿。
4. 全程不改字段名、类型、默认值与序列化逻辑，避免触及支付/履约语义。

### 明确排除作为首拆的原因

- `MainWindow.xaml` / `MainWindowResources.xaml` / `LoginView.xaml`：UI/XAML，受规则 0/1 限制，视觉回归风险高。
- `MainViewModel.StringNarrationOrders.cs` / `MainViewModel.SettingsP0.cs`：分别属订单履约页（规则 3）与设置页（规则 2），受保护禁改。
- `StringNarrationGatewayOrderService.cs` / `StringNarrationGatewayBusinessService.cs`：与订单创建/履约同步链路过近，拆分可能触碰受保护行为，不适合作为「最安全」首拆。

---

## 3. 边界声明

- 本文件仅为规划，**未进行任何代码移动或重构**。
- 实际拆分须作为独立任务、在获得明确实施指令后进行，并遵循 AGENTS.md 的最小作用域与受保护页面约束。
