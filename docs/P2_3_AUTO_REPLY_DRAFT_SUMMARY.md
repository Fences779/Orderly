# P2.3 Auto Reply Draft Summary

## 1. 本轮做了什么

- 在现有 `AI 建议` 卡片内新增最小“准备回复草稿”闭环。
- 复用现有 `IAutoReplyService` / `LocalAutoReplyService`，打通：
  - 准备本地回复草稿
  - 标记草稿已发送
  - 拒绝草稿
- 复用现有 `AiSuggestion` 作为草稿承载体，没有新增独立草稿表。
- 为 `AiSuggestion` 小步扩展 `DraftPrepared` 状态，明确区分“AI 建议草稿”和“本地回复草稿 / 未发送”。
- 所有草稿状态变化均写入 `ActivityLog`。
- 新增 `tools/qa/run-p2-3-auto-reply-smoke.ps1`，覆盖 Service / Repository 级专项 QA。

## 2. 本轮没做什么

- 没有接真实 AI API。
- 没有接真实微信 / 闲鱼 / 平台发送。
- 没有做 OCR。
- 没有做同步。
- 没有做 Provider 接入。
- 没有做复杂模板系统。
- 没有做多轮聊天。
- 没有大改 UI。
- 没有重构订单主链路。

## 3. UI 入口在哪里

- 入口位置：`工作台` 页签，右侧详情栏，`AI 建议` 区块的单条建议卡片内。
- 用户操作按钮：
  - `准备回复草稿`
  - `标记已发送`
  - `拒绝草稿`
- UI 文件：
  - `src/Orderly.App/Views/MainWindow.xaml`
- ViewModel 命令入口：
  - `src/Orderly.App/ViewModels/MainViewModel.AutoReplyCommands.cs`
  - `src/Orderly.App/ViewModels/AiSuggestionListItem.cs`

## 4. 数据写入路径

- UI / ViewModel：
  - `MainViewModel -> IAutoReplyService`
- Service：
  - `src/Orderly.Data/Services/LocalAutoReplyService.cs`
- Repository：
  - `src/Orderly.Data/Repositories/AiSuggestionRepository.cs`
  - `src/Orderly.Data/Repositories/ActivityLogRepository.cs`
- SQLite 表：
  - `AiSuggestions`
  - `ActivityLogs`

说明：
- 本轮没有新增 `AutoReplyDrafts` 表。
- 草稿内容继续复用 `AiSuggestion.SuggestionText`。
- 草稿来源和本地未发送标记写入 `AiSuggestion.MetadataJson.autoReply`。
- `DealId` 不落在 `AiSuggestion` 上，仍通过 `Order -> DealId` 在写 `ActivityLog` 时补齐上下文。

## 5. 状态流转

- AI 建议原有流：
  - `Draft -> Accepted / Rejected`
- P2.3 回复草稿流：
  - `Draft -> DraftPrepared -> Sent`
  - `Draft -> DraftPrepared -> Rejected`
  - `Accepted -> DraftPrepared -> Sent / Rejected`

说明：
- `DraftPrepared` 表示“本地草稿 / 未发送”。
- `Sent` 只是本地标记，不执行任何外部发送。
- `Rejected` 在带有 `autoReply` 元数据时表示“草稿已拒绝”；记录保留不删除。

留痕规则：
- 准备草稿：`ActivityType.AutoReplyDraftPrepared`
- 标记已发送：`ActivityType.AutoReplySent`
- 拒绝草稿：`ActivityType.AutoReplyDraftRejected`

## 6. 为什么没有新增草稿模型

- 现有 `AiSuggestion` 已经具备：
  - 文本内容承载
  - 状态持久化
  - 与 `Customer / Order / Message` 的关联
  - `MetadataJson` 扩展位
- P2.3 只需要最小本地草稿闭环，不需要并行维护第二套草稿表和仓储。
- 因此本轮选择在现有 `AiSuggestion` 上小步扩展，减少状态源和重复逻辑。

## 7. QA 命令和结果

- Build：
  - `dotnet build Orderly.sln -c Debug`
  - 结果：通过
- P1 smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p1-smoke.ps1`
  - 结果：通过
- P2.3 auto reply smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p2-3-auto-reply-smoke.ps1`
  - 结果：通过

P2.3 smoke 覆盖点：
- 基于一条 `AiSuggestion` 准备本地回复草稿
- 通过 Repository 回读草稿状态与元数据
- 将草稿标记为已发送
- 将另一条草稿标记为拒绝
- 校验 `ActivityLog` 的准备 / 已发送 / 拒绝记录
- 校验 `reset-qa-data` 后 `AiSuggestions` 与 `ActivityLogs` 基线恢复稳定

UIA 覆盖说明：
- 本轮未新增单独的 P2.3 UIA 自动化。
- 已通过 `run-p1-smoke.ps1`，说明 P1 主链路未被破坏。
- P2.3 目前覆盖到 Service / Repository 级专项 QA，未覆盖按钮点击级 UIA。

## 8. 下一步 P2.4 建议

- 接入真实 AI Provider，但保留 `Local Stub` 作为离线降级路径。
- 在生成建议与回复草稿前补最小上下文裁剪规则。
- 为 Provider 调用补超时、失败态、用户可见降级提示与调用留痕。
