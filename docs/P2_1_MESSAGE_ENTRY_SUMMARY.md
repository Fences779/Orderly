# P2.1 Message Entry Summary

## 1. 本轮做了什么

- 在主工作台右侧详情区新增了一个最小“消息 / 沟通记录”入口。
- 支持在当前上下文下手工输入一条消息内容并保存到 `ConversationMessages`。
- 保存后立即刷新当前上下文的最近消息列表。
- 最近消息列表只读展示，默认显示最近 8 条。
- 保持沿用现有 `IConversationService -> ConversationMessageRepository -> SQLite` 路径，不把 Repository 逻辑塞进 `MainViewModel`。
- 保持 `ActivityLog` 留痕：新增消息会继续写入 `ConversationMessageAdded`。
- 新增 `tools/qa/run-p2-1-message-smoke.ps1`，覆盖写入、读取、`reset-qa-data`、`qa-data-status`。

## 2. 本轮没做什么

- 没有接真实 AI。
- 没有接 OCR。
- 没有做自动发送。
- 没有做平台同步。
- 没有做消息编辑、删除、导入批处理。
- 没有做独立消息页面或复杂筛选。
- 没有改订单主链路，也没有重构 `MainViewModel` 大结构。

## 3. 入口在哪里

- 入口位置：`工作台` 页签，右侧详情栏，`ActivityLog` 上方的 `消息 / 沟通记录` 区块。
- 文件位置：`src/Orderly.App/Views/MainWindow.xaml`
- 交互方式：
  - 先选择当前客户或订单。
  - 在文本框输入消息内容。
  - 点击“保存沟通记录”。

## 4. 数据如何写入

- ViewModel 入口：`src/Orderly.App/ViewModels/MainViewModel.ConversationCommands.cs`
- Service：`src/Orderly.Data/Services/ConversationService.cs`
- Repository：`src/Orderly.Data/Repositories/ConversationMessageRepository.cs`
- 数据写入规则：
  - `CustomerId`：始终绑定当前 `SelectedCustomer`
  - `OrderId`：如果当前存在且匹配该客户的 `SelectedOrder`，则绑定当前订单
  - `DealId`：优先绑定当前订单上的 `DealId`，没有则回退当前 `SelectedDeal`
  - `Direction`：本轮默认记为 `Incoming`
  - `Channel`：本轮默认记为 `Manual`
  - `SenderName`：本轮默认记为当前客户名
- 读取规则：
  - 有当前订单时，优先按订单读取消息
  - 没有当前订单时，按客户读取消息
- 原因：
  - 现有 UI 已有稳定的 `SelectedCustomer <-> SelectedOrder` 同步链
  - 这是当前风险最低、侵入最小的上下文来源，不需要引入新状态源

## 5. QA 怎么跑

- Build：
  - `dotnet build Orderly.sln -c Debug`
- P1 smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p1-smoke.ps1`
- P2.1 message smoke：
  - `pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-p2-1-message-smoke.ps1`

P2.1 smoke 覆盖点：
- 通过 `ConversationService` 写入一条手工消息
- 通过 `ConversationService.ListByOrderAsync` 回读最近消息
- 通过 `--qa-data-status` 验证消息数量变化
- 通过 `--reset-qa-data` 验证 QA 基线可恢复

UIA 覆盖说明：
- 本轮没有新增单独的 P2.1 UIA 自动化步骤。
- 现有 `run-p1-smoke.ps1` 已通过，说明 P1 主链路未被破坏。
- P2.1 本轮至少完成了 Repository / Service 级专项 QA。

## 6. 下一步 P2.2 建议

- 在当前消息块上增加“生成建议”入口，但继续明确区分 `Local Stub` 与真实 AI。
- 把 `SelectedOrder` 上下文下的最近消息裁剪成稳定的 prompt 输入。
- 为建议生成增加最小状态流：`Draft / Accepted / Rejected`。
- 如果后续要支持人工记录“我方发消息”，再补 `Direction` 选择，不要和本轮最小入口混在一起。
