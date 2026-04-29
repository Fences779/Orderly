# 数据模型

> Legacy / Historical Reference
>
> 本文描述的是旧小程序 / 云函数数据模型设计，只供历史追溯，不代表当前 `main` 的 SQLite 实现或发布基线。
>
> 当前主线请以 `README.md`、`docs/product-overview.md`、`docs/deployment.md`、`docs/RELEASE_CHECK.md`、`docs/QA_AUTOMATION.md` 为准。

所有正式业务数据以 `workspaceId` 隔离。第一版默认 `workspaceId = default`。

## customers

长期客户实体，存稳定的人和偏好。

字段：`_id`、`workspaceId`、`name`、`platform`、`externalUid`、`contactHandle`、`sourceChannel`、`profileTags`、`preferenceNotes`、`tabooNotes`、`riskNotes`、`totalOrders`、`totalSpent`、`lastContactAt`、`lastPurchaseAt`、`createdAt`、`updatedAt`、`createdBy`、`updatedBy`。

## deals

一次成交机会。看板、状态、报价、跟进都挂在 deal 上。

字段：`_id`、`workspaceId`、`customerId`、`title`、`sourceEntry`、`dealStage`、`priorityLevel`、`intentCategory`、`demandSummary`、`styleTags`、`materialTags`、`sizeSpec`、`colorPref`、`budgetMin`、`budgetMax`、`deadlineAt`、`urgencyLevel`、`latestQuoteId`、`nextFollowupAt`、`lastInteractionAt`、`followupCount`、`lossReason`、`archivedAt`、`createdAt`、`updatedAt`、`createdBy`、`updatedBy`。

## quotes

一次报价动作。一个 deal 可有多次 quote。

字段：`_id`、`workspaceId`、`dealId`、`quoteNo`、`quoteStatus`、`items`、`baseAmount`、`customFee`、`laborFee`、`shippingFee`、`discountAmount`、`depositRequired`、`totalAmount`、`validUntil`、`quoteNote`、`sentAt`、`respondedAt`、`createdAt`、`updatedAt`、`createdBy`、`updatedBy`。

金额公式：`totalAmount = baseAmount + customFee + laborFee + shippingFee - discountAmount`。

## sku_catalog

报价基座。

字段：`_id`、`workspaceId`、`name`、`category`、`basePrice`、`costPrice`、`specSchema`、`adjustableFields`、`tags`、`enabled`、`sortOrder`、`createdAt`、`updatedAt`。

## followup_tasks

一次可执行跟进行动。

字段：`_id`、`workspaceId`、`dealId`、`customerId`、`customerName`、`triggerType`、`triggerAt`、`dueAt`、`priorityScore`、`templateId`、`suggestedText`、`taskStatus`、`completedAt`、`resultType`、`dedupeKey`、`createdAt`、`updatedAt`、`createdBy`、`updatedBy`。

## message_templates

文案模板。

字段：`_id`、`workspaceId`、`sceneType`、`title`、`content`、`variables`、`toneStyle`、`enabled`、`useCount`、`createdAt`、`updatedAt`。

## captures

一次采集草稿。剪贴板、OCR、手动文本都先进入 capture，确认后才进入正式 customer/deal。

字段：`_id`、`workspaceId`、`sourceType`、`rawText`、`rawImageUrl`、`ocrText`、`parserResult`、`confidenceScore`、`confirmStatus`、`linkedCustomerId`、`linkedDealId`、`createdAt`、`updatedAt`、`createdBy`。

## activity_logs

关键动作留痕。

字段：`_id`、`workspaceId`、`entityType`、`entityId`、`actionType`、`beforeData`、`afterData`、`note`、`operatorId`、`createdAt`。

## 关系说明

- `customers._id` -> `deals.customerId`
- `deals._id` -> `quotes.dealId`
- `deals.latestQuoteId` -> `quotes._id`
- `deals._id` -> `followup_tasks.dealId`
- `captures.linkedCustomerId` -> `customers._id`
- `captures.linkedDealId` -> `deals._id`
- `activity_logs.entityType + entityId` 指向任意业务实体。

## 索引建议

项目根目录 `database.indexes.json` 已给出索引定义建议：

- `customers.platform + customers.externalUid`
- `customers.contactHandle`
- `deals.customerId + deals.createdAt`
- `deals.dealStage + deals.updatedAt`
- `quotes.dealId + quotes.createdAt`
- `followup_tasks.taskStatus + dueAt`
- `followup_tasks.dedupeKey`
- `captures.confirmStatus + createdAt`
- `activity_logs.entityType + entityId + createdAt`
