# P2 Roadmap

## P2.0: 底座骨架

- 会话消息模型
- AI 建议模型
- OCR 结果模型
- 同步记录模型
- Repository / Service 合同
- 本地 Stub 实现
- SQLite 表与 QA 扩展

说明：
- 本轮不是 AI / OCR / 自动回复 / 云同步的真实实现。

## P2.1: 聊天消息手工录入 / 导入

- 手工录入消息
- 按当前客户 / 订单读取最近消息
- 为后续 AI 建议提供上下文

P2.1 当前落地范围：
- 已在主工作台右侧详情区增加“消息 / 沟通记录”最小入口。
- 已支持默认按“客户来消息 + 手工录入”写入 `ConversationMessages`。
- 已支持按当前订单优先、客户兜底读取最近消息。
- 已补充 `tools/qa/run-p2-1-message-smoke.ps1`。

P2.1 当前明确未做：
- 未接真实 AI Provider。
- 未接 OCR。
- 未做自动发送。
- 未做平台同步。
- 未扩展复杂消息筛选、导入、编辑、删除。
- 未改订单主链路和大结构。

## P2.2: AI 建议本地闭环

- 手动触发生成本地 Stub 建议
- 建议写入 `AiSuggestions`
- 按当前订单优先、客户兜底读取最近建议
- 支持 `Draft -> Accepted / Rejected` 状态闭环
- 生成、接受、拒绝全部写 `ActivityLog`

P2.2 当前落地范围：
- 已在主工作台右侧详情区、`消息 / 沟通记录` 下方增加最小 `AI 建议` 入口。
- 已接入现有 `LocalAiAssistantService`，明确标记为 `Local Stub`，不调用真实 AI。
- 已支持手动点击“生成建议”后持久化到 `AiSuggestions`。
- 已支持最近建议只读展示，并对 `Draft` 状态执行“接受 / 拒绝”。
- 已补充 `tools/qa/run-p2-2-ai-suggestion-smoke.ps1`，覆盖生成、回读、接受、拒绝、`ActivityLog` 留痕、`reset-qa-data` 恢复。

P2.2 当前明确未做：
- 未接真实 AI Provider。
- 未做 Prompt 配置和上下文裁剪策略扩展。
- 未做自动发送。
- 未做 OCR。
- 未做平台同步。
- 未做多轮聊天。
- 未改订单主链路和大结构。

## P2.3: 自动回复草稿流

- 基于现有 `AI 建议` 准备本地回复草稿
- 草稿写回 `AiSuggestions`
- 支持 `DraftPrepared -> Sent / Rejected` 本地状态闭环
- 准备 / 标记已发送 / 拒绝全部写 `ActivityLog`

P2.3 当前落地范围：
- 已复用现有 `IAutoReplyService` / `LocalAutoReplyService`。
- 已在 `AI 建议` 卡片内新增“准备回复草稿 / 标记已发送 / 拒绝草稿”最小入口。
- 已复用 `AiSuggestion` 承载草稿内容与状态，不新增独立草稿表。
- 已明确标记本地草稿 / 未发送，不连接真实微信 / 闲鱼 / 平台发送。
- 已补充 `tools/qa/run-p2-3-auto-reply-smoke.ps1`，覆盖准备、回读、标记已发送、拒绝、`ActivityLog` 留痕、`reset-qa-data` 恢复。

P2.3 当前明确未做：
- 未接真实 AI Provider。
- 未接真实外部消息发送。
- 未做多轮聊天。
- 未做复杂模板系统。
- 未做 OCR。
- 未做平台同步。
- 未改订单主链路和大结构。

## P2.4: AI Provider 架构 + OpenAI-compatible 接入 + Local Stub fallback

- 保留 `IAiAssistantService` 业务入口
- 新增 Provider 层抽象与 `OpenAI-compatible` 最小实现
- 默认 `Local Stub`
- Provider 缺配置 / 超时 / HTTP 错误 / JSON 解析失败 / 空文本时自动 fallback
- 补最小上下文裁剪与 metadata 留痕
- 新增 `tools/qa/run-p2-4-ai-provider-smoke.ps1`

## P2.5: OCR 截图识别

- 手动选择图片 / 导入截图
- 创建 `OcrResults`
- 无真实 OCR 时走 `Local fallback`
- UI 展示 OCR 状态和文本
- 支持转为 `ConversationMessages`
- 写 `ActivityLog`
- 新增 `tools/qa/run-p2-5-ocr-smoke.ps1`

