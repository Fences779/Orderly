# P2.5 OCR Entry Summary

## 1. 本轮做了什么

- 在工作台右侧详情栏 `消息 / 沟通记录` 区块内增加了一个最小 OCR 子入口。
- 支持手动选择图片 / 导入截图，创建 `OcrResults` 并立即执行本地 fallback OCR。
- 无真实 OCR API / 引擎时，默认写入明确占位文本：
  - `【本地OCR占位】请人工确认截图内容后转为沟通记录。`
- 支持展示当前 OCR 文件名、状态、文本预览和转沟通记录按钮。
- 支持将已完成且文本非空的 OCR 结果转为 `ConversationMessages`。
- 转换后会刷新当前消息列表和活动时间线。
- 保持复用现有 `OcrResult` / `IOcrService` / `LocalOcrService` / `IConversationService` / `ConversationService` / `ActivityLog`。
- 新增 `tools/qa/run-p2-5-ocr-smoke.ps1`，覆盖离线 OCR fallback 闭环。

## 2. 本轮没做什么

- 没有做自动截屏。
- 没有监听微信、闲鱼或其他平台窗口。
- 没有接真实 OCR API、云 OCR、系统 OCR 引擎。
- 没有做自动发送。
- 没有做同步。
- 没有做批量导入、拖拽、图片预览、复杂美化。
- 没有重构订单主链路，也没有拆大结构。

## 3. UI 入口

- 入口位置：
  - `工作台` 页签
  - 右侧详情栏
  - `消息 / 沟通记录` 区块内，手工消息录入框上方
- 文件：
  - `src/Orderly.App/Views/MainWindow.xaml`
- 最小元素：
  - `选择图片 / 导入截图`
  - `OCR 状态`
  - `OCR 文本预览`
  - `转为沟通记录`

## 4. OCR 数据路径

- ViewModel 入口：
  - `src/Orderly.App/ViewModels/MainViewModel.OcrCommands.cs`
- OCR Service：
  - `src/Orderly.Data/Services/LocalOcrService.cs`
- OCR Repository：
  - `src/Orderly.Data/Repositories/OcrResultRepository.cs`
- Conversation Service：
  - `src/Orderly.Data/Services/ConversationService.cs`
- Conversation Repository：
  - `src/Orderly.Data/Repositories/ConversationMessageRepository.cs`

数据流：
- 选择图片后，先按当前上下文创建一条 `OcrResult`
- `CustomerId`：
  - 取 `SelectedCustomer`
- `OrderId`：
  - 优先当前订单
- `DealId`：
  - 转消息时优先当前订单 `DealId`，否则 `SelectedDeal`
- 本轮 OCR 执行不依赖公网：
  - 当前默认直接走本地 fallback 文本

## 5. 状态流转

- `Pending`
  - 选择图片并创建 `OcrResult`
- `Completed`
  - 本地 fallback 成功写入占位文本
- `Failed`
  - 图片不存在或 OCR 过程异常

ActivityLog：
- `OcrTaskCreated`
- `OcrTaskCompleted`
- `OcrTaskFailed`
- 转消息复用 `ConversationMessageAdded`

## 6. metadata

`MetadataJson` 最少包含：
- `source = "manual-image"`
- `createdBy = "p2.5"`
- `provider = "local"`
- `usedFallback = true`
- `fileName`
- `fileExists`
- `convertedToMessageId` 可选
- `errorSummary` 可选

说明：
- OCR 失败时会把 `errorSummary` 写回 `MetadataJson`
- OCR 成功转消息后，会回写 `convertedToMessageId`

## 7. 转沟通记录流程

- 只允许 `Completed` 且 `ExtractedText` 非空的 OCR 结果执行转换。
- 转换后默认写入：
  - `Direction = Incoming`
  - `Channel = Manual`
- 消息写入后：
  - 刷新当前消息列表
  - 刷新活动时间线
- 去重策略：
  - `ConversationMessages.SourceMessageId = ocr-result:{ocrId}`
  - `OcrResults.MetadataJson.convertedToMessageId`
- 当前已尽量避免重复转换。
- 仍然不是数据库事务级一次提交：
  - 如果极端情况下“消息已写入但 OCR metadata 回写失败”，理论上仍存在重复尝试风险，但常规重复点击已被拦住。

## 8. QA 结果

新增脚本：
- `tools/qa/run-p2-5-ocr-smoke.ps1`

覆盖点：
- 可创建 `OcrResult`
- Local fallback 可完成 OCR
- `OcrResult` 可读回
- OCR 文本可转 `ConversationMessage`
- `ConversationMessage` 可读回
- `ActivityLog` 有 OCR 创建 / 完成 / 转消息记录
- `MetadataJson` 包含 `provider / usedFallback / createdBy / source`
- `reset-qa-data` 后基线恢复稳定

UIA 覆盖说明：
- 本轮没有新增按钮级 UIA 自动化。
- 仍以 Service / Repository 级 smoke 为主。
- 总结里需要明确：本轮未补独立 UIA。

## 9. 下一步 P2.6 建议

- 在当前 OCR / AI / 草稿基础上，接“人工确认后发送”的半自动回复，不做自动发送。
- 把转消息后的 OCR 记录和 AI 建议串起来，形成更稳定的上下文来源。
- 如果要接真实 OCR，优先继续保留当前 local fallback，避免 QA 与 build 对真实 Provider 产生硬依赖。
