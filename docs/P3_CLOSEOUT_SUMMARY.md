# P3 Closeout Summary

日期：2026-04-29
状态：P3 完成，closeout 验证通过

## 1. P3 完成范围

- P3.1：今日行动 / 待处理工作台基础投影
- P3.2：`PipelineStage` 只读阶段推导
- P3 QA / 文档：P3 专项 smoke 与 full regression 编排
- P3.4：`WorkbenchTask` 逻辑加固
- P3.5：全局搜索、`WorkbenchTask` 筛选、`QuickAction` projection
- P3.6：统一路由语义与 `NavigationRouteService`
- P3.6.1：P1 UIA smoke 稳定性加固，保证 P3 full regression 可稳定执行

## 2. 每个 P3 commit 对应内容

- `01ad22d` `feat: p3.1 workbench tasks`
  - 新增 `WorkbenchTask`、`WorkbenchTaskType`、`WorkbenchTaskPriority`
  - 新增 `PipelineStage`、`PipelineStageSnapshot`
  - 新增 `IPipelineStageResolver`、`PipelineStageResolver`、`PipelineStageRuleEngine`
  - 落地工作台任务基础投影和阶段推导
- `d833459` `chore: p3 qa and docs`
  - 新增 `docs/P3_1_WORKBENCH_TASKS_SUMMARY.md`
  - 新增 `docs/P3_2_PIPELINE_STAGE_SUMMARY.md`
  - 新增 `tools/qa/run-p3-1-workbench-smoke.ps1`
  - 新增 `tools/qa/run-p3-2-pipeline-smoke.ps1`
  - 新增 `tools/qa/run-p3-full-regression.ps1`
- `e6c8398` `feat: p3.4 workbench logic hardening`
  - 补齐 `WorkbenchTask` 深链字段
  - 完成 `RecentlyActiveCustomer` 降噪
  - 完成任务去重和稳定排序
  - 加固 `PipelineStage` fallback
- `97b6108` `feat: p3.5 search and action projections`
  - 新增 `SearchRequest / SearchResultItem / SearchResultSet`
  - 新增 `WorkbenchTaskFilter / WorkbenchTaskQuery`
  - 新增 `QuickAction / QuickActionType`
  - 落地统一搜索与快捷动作 projection
- `501e984` `feat: p3.6 navigation route hardening`
  - 新增 `NavigationTarget / NavigationTargetSection / NavigationActionHint / NavigationRouteResult / NavigationSemantics`
  - 新增 `INavigationRouteService / LocalNavigationRouteService`
  - 统一 `SearchResult / WorkbenchTask / QuickAction` 路由语义
- `bff98fb` `test: stabilize p1 uia smoke`
  - 加固 `run-uia-smoke.ps1`
  - 让 `run-p1-smoke.ps1` 与 `run-p3-full-regression.ps1` 稳定通过

## 3. 当前可用产品链路

1. 今日行动 / 待处理工作台：
   `ReplyNeeded / DraftNotSent / AiSuggestionPending / OcrNotConverted / FollowUpToday / FollowUpOverdue / RecentlyActiveCustomer`
2. `PipelineStage` 只读阶段推导：
   `New / Contacted / Interested / Quoted / DraftPrepared / WaitingPayment / Paid / Fulfilled / Lost`
3. 跟进任务链路：
   今日跟进、逾期跟进、深链字段定位、只读 route 解析
4. 搜索与动作链路：
   统一搜索 -> 结果 projection -> `QuickAction` projection -> route service 安全定位
5. 导航链路：
   `SearchResult / WorkbenchTask / QuickAction` 共用 `TargetSection / ActionHint` 语义，统一走 `INavigationRouteService`

## 4. P3 新增能力说明

- 工作台：
  - 新增 `WorkbenchTask` 只读投影
  - 支持深链字段：`CustomerId / OrderId / DealId / MessageId / AiSuggestionId / OcrResultId / FollowUpId / TargetSection / ActionHint / DedupeKey`
  - 支持去重和稳定排序
