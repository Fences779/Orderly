# 成交助手产品总览

日期：2026-04-29
口径：当前 `main` 主线

## 当前定位

Orderly 是一个面向 Windows 桌面端的成交助手，当前主线是 `WPF + SQLite` 的本地优先工作台，不是微信小程序交付物。

目标不是做完整 CRM，也不是做自动发送机器人，而是把轻量成交流程收口为可追踪、可回看、可执行的桌面工作流。

## 当前主线能力

- 客户、订单、成交推进、跟进、履约、复购提醒
- 本地沟通记录录入与管理
- OCR 结果转记录
- AI 建议、回复草稿、人工复制发送确认
- 工作台任务与今日行动
- `PipelineStage` 只读阶段推导
- 全局搜索、快捷动作、统一导航路由
- 本地备份、恢复预览、QA-only 受控恢复

## 当前发布基线

- 当前 `main` 的发布前验收基线是 `QA-only baseline`
- 以 `dotnet build` 和 `tools/qa` 中的 smoke 脚本为准
- 统一入口：`docs/RELEASE_CHECK.md`
- 当前未恢复正式 tests project，因此 `dotnet test` 暂不作为必跑项

## 当前明确不包含

- 小程序发布链路
- 云函数部署链路
- 云端同步
- 自动发送平台消息
- 生产库覆盖恢复
- Final UI / XAML / 视觉 polish / 125% 缩放验收

## 当前主要文档入口

- `README.md`：项目入口与当前状态
- `docs/deployment.md`：桌面端构建、运行与发布准备
- `docs/RELEASE_CHECK.md`：发布前统一验收入口
- `docs/QA_AUTOMATION.md`：QA 自动化说明
- `docs/P4_ENGINEERING_AUDIT.md`：P4.1 工程健康审计结论

## 历史参考说明

仓库中仍保留旧小程序 / 云函数 / `dealStage` 体系相关文档与目录，仅供历史追溯或后续比对，不代表当前 `main` 的实现或发布口径。