P2.5 当前落地范围：
- 已在主工作台右侧详情栏 `消息 / 沟通记录` 附近增加最小 OCR 入口。
- 已支持按 `SelectedCustomer` + 当前订单上下文创建 `OcrResult`。
- 已支持本地 fallback 文本：`【本地OCR占位】请人工确认截图内容后转为沟通记录。`
- 已支持 `Pending / Completed / Failed` 状态展示。
- 已支持将已完成且非空的 OCR 文本转为 `ConversationMessages`，并刷新消息列表。
- 已支持 `OcrTaskCreated / OcrTaskCompleted / OcrTaskFailed / ConversationMessageAdded` 留痕。
- 已通过 `convertedToMessageId` + `SourceMessageId=ocr-result:{id}` 尽量避免重复转换。
- 已补充 `tools/qa/run-p2-5-ocr-smoke.ps1`，覆盖创建、fallback、回读、转消息、`ActivityLog`、`metadata`、`reset-qa-data` 恢复。

P2.5 当前明确未做：
- 未做自动截屏。
- 未监听微信 / 闲鱼 / 任何平台窗口。
- 未接真实 OCR API 或本地 OCR 引擎。
- 未做批量导入、拖拽、图片预览、复杂视觉美化。
- 未做自动发送。
- 未做同步。
- 未改订单主链路和大结构。

## P2.6: 人工确认发送闭环 / 复制发送辅助

- 基于现有 `AI 建议 / 回复草稿` 增加“复制草稿 -> 手动粘贴发送 -> 标记已发送”闭环
- 复制动作只写系统剪贴板，不执行任何外部发送
- `AiSuggestionStatus` 继续复用 `DraftPrepared / Sent`，不新增 `Copied` 状态枚举
- `MetadataJson.autoReply` 补充 `copiedAt / copiedBy / sentAt / sentBy / deliveryMode`
- `ActivityLog` 补充草稿复制与手动确认发送留痕
- 新增 `tools/qa/run-p2-6-manual-send-smoke.ps1`

P2.6 当前落地范围：
- 已在 `工作台 -> 右侧详情栏 -> AI 建议` 区块增加 `复制草稿` 按钮。
- 已明确限制为手动复制发送：本软件不会自动发送、不会控制微信 / 闲鱼窗口。
- 已要求先复制草稿，再允许点击 `标记已发送`，避免直接跳过人工发送步骤。
- 已复用 `AiSuggestion` / `IAutoReplyService` / `LocalAutoReplyService` / `ActivityLog`，没有新增独立发送表。
- 已补充 `tools/qa/run-p2-6-manual-send-smoke.ps1`，覆盖复制、metadata、`ActivityLog`、标记已发送、`reset-qa-data` 恢复。

P2.6 当前明确未做：
- 未接真实外部发送。
- 未控制微信 / 闲鱼 / 平台窗口。
- 未监听平台回执。
- 未接平台 API。
- 未做自动同步。
- 未做自动截屏。
- 未做复杂发送历史页。
- 未做全局快捷键监听。
- 未改订单主链路和大结构。

## P2.7: 本地备份 / 恢复 / 同步边界

- 设置页低频区新增 `导出备份 / 校验备份 / 最近备份状态`
- 新增 `IBackupService / LocalBackupService`
- 本地导出 `JSON` 备份，不接云端、不接平台同步
- 备份覆盖核心业务表：`Customers / Deals / Orders / FollowUps / CustomerNotes / PriceAdjustments / ActivityLogs / ConversationMessages / AiSuggestions / OcrResults`
- 导出成功写 `SyncRecord(local-backup)` 与 `ActivityLog(BackupExported)`
- 校验成功 / 失败写 `ActivityLog(BackupValidationSucceeded / BackupValidationFailed)`
- 新增 `tools/qa/run-p2-7-backup-smoke.ps1`

P2.7 当前落地范围：
- 已在 `设置` 中增加本地备份最小入口，避免进入订单主成交流。
- 已支持用户手动导出 `orderly-backup-yyyyMMdd-HHmmss.json`。
- 已支持校验 JSON 结构、关键表、counts、checksum。
- 已支持从最近一次 `SyncRecord(local-backup)` 读取最近备份状态。
- 已明确限制为本地文件导出 / 校验，不做云同步、平台同步、多设备同步。
- 已补充 `docs/P2_7_LOCAL_BACKUP_SUMMARY.md` 与 `tools/qa/run-p2-7-backup-smoke.ps1`。

P2.7 当前明确未做：
- 未做云端备份。
- 未做微信 / 闲鱼 / 任何平台同步。
- 未做多设备实时同步。
- 未做账号体系。
- 未做复杂冲突解决。
- 未做自动上传。
- 未开放生产库覆盖恢复。
- 未改订单主链路和大 UI 结构。

## 边界声明

- AI 不是 P2.0 真实实现。
- OCR 不是 P2.0 真实实现。
- 自动回复不是 P2.0 真实实现。
- 云同步不是 P2.0 真实实现。