- 阶段推导：
  - 新增 `PipelineStage` 只读推导
  - 处理 `WaitingPayment`、`Fulfilled`、`Lost` 和缺失上下文 fallback
- 跟进与降噪：
  - 支持 `FollowUpToday / FollowUpOverdue`
  - `RecentlyActiveCustomer` 限 7 天、最多 5 条、同客户最多 1 条，并受更高优先级任务压制
- 搜索与筛选：
  - 搜索覆盖 `Customer / Order / ConversationMessage / AiSuggestion / OcrResult / FollowUp / ActivityLog`
  - 支持 `WorkbenchTaskFilter / WorkbenchTaskQuery`
- 快捷动作：
  - 只生成动作 projection，不直接执行副作用
  - 高风险动作统一要求 `RequiresUserAction = true`
- 路由语义：
  - `DraftNotSent` 统一为 `AiSuggestion / ReviewDraft`
  - `Ocr` 统一为 `ConvertOcrToMessage`
  - `ActivityLog` 搜索结果统一为 `TargetSection = ActivityLog`
  - `MainViewModel` 统一先走 route service，只做安全定位

## 5. 明确未做内容

- 未做最终 UI / XAML 接入
- 未做视觉 polish
- 未做 125% 缩放收尾
- 未接微信、闲鱼或其他平台联动
- 未自动发送
- 未接云同步
- 未开放生产库覆盖恢复
- 未改数据库 schema
- 未改 `src/Orderly.Data/Sqlite/DatabaseInitializer.cs`
- 未合入 `p4/customer-import-export-wip`

## 6. QA 命令和结果

已执行命令：

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-full-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-full-regression.ps1
```

结果：

- `dotnet build Orderly.sln -c Debug`：PASS
- `run-p1-smoke.ps1`：PASS
- `run-p2-full-regression.ps1`：PASS
- `run-p3-full-regression.ps1`：PASS

`run-p3-full-regression.ps1` 内部顺序执行并全部 PASS：

- `run-p3-1-workbench-smoke.ps1`
- `run-p3-2-pipeline-smoke.ps1`
- `run-p3-4-workbench-logic-smoke.ps1`
- `run-p3-5-search-smoke.ps1`
- `run-p3-6-navigation-smoke.ps1`

## 7. 已知限制

- `PipelineStage` 仍是启发式只读 projection，不是权威业务状态。
- 搜索是本地聚合扫描，不是全文索引。
- `QuickAction` 当前只生成动作，不执行发送、OCR 转消息、完成跟进、延期跟进。
- 路由 smoke 只验证逻辑和非视觉状态，不验证最终 UI 焦点表现。
- 当前 closeout 回归不覆盖真实平台回执、云同步和生产库恢复。

## 8. Final UI 阶段说明

- Final UI 阶段只应消费现有 `WorkbenchTask`、`SearchResult`、`QuickAction`、`NavigationRouteResult` 和 ViewModel 状态。
- Final UI 阶段统一处理 `.xaml`、视觉 polish、信息层级、125% 缩放和最终焦点表现。
- Final UI 阶段不应反向推动 schema 变更，不应新增第二套路由逻辑，不应绕开 `INavigationRouteService`。

## 9. P4 建议方向

1. 继续保持 P3 main 冻结，只做最终 UI 收口，不掺入新业务能力。
2. 在独立分支推进 customer import/export，并把危险写入动作明确门控。
3. 把 database migration/migrator 作为单独风险项推进，先补验证再考虑合入。
4. tests project 作为后续工程化增强独立评估，不混入 P3 closeout 结论。

## 10. p4/customer-import-export-wip 隔离说明

- 隔离分支：`p4/customer-import-export-wip`
- 隔离提交：`739e47a` `wip: isolate customer import export and migration work`
- 该分支当前包含但未并入 main：
  - customer import/export
  - `DatabaseMigration.cs`
  - `DatabaseMigrator.cs`
  - `tests/Orderly.Tests/Orderly.Tests.csproj` 及相关测试文件
  - `MainWindow.xaml` 上的导入导出入口改动
- 当前 main 的 P3 closeout 不包含以上内容，且 QA 结论不覆盖该分支。
