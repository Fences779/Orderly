# P4 Engineering Audit

日期：2026-04-30
阶段：P4.1 工程健康审计（P4.4 状态已同步）
状态：已完成
下一步：交给 Antigravity 做 UI/XAML/视觉/125% 缩放/Final Visual QA
当前发布入口：`docs/RELEASE_CHECK.md`
结论：P4.1 / P4.2A / P4.2B 已完成；当前工程发布口径接受 `QA-only baseline`，`dotnet test` 非必跑，当前剩余非视觉阻断项为无；本轮不建议改业务逻辑和任何视觉/UI/XAML 表现。

## P4.4 Release Freeze 状态

- 当前发布基线：`QA-only baseline`
- 当前 `main` 没有正式 tests project，本轮不恢复
- `dotnet test` 不是当前发布必跑项
- 2026-04-30 已复跑 `build + QA smoke`，结果全部 PASS
- 当前剩余非视觉阻断项：无
- 下一阶段交给 Antigravity 处理 UI/XAML/视觉/125% 缩放/Final Visual QA

## P4.2A 处理状态

- 已建立当前发布前统一入口：`docs/RELEASE_CHECK.md`
- 已同步当前主线文档：
  - `README.md`
  - `docs/product-overview.md`
  - `docs/deployment.md`
  - `docs/QA_AUTOMATION.md`
  - `tools/qa/README.md`
- 已把以下历史文档明确标记为 `Legacy / Historical Reference`，并注明“非当前 main 发布基线”：
  - `docs/flow-and-state-machine.md`
  - `docs/data-model.md`
  - `docs/manual-test-checklist.md`
- 当前剩余未处理项已收敛为 UI / 视觉阶段交接；tests project 本轮不恢复，Release freeze 另见 `docs/P4_RELEASE_FREEZE.md`

## P4.2B 处理状态

- 已完成 `AutoReplyState` 内部常量与 metadata 读取 helper 收口；仅替换内部引用，不改变 SQLite 存储值与 UI 文案。
- 已完成 `FollowUp` 开放态 / 日期判断收口，集中到当前主线 `Core` helper；`CompletedAt` 终态语义因兼容风险未做扩大化收口。
- 已完成 `MainViewModel` 小步命令刷新辅助提炼，并移除 `RestoreBackupCommand` 的一处手动重复刷新；未改绑定名、命令语义或 XAML。
- 本轮验证通过：`dotnet build Orderly.sln -c Debug`、`run-qa-data-status.ps1`、`run-p3-2-pipeline-smoke.ps1`、`run-p3-5-search-smoke.ps1`、`run-p3-6-navigation-smoke.ps1`、`run-p1-smoke.ps1`。

## 当前项目状态

- 当前主线是 Windows 桌面端 WPF/.NET 8 应用，解决方案入口为 `Orderly.sln`。
- P1 / P2 / P3 主业务链与 closeout 已完成，P4 当前目标不是加功能，而是工程封板、自动化验收、发布前收口。
- 本轮实际验证通过：
  - `dotnet build Orderly.sln -c Debug`
  - `tools/qa/run-p1-smoke.ps1`
  - `tools/qa/run-p3-2-pipeline-smoke.ps1`
  - `tools/qa/run-p3-5-search-smoke.ps1`
  - `tools/qa/run-p3-6-navigation-smoke.ps1`
- 当前主线的 QA 基线仍然是 `build + smoke/regression scripts`，不是正式 `dotnet test` 测试项目基线。

## 项目地图

- `Orderly.sln`
  - `src/Orderly.App`
    - WPF 应用入口、View、ViewModel、交互状态
    - 关键文件：`App.xaml.cs`、`ViewModels/MainViewModel*.cs`、`Views/*.xaml`
  - `src/Orderly.Core`
    - 领域模型、枚举、仓储接口、服务接口
    - 关键模型：`Customer / MerchantOrder / Deal / FollowUp / PipelineStage / Navigation*`
  - `src/Orderly.Data`
    - SQLite 仓储实现、本地 service、投影逻辑、QA 数据工具
    - 关键区域：`Repositories/`、`Services/`、`Sqlite/`
  - `src/Orderly.Infrastructure`
    - 剪贴板、托盘、全局热键等桌面基础设施
- `tools/qa`
  - P1/P2/P3 smoke 与 full regression 脚本
  - `qa-common.ps1` 为统一入口辅助
- `docs`
  - P2/P3 收口文档、QA 文档、发布验收文档、若干历史参考文档
- `tests`
  - 当前 `main` 下不是有效测试工程；仅存在本地残留 `bin/obj` 产物

## 已确认健康项

