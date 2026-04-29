# P3.6 Navigation Route Summary

## 范围

- 本轮只做逻辑、数据层、Service、Projection、ViewModel 非视觉接入。
- 没有修改任何 `.xaml`。
- `src/Orderly.App/Views/MainWindow.xaml` 未改动。
- UI、视觉、交互体验、125% 缩放统一留到项目最后处理。

## 路由模型

- 新增 Core 只读模型：
  - `NavigationTarget`
  - `NavigationTargetSection`
  - `NavigationActionHint`
  - `NavigationRouteResult`
  - `NavigationSemantics`
- 新增 Core Service：
  - `INavigationRouteService`
- 新增本地实现：
  - `src/Orderly.Data/Services/LocalNavigationRouteService.cs`

## TargetSection 语义

- `Customer`
- `Order`
- `Conversation`
- `AiSuggestion`
- `Ocr`
- `FollowUp`
- `ActivityLog`

## ActionHint 语义

- `OpenCustomer`
- `OpenOrder`
- `ReplyToCustomer`
- `ReviewSuggestion`
- `ReviewDraft`
- `CopyDraft`
- `MarkSent`
- `ConvertOcrToMessage`
- `CompleteFollowUp`
- `SnoozeFollowUp`

## 一致性收口

- `WorkbenchTask.DraftNotSent` 统一为 `AiSuggestion / ReviewDraft`。
- `Ocr` 路由统一为 `ConvertOcrToMessage`，保留 `ConvertOcr` 兼容解析。
- `ActivityLog` 搜索结果统一使用 `TargetSection = ActivityLog`。
- `QuickAction` 现在补齐：
  - `Type`
  - `TargetSection`
  - `ActionHint`
  - `IsEnabled`
  - `DisabledReason`
  - `CustomerId / OrderId / RelatedEntityType / RelatedEntityId`
  - `RequiresUserAction`

## 高风险动作

以下动作只返回 `RequiresUserAction = true`，不自动执行：

- `CopyDraft`
- `MarkSent`
- `ConvertOcrToMessage`
- `CompleteFollowUp`
- `SnoozeFollowUp`

原因：

- 这些动作会影响外部发送、OCR 转消息、跟进状态或用户剪贴板。
- 本轮目标是“稳定定位”，不是“自动代做”。
- 先把路由语义和 QA 做稳，再留给最终 UI 显式确认。

## ViewModel 行为

- `OpenSearchResultCommand` 和 `OpenWorkbenchTaskCommand` 统一先走 `INavigationRouteService`。
- `MainViewModel` 新增：
  - `CurrentNavigationTarget`
  - `LastNavigationStatus`
  - `LastNavigationError`
- 命令只做安全定位：
  - 选择客户
  - 选择订单
  - 同步 `SelectedAiSuggestion`
  - 同步 `CurrentOcrResult`
- 不会自动：
  - 发送
  - 复制真实剪贴板
  - OCR 转消息
  - 完成跟进
  - 延期跟进

## QA 结果

2026-04-29：

- `dotnet build Orderly.sln -c Debug`：PASS
- `run-p2-full-regression.ps1`：PASS
- `run-p3-1-workbench-smoke.ps1`：PASS
- `run-p3-2-pipeline-smoke.ps1`：PASS
- `run-p3-4-workbench-logic-smoke.ps1`：PASS
- `run-p3-5-search-smoke.ps1`：PASS
- `run-p3-6-navigation-smoke.ps1`：PASS
- `run-p1-smoke.ps1`：FAIL
- `run-p3-full-regression.ps1`：FAIL

失败原因：

- 两者都被既有 UIA `SendWait` 异常阻塞。
- 失败发生在 P1 UI 自动化，不是本轮路由逻辑或 build 错误。

## 已知限制

- `ActivityLog / Conversation / FollowUp` 目前只有路由模型和安全定位，没有最终 UI 消费层。
- `ActivityLog` 结果当前主要依赖客户/订单 fallback 定位，最终 UI 还未接入专门焦点态。
- `run-p3-full-regression.ps1` 仍然先跑 `run-p1-smoke.ps1`，所以会被现有 UIA 问题提前截断。

## 下一步建议

- P3 closeout 先处理 `run-uia-smoke.ps1` 的 `SendWait` 环境稳定性。
- 然后把最终 UI 只消费 `NavigationTarget / NavigationRouteResult`，不要再新增平行路由逻辑。
- closeout 前再跑一次 `build -> P1 -> P2 -> P3 full -> P3.6`。
