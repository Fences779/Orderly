# 流程与状态机

## 录入链路

主链路固定为：

`capture input -> parse -> customer match -> confirm -> persist`

实现位置：

- 剪贴板输入页：`miniprogram/pages/capture-clipboard`
- OCR 输入页：`miniprogram/pages/capture-ocr`
- 草稿确认页：`miniprogram/pages/capture-confirm`
- 解析云函数：`cloudfunctions/captureParse`
- 确认入库云函数：`cloudfunctions/captureConfirm`

规则：

- 解析结果先写入 `captures`，`confirmStatus = draft`。
- 不允许解析后直接污染 `customers` 或 `deals`。
- 确认页支持修改结构化字段，选择已有客户或新建客户。
- 确认后写入 customer/deal，更新 capture 链接字段，并写 `activity_logs`。

## dealStage 单一状态源

状态列表：

1. `new_inquiry`
2. `needs_clarification`
3. `quote_preparing`
4. `quote_sent`
5. `waiting_deposit`
6. `scheduled`
7. `in_production`
8. `ready_to_ship`
9. `shipped`
10. `received`
11. `completed`
12. `repurchase_due`
13. `dormant`
14. `lost`

小程序端状态常量：`miniprogram/constants/dealStages.js`。

云端统一状态入口：`cloudfunctions/dealStageUpdate`。

页面不能裸改 `dealStage`；必须调用 `dealService.updateStage`，由云函数完成：

- 合法流转校验
- `updatedAt` / `updatedBy` 更新
- activity log
- 报价未回复任务自动关闭
- 收货后关怀 / 复购任务触发

## 合法流转

- `new_inquiry` -> `needs_clarification` / `quote_preparing` / `lost`
- `needs_clarification` -> `quote_preparing` / `dormant` / `lost`
- `quote_preparing` -> `quote_sent`
- `quote_sent` -> `waiting_deposit` / `quote_preparing` / `dormant` / `lost`
- `waiting_deposit` -> `scheduled` / `lost`
- `scheduled` -> `in_production`
- `in_production` -> `ready_to_ship`
- `ready_to_ship` -> `shipped`
- `shipped` -> `received`
- `received` -> `completed`
- `completed` -> `repurchase_due` / `dormant`
- `repurchase_due` 不篡改旧 deal，复购用新 deal 承接。
- `dormant` 可重新激活到早期合适阶段。
- `lost` 为终态，重新开始通过复制/新建 deal。

## 报价链路

实现位置：

- 编辑页：`miniprogram/pages/quote-edit`
- 详情页：`miniprogram/pages/quote-detail`
- 云函数：`cloudfunctions/quoteCreateOrUpdate`

规则：

- 报价独立存 `quotes`。
- 一个 deal 可有多次 quote。
- 发送报价后更新 `deal.latestQuoteId`。
- 当前 `dealStage = quote_preparing` 时，发送报价自动推进到 `quote_sent`。
- 发送报价后生成 `quote_no_reply` 任务，dueAt 为发送后 24 小时。
- 报价保存/发送写入 `activity_logs`。

## 跟进生成规则

实现位置：`cloudfunctions/followupScan`。

类型：

- `quote_no_reply`
- `post_delivery`
- `repurchase`
- `manual`
- `custom`

幂等规则：

- 每条规则任务都有 `dedupeKey`。
- `quote_no_reply`：`dealId + triggerType + quoteId`
- `post_delivery`：`dealId + triggerType + 日期窗口`
- `repurchase`：`dealId + triggerType + 日期窗口`
- 重复扫描时先查 `dedupeKey`，已有则不重复创建。

优先级分：

`priorityScore = 场景基础分 + 超时分 + 紧急度分 + 客户价值分 - 近期打扰惩罚`

当前实现：

- 报价未回复：基础 40
- 收货后关怀：基础 30
- 复购提醒：基础 35
- 手动任务：基础 28
- 已超时：+10
- 超过 72h：再 +10
- 急单：+20
- 高价值/多次成交客户：+15
- 最近 3 天已联系：-20

## 复购逻辑

- `completed` 后默认 30 天生成 `repurchase` 任务。
- `repurchase_due` 是旧 deal 的执行提醒状态，不直接篡改为新成交。
- 真正复购通过客户详情或 deal 详情的“再建新 deal”承接。

## 统计口径

- 新增咨询：周期内创建的 deal 数。
- 已报价：周期内创建或发送 quote 的唯一 deal 数。
- 成交数：周期内进入 `scheduled` 及之后阶段的唯一 deal 数。
- 成交率：`成交数 / 新增咨询数`。
- 流失数：周期内进入 `lost` 的唯一 deal 数。
- 待跟进数：`followup_tasks.taskStatus = pending 且 dueAt <= now`。
- 待复购数：未完成 `repurchase` 任务数。
