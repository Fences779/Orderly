# OPUS4.8 Orderly Release Closeout

- 日期：2026-05-29
- 范围：发布前收尾审计（continuation，从上次 InternalServerException 中断点恢复）
- 口径：当前 `main` 主线，`QA-only baseline`（见 `docs/RELEASE_CHECK.md`、`tools/qa/README.md`）
- 本次未进行：UI/XAML 视觉收尾、God-file 重构、任何受保护链路改动

---

## 1. 执行总览（结论先行）

- 构建：`dotnet build Orderly.sln -c Debug` 通过，0 警告 / 0 错误。
- QA：发布前必跑 QA smoke 脚本全部通过。
- 脏工作区：全部属于「库存云同步 / 库存工作区接入」特性 + 一处现金流响应式布局调整，均为本次审计之前已存在的未提交工作，已完整保留，未做 reset / revert / clean / 覆盖。
- 受保护链路（微信支付回调验签与下单、自动已支付状态流转、微信履约/发货同步与支付—履约闭环、小程序兼容）本次零改动。
- 阻断项：未发现新的发布阻断项。
- 功能就绪度：**Orderly 在当前 `QA-only baseline` 口径下，对今天的收尾而言功能上已就绪。**

---

## 2. 构建与 QA 命令执行记录

下表区分「上一轮中断前已确认」与「本轮新执行」结果。

| 命令 | 来源 | 结果 |
|---|---|---|
| `dotnet build Orderly.sln -c Debug`（首次） | 上一轮已确认 | 失败，仅因运行中的 `Orderly.App.exe` 锁定输出二进制 |
| `dotnet build Orderly.sln -c Debug`（停止 app 进程后） | 上一轮已确认 | 通过，0 警告 / 0 错误 |
| `tools/qa/run-qa-data-status.ps1` | 上一轮已确认 | 通过 |
| `tools/qa/run-p3-2-pipeline-smoke.ps1` | 上一轮已确认 | 通过 |
| `tools/qa/run-p3-5-search-smoke.ps1` | 上一轮已确认 | `P3.5 SEARCH/ACTION SMOKE: PASS`（中断前已显示） |
| `dotnet build Orderly.sln -c Debug`（本轮重跑，一致性基线） | 本轮新执行 | 通过，0 警告 / 0 错误 |
| `tools/qa/run-p3-6-navigation-smoke.ps1` | 本轮新执行 | `P3.6 NAVIGATION ROUTE SMOKE: PASS`（Workbench task count: 61，Route summaries checked: 5） |
| `tools/qa/run-p1-smoke.ps1` | 本轮新执行 | 全链路通过：`P1 WRITE SMOKE: PASS` + `UIA smoke PASS`，QA 基线 reset/seed 前后一致 |
| `git status --short` | 本轮新执行 | 输出符合预期，无误提交产物；QA 脚本运行后工作区无新增改动（脚本自带基线还原） |

说明：
- 进入本轮时确认 `Orderly.App.exe` 已不在运行（`NOT_RUNNING`），因此重跑构建未再遇到二进制锁定问题。
- P1 smoke 使用独立 QA 库 `artifacts/qa-db/orderly.qa.db`，与主库隔离；运行过程 reset → status → write-chain → UIA → status，结束后基线计数完全还原。
- 未重复已确认通过的 `run-qa-data-status` / `run-p3-2` / `run-p3-5`，因其在上一轮已确认且本轮无相关代码改动；本报告将其作为一致性引用纳入最终结论。

---

## 3. 当前脏工作区文件分组

`git status` 全部变更均归属以下两组，均为**审计前已存在的未提交工作**，已原样保留。

### 组 A：库存云同步 / 库存工作区接入（主特性）

已跟踪文件（modified）：

- `src/Orderly.App/App.xaml.cs` — 组合根注入 `IInventoryWorkspaceService`，按环境变量是否配置在 `CloudInventoryWorkspaceService` 与本地只读 `StringNarrationInventoryWorkspaceServiceAdapter` 之间二选一。
- `src/Orderly.App/ViewModels/MainViewModel.cs` — 新增 `_inventoryWorkspaceService` 字段、构造参数与空实现 `EmptyInventoryWorkspaceService` 兜底。
- `src/Orderly.App/ViewModels/MainViewModel.BusinessPages.cs` — 库存看板加载改走 `_inventoryWorkspaceService.GetDashboardAsync`，空态文案调整。
- `src/Orderly.Data/Orderly.Data.csproj` — 新增 `ClosedXML 0.104.2` 包引用（Excel 导入/导出）。
- `README.md` — 补充库存模块可选 CloudBase SQL 同步链路说明。
- `src/Orderly.App/Views/MainWindow.xaml` — 库存页新增「导入 Excel / 导出 Excel」按钮（绑定库存命令）。

未跟踪文件（new）：