- 分层总体清晰：`App -> Core/Data/Infrastructure` 边界基本成立，未发现明显 UI 直接操作 SQLite 的路径。
- 业务主链当前实际状态源以代码为准，核心枚举已集中定义：
  - `OrderStatus`：`src/Orderly.Core/Models/OrderStatus.cs`
  - `DealStage`：`src/Orderly.Core/Models/DealStage.cs`
  - `PipelineStage`：`src/Orderly.Core/Models/PipelineStage.cs`
  - `FollowUpStatus`：`src/Orderly.Core/Models/FollowUpStatus.cs`
- SQLite 接入方式统一：仓储层通过 `SqliteConnectionFactory` 建连，未发现绕开工厂直接拼接连接的并行实现。
- `PipelineStage` 仍保持只读 projection，没有发现写回 schema 或篡改 `OrderStatus / DealStage` 的行为。
- QA 自动化与当前主线基线基本一致：
  - `docs/QA_AUTOMATION.md`
  - `tools/qa/README.md`
  - `docs/RELEASE_CHECK.md`
- UIA smoke 当前可跑通，说明自动化 ID 与最小主链路交互仍然有效。
- 未发现明显 `.Result / .Wait / GetAwaiter().GetResult()` 式同步阻塞调用，未看到立即可见的 UI 死锁风险。

## 发现的问题

### P1

1. 历史产品/流程文档与当前 WPF 主线严重分叉。
   - 证据：
     - `docs/flow-and-state-machine.md`
     - `docs/data-model.md`
     - `docs/deployment.md`
     - `docs/manual-test-checklist.md`
     - `docs/product-overview.md`
   - 现状：
     - 文档仍在描述小程序、云函数、`capture`、`quote`、`dealStage` 14 状态、云开发集合与页面路由。
     - 当前主工程实际是 WPF + SQLite，本地主链使用 `OrderStatus / DealStage / PipelineStage / FollowUpStatus`。
   - 影响：
     - 容易误导 P4 后续维护、发布检查、人工验收、以及 Antigravity 的 UI 对接口径。
     - 会造成“文档声明已实现，但当前主线根本没有该对象/页面/部署链路”的判断错误。
   - 是否建议现在修复：是。
   - 修复方式：只做文档收口，标记历史文档为 legacy / archived，或改写为“非当前 main 基线”。不需要动代码。
   - 当前状态：已在 `P4.2A` 完成文档收口；当前 `main` 请以 `README.md`、`docs/product-overview.md`、`docs/deployment.md`、`docs/RELEASE_CHECK.md`、`docs/QA_AUTOMATION.md` 为准。

### P2

1. 当前 `main` 没有正式测试工程基线，`tests/Orderly.Tests` 只是本地残留产物。
   - 证据：
     - `Orderly.sln` 未包含测试项目
     - `git ls-files tests` 为空
     - `tests/Orderly.Tests` 下无 `.csproj`、无源码，仅有 `bin/obj`
     - `docs/QA_AUTOMATION.md` 与 `README.md` 明确写为 `QA-only baseline`
   - 影响：
     - 如果发布门槛要求 `dotnet test`，当前 `main` 不满足。
     - 当前回归能力主要依赖 smoke/regression 脚本，缺少主线单元/集成测试兜底。
   - 是否建议现在修复：不建议在 P4.1 立即恢复整套 tests project。
   - 修复方式：在 P4.2 先做“发布标准决策”。
     - 路径 A：明确当前主线以 QA smoke 作为发布基线。
     - 路径 B：从隔离分支恢复最小测试工程，再纳入 `Orderly.sln`。

2. `MainViewModel` 仍然偏重，维护成本高，但暂未构成当前发布阻断。
   - 证据：
     - `src/Orderly.App/ViewModels/MainViewModel.cs` 仍有 20 个依赖字段、多个大型状态块
     - `MainViewModel.*.cs` 全部 partial 合计约 2.7k+ 行
     - `isLoading / isSaving / isGeneratingAiSuggestion` 三组属性重复声明了大段 `NotifyCanExecuteChangedFor`
   - 影响：
     - 后续任何非视觉功能收尾都容易继续把责任堆回主 ViewModel。
     - 命令可执行状态刷新规则分散，后续加命令容易漏改。
   - 是否建议现在修复：否。
   - 修复方式：P4.2 只做小步减重，例如抽公共“命令状态刷新”辅助，不做大拆分。

