# 成交助手产品总览

## 产品目标

成交助手是一个面向微信私域卖货、小型定制工作室、闲鱼卖家和高频咨询卖家的轻量成交管理小程序。

目标不是 AI 聊天，而是把「咨询 -> 记录 -> 报价 -> 跟单 -> 履约 -> 复购」做成低录入成本、可执行、可留痕的成交工作台。

## 用户角色

- 私域卖货商家：每天处理大量微信咨询，容易漏记、漏跟。
- 定制工作室：需求非标，需要沉淀客户偏好、尺寸、预算和风险信号。
- 闲鱼/小红书/抖音卖家：平台私信分散，需要统一看板和报价记录。

## 功能清单

- 工作台：今日新增、待跟进、已报价、成交、流失、待复购与高优先级任务预览。
- 极速新建：最少字段建客户和 deal。
- 粘贴板录入：读取/编辑剪贴板文本，规则解析，进入草稿确认。
- OCR 录入：选择图片、上传、OCR adapter、文本修正、解析确认。
- 草稿确认：展示原文、结构化字段、客户匹配建议，确认后入库。
- 成交看板：按 `dealStage` 映射列展示，支持筛选和快捷推进。
- Deal 详情：需求、报价、跟进、时间轴和状态动作集中处理。
- 客户列表/详情：长期档案、偏好、避雷点、历史 deal 和复购新建。
- 报价编辑/详情：SKU 选择、金额构成、发送报价、复制对外文案。
- 跟进任务：报价未回复、收货关怀、复购、手动/自定义任务处理。
- 统计：今日/近7天/近30天的成交指标、平台分布、模板使用和风险原因。
- SKU 管理：基础价、成本价、规格 schema、标签、启停和排序。
- 模板管理：按场景管理文案模板，支持变量渲染和预览。
- 设置：初始化 seed 数据，手动触发跟进扫描。

## 页面清单

- `pages/dashboard/dashboard`
- `pages/quick-create/quick-create`
- `pages/capture-clipboard/capture-clipboard`
- `pages/capture-ocr/capture-ocr`
- `pages/capture-confirm/capture-confirm`
- `pages/deals-board/deals-board`
- `pages/deal-detail/deal-detail`
- `pages/customers/customers`
- `pages/customer-detail/customer-detail`
- `pages/quote-edit/quote-edit`
- `pages/quote-detail/quote-detail`
- `pages/followups/followups`
- `pages/stats/stats`
- `pages/sku/sku`
- `pages/templates/templates`
- `pages/settings/settings`
