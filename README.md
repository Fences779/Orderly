# 成交助手 Deal Assistant

一个独立的微信原生小程序工程，用于把私域咨询、需求记录、报价、跟进、履约和复购提醒串成轻量成交管理流程。

## 工程入口

- 小程序目录：`miniprogram/`
- 云函数目录：`cloudfunctions/`
- 产品与部署文档：`docs/`
- 数据库索引建议：`database.indexes.json`

## 快速运行

1. 用微信开发者工具导入当前目录。
2. 在「云开发」里创建环境，复制环境 ID。
3. 修改 `miniprogram/config/appConfig.js` 的 `cloudEnv`。
4. 右键 `cloudfunctions` 下每个云函数目录，选择「上传并部署：云端安装依赖」。
5. 在小程序「设置」页点击「初始化演示数据」。
6. 回到首页，按 `docs/manual-test-checklist.md` 跑完整链路。

## 关键约束

- `dealStage` 是唯一主状态源。
- capture 必须先落草稿，确认后才写入 customer/deal。
- 报价是独立 `quote` 对象，一个 deal 可有多次报价。
- OCR 默认使用 mock 降级 provider，链路不中断。
- 跟进任务由规则生成，并使用 dedupe key 保证扫描幂等。