- `src/Orderly.Core/Services/IInventoryWorkspaceService.cs` — 库存工作区服务接口。
- `src/Orderly.Core/Models/InventoryCloudSyncModels.cs` — 工作簿行 / 导入预览 / 提交结果模型。
- `src/Orderly.Data/Services/InventoryGatewayOptions.cs` — 网关环境变量配置与校验。
- `src/Orderly.Data/Services/InventoryGatewayClient.cs` — 网关 HTTP 客户端（Bearer token、超时、错误解析）。
- `src/Orderly.Data/Services/CloudInventoryWorkspaceService.cs` — 云端库存实现 + Excel 读写 + 本地只读兜底适配器。
- `src/Orderly.App/ViewModels/MainViewModel.InventoryWorkbookSync.cs` — 导入/导出命令（确认弹窗、错误汇总、备份与回写）。
- `cloudfunctions/inventorySqlGateway/`（`index.js`、`package.json`）— CloudBase 库存 SQL 网关云函数。
- `scripts/cloudbase/`（含 `inventory_schema.sql`）— 库存建表脚本。
- `docs/inventory-cloudbase-sql-setup.md` — 库存 CloudBase SQL 落地文档。

### 组 B：现金流页响应式布局调整（独立小改动）

- `src/Orderly.App/Views/MainWindow.xaml` — 现金流 Tab 由 `ScrollViewer + StackPanel` 改为 `Grid` 行布局，卡片间距/内边距微调。
- `src/Orderly.App/Views/MainWindow.xaml.cs` — 新增 `MainWindow_SizeChanged` / `UpdateCashflowTrendCardVisibility`，按窗口最大化或宽度 ≥1600 切换趋势卡片显隐与行高。

> 注：组 B 属于现金流页视觉/布局行为，不在本次「库存」主特性范围内，但同样是审计前已存在的未提交改动，已保留。本次未对其做任何视觉收尾或进一步改动。

---

## 4. 库存云同步 / 库存工作区接入：完成度判定

判定：**对今天的收尾而言，功能闭环已完整、可作为「可选特性」安全合入；属于「已实现、默认关闭、可灰度」的完成度，而非发布阻断项。**

依据：

1. **默认行为安全（本地优先不退化）**
   - 仅当 `ORDERLY_INVENTORY_GATEWAY_ENDPOINT` 与 `ORDERLY_INVENTORY_GATEWAY_TOKEN` 同时配置时，`InventoryGatewayOptions.IsConfigured` 为真，才启用 `CloudInventoryWorkspaceService`。
   - 未配置时回落到 `StringNarrationInventoryWorkspaceServiceAdapter`：库存看板维持原有本地只读能力；导入/导出会抛出明确的「未配置」提示而非崩溃。
   - `MainViewModel` 还有 `EmptyInventoryWorkspaceService` 兜底，避免空引用。
   - 因此该特性对主线是纯增量，不改变本地优先口径。

2. **端到端闭环具备**
   - 看板刷新（`inventoryDashboard`）、Excel 预分析（`PrepareWorkbookImportAsync`，含表头别名映射、必填校验、新增/更新/未变化分类）、人工确认后批量同步（`inventoryBulkUpsert`）、同步后自动备份 + 回写原 Excel、手动导出云端快照（`inventoryExportRows`）均已实现，并有 UI 入口与确认/错误弹窗。
   - 网关侧有建表脚本与云函数、落地文档齐全。

3. **构建与回归无回退**
   - 含该特性的解决方案构建 0 警告 / 0 错误。
   - 发布前 QA smoke 全通过，未发现该特性引入的回归。

未完成/属于可选后续（非今天收尾阻断项）：

- 该特性**没有自动化测试覆盖**（与当前 `main` 无正式 tests project 的现状一致；属 `QA-only baseline` 既定边界）。云端路径依赖真实 gateway，未做联网回归（QA 口径明确不联网）。
- 网关错误/超时/鉴权失败目前以 `MessageBox` + 状态文案呈现，未做重试/离线队列等增强 —— 属体验增强类后续。
- 组 B 现金流响应式布局未做视觉终验（按规则 UI 终验不在本阶段）。

---

## 5. 受保护链路核验

本次审计与本次报告均未修改以下受保护链路（仅做只读核对，确认脏工作区文件分组与之无交集）：

- 微信支付回调验签与下单流程：未改动。
- 自动「已支付」状态流转：未改动。
- 微信履约/发货同步、支付—履约闭环：未改动。
- 小程序兼容行为：未改动。

库存特性走的是独立的 CloudBase 库存网关与本地库存看板，与上述支付/履约链路无代码交叉。

---

## 6. 阻断项

- 已修复的确认阻断项：无（本轮未改动任何代码）。
- 仍存在的阻断项：无。
- 唯一一次构建失败是环境性问题（运行中的 app 锁定输出二进制），停止进程后即恢复，非代码缺陷、非发布阻断项。

---

## 7. 今天收尾的功能就绪结论

- 在当前 `QA-only baseline` 口径下：**Orderly 功能上已就绪，可进行今天的收尾。**
- 库存云同步 / 库存工作区接入：作为「默认关闭、可选启用」的增量特性，功能闭环完整、不影响主线、构建与 QA 均通过，可随今天收尾一并保留合入。
- 仍由后续阶段负责、且不属于本次阻断项：UI/视觉/XAML 终验与 125% 缩放收尾、God-file 重构、库存特性的自动化测试与联网回归。

---

## 8. 下一步（独立任务，不在本次执行）

- God-file 拆分：详见 `docs/OPUS48_GOD_FILE_REFACTOR_CANDIDATES.md`。本次仅给出排名与「单个最安全的首拆候选」，不做任何重构。