3. 状态标签和状态判断存在重复实现，后续存在分叉风险。
   - 证据：
     - 标签重复：`src/Orderly.App/Converters/EnumLabelConverter.cs` 与 `src/Orderly.App/ViewModels/Helpers/StatusLabelHelper.cs`
     - FollowUp 可流转判断重复：
       - `src/Orderly.App/ViewModels/Helpers/FollowUpDateHelper.cs`
       - `src/Orderly.Data/Services/FollowUpService.cs`
       - `src/Orderly.Data/Services/LocalWorkbenchTaskService.cs`
     - AutoReply metadata state `"prepared" / "copied" / "sent" / "rejected"` 以 magic string 分散在多个 service/projection 中
   - 影响：
     - 当前没炸，但后续只要改一处状态标签或状态语义，就可能出现 UI、投影、搜索、任务生成不一致。
   - 是否建议现在修复：否，P4.1 不动代码。
   - 修复方式：P4.2 做低风险常量/辅助类收口，不改业务行为。

### P3

1. QA 文档已自洽，但覆盖边界仍然要继续明确。
   - 现状：
     - 当前脚本能覆盖构建、P1 UIA 主链路、P3 pipeline/search/navigation。
     - 不覆盖最终 UI/XAML 视觉、125% 缩放、真实外网 AI API、云同步、生产库覆盖恢复。
   - 影响：
     - 如果后续有人把“smoke 通过”误读为“最终交付已验收完成”，会造成错误发布判断。
   - 是否建议现在修复：是。
   - 修复方式：在 P4.2 文档层继续强化“工程验收”和“视觉/UI 验收”的边界。

## 业务闭环一致性结论

- 当前代码口径：
  - 客户：`CustomerStatus`
  - 订单：`OrderStatus`
  - 成交机会：`DealStage`
  - 只读工作台阶段：`PipelineStage`
  - 跟进：`FollowUpStatus`
- 当前主链 `咨询/客户 -> 订单/报价信号 -> 跟进 -> 履约 -> 复购提醒` 在代码里是闭合的，但它不是历史文档中那套“小程序 capture/quote/dealStage 云函数状态机”。
- `Dashboard / Workbench / Search / QuickAction / NavigationRoute` 已围绕 `PipelineStage` 与路由语义收口，实测 smoke 通过。
- 一致性风险主要不在代码，而在历史文档仍把旧体系写成“当前正式口径”。

## 自动化验收结论

- 构建稳定：通过。
- QA 脚本稳定：本轮执行的 `P1 / P3.2 / P3.5 / P3.6` 均通过。
- SQLite QA 数据维护链可理解且可运行：
  - `run-qa-data-status.ps1`
  - `reset-qa-data.ps1`
  - `clear-qa-data.ps1`
  - `qa-common.ps1`
- `docs/QA_AUTOMATION.md`、`tools/qa/README.md` 与 `docs/RELEASE_CHECK.md` 已作为当前主线发布入口。
- 发布前阻断判断：
  - 当前发布标准已明确为“build + QA smoke/regression”。
  - `dotnet test` 只有在 `main` 恢复正式 tests project 后才升级为必跑项。

## 推荐进入 P4.2 的任务清单

1. 收口历史文档，把旧小程序/云函数体系明确标成 legacy，避免继续污染当前主线判断。
2. 明确当前发布标准：`QA-only baseline` 还是 `恢复 tests project`。
3. 做一轮只读优先的 release-check 文档收口，形成 P4 发布前统一入口。
4. 低风险收口 `AutoReplyState` magic strings，改为集中常量或 helper。
5. 低风险收口 FollowUp 可流转判断，避免 helper / service / projection 三处分叉。
6. 低风险减少 `MainViewModel` 中重复的命令状态刷新声明，不改 UI 行为。
7. 在文档中明确“工程验收通过不等于视觉验收通过”。

## 应留给 Antigravity/UI 阶段的事项

- `MainWindow.xaml` 以及任何 XAML 视觉打磨
- 布局、颜色、字号、间距、控件层级、动画、视觉层次
- 125% 缩放、视觉回归、最终桌面交互质感
- 任何只为 UI 呈现而新增的绑定、样式和视觉状态

## 不建议做的事项

- 不建议在 P4.2 做大规模 ViewModel / service / repository 重构。
- 不建议引入新依赖、新框架、新数据库迁移体系。
- 不建议为 UI 再建一套平行状态源、平行路由逻辑或平行任务系统。
- 不建议在没有发布标准决策前，直接重建完整 tests 体系。
- 不建议现在处理视觉/XAML 问题，避免和 Antigravity 阶段冲突。

## 验证记录

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-qa-data-status.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-2-pipeline-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-5-search-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-6-navigation-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
```

结果：

- `dotnet build`：PASS
- `run-qa-data-status`：PASS
- `run-p3-2-pipeline-smoke`：PASS
- `run-p3-5-search-smoke`：PASS
- `run-p3-6-navigation-smoke`：PASS
- `run-p1-smoke`：PASS
