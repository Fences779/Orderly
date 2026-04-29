# P3.2 Pipeline Stage Summary

## 做了什么

- 新增 `PipelineStage` 和 `PipelineStageSnapshot`
- 新增 `IPipelineStageResolver` / `PipelineStageResolver`
- 新增 `PipelineStageRuleEngine`
- 在 `WorkbenchTask` 上附带只读 `PipelineStage`

## 没做什么

- 没把 `PipelineStage` 落库
- 没替换 `DealStage`
- 没替换 `OrderStatus`
- 没改现有保存逻辑

## 推导规则

- `New`
  - 无沟通、无更强阶段信号
  - 或存在订单/跟进但缺少更明确状态时走安全 fallback
- `Contacted`
  - 已有沟通记录
- `Interested`
  - 已有 AI 建议
  - 或 `Customer.LastContactAt` / 非消息类 `ActivityLog` 存在近期活跃信号
- `Quoted`
  - `OrderStatus.Quoted`
  - 或 `DealStage.Quoting / Negotiating`
  - 或存在 `PriceAdjustment` 报价信号
- `DraftPrepared`
  - `AiSuggestionStatus.DraftPrepared`
  - 或 `MetadataJson.autoReply.state = prepared / copied`
- `WaitingPayment`
  - `AiSuggestionStatus.Sent`
  - 或 `MetadataJson.autoReply.state = sent`
  - 或 `ActivityType.AutoReplySent`
- `Paid`
  - `OrderStatus.Won`
  - 或 `DealStage.Won`
- `Fulfilled`
  - `OrderStatus.Closed` 且同时存在成交/履约完成信号
- `Lost`
  - `DealStage.Lost`
  - 或 `OrderStatus.Closed` 且无成交完成信号

## Fallback 策略

- 客户不存在：回退 `New`，`UsedFallback = true`
- 有订单但缺少足够阶段信号：回退 `New`，`UsedFallback = true`
- 不抛异常、不写库、不修改原始状态

## 使用位置

- 当前用于 `WorkbenchTask` 展示阶段标签
- 还未扩展到详情区主阶段展示，避免和现有 `DealStage` 视觉混淆

## 已知限制

- 现有数据模型没有显式“待付款”字段，所以 `WaitingPayment` 目前主要依赖草稿已发送留痕。
- `Fulfilled / Lost` 都借用 `OrderStatus.Closed` 结合上下文区分，属于启发式推导。
- 规则是 projection，不应被外部代码当成权威业务状态写回。
