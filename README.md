# 成交助手 Orderly / Deal Assistant

## 项目定位

Orderly 是一个独立的 Windows 桌面端成交管理工具，主体为 WPF/.NET 应用，不是传统 CRM，也不是 AI 聊天工具。

它面向微信私域卖家、定制工作室、闲鱼卖家等轻量成交场景，用于管理：

- 咨询
- 需求记录
- 报价
- 跟进
- 履约
- 复购提醒

核心目标是把零散咨询和成交推进，整理成轻量、清晰、可追踪的工作流。

## 当前状态

- 当前版本：P2 Closeout Candidate ✅
- 已完成：P1 主工作台链路，以及 P2 沟通记录、OCR 转记录、AI 建议、回复草稿、人工复制发送、本地备份、受控恢复与恢复预览
- 待确认：P2 全量 smoke 与 closeout 验证结果以 `docs/P2_CLOSEOUT_SUMMARY.md` 为准
- 当前 README 仅描述 PC 端 WPF 项目，不再描述旧的小程序工程入口

## 技术栈

- .NET 8
- WPF
- C#
- SQLite
- MVVM
- 本地优先数据存储

## 工程结构

- `Orderly.sln`：解决方案入口
- `src/Orderly.App`：桌面应用入口、视图、ViewModel、交互层
- `src/Orderly.Core`：核心模型、仓储接口、服务接口
- `src/Orderly.Data`：SQLite 数据访问、仓储实现、基础服务实现
- `src/Orderly.Infrastructure`：桌面端基础设施能力与外部适配
- `docs/`：产品、流程、部署、测试等文档
- `artifacts/`：阶段性验证产物与截图

## 快速运行

1. 安装 .NET 8 SDK。
2. 使用 Visual Studio、Rider 或 VS Code 打开 `Orderly.sln`。
3. 还原依赖：
   `dotnet restore Orderly.sln`
4. 构建解决方案：
   `dotnet build Orderly.sln`
5. 启动桌面应用：
   - 直接在 IDE 中启动 `Orderly.App`
   - 或按仓库现有脚本使用 `start-orderly.bat`
6. 如果需要本地开发观察流程，可按仓库现有脚本使用 `dev-watch.bat`

## 核心产品链路

咨询捕获 → 客户/订单记录 → 报价 → 跟进 → 履约 → 复购提醒

P2 当前补充可用链路：

- 手工录入沟通记录
- OCR 结果转沟通记录
- 本地 AI 建议生成与接受/拒绝
- 本地回复草稿准备、复制、人工确认已发送
- 本地 JSON 备份导出、校验、恢复预览、空库/QA-only 受控恢复

## 关键约束

- `dealStage` / 交易阶段是主状态来源之一
- `capture` 必须先落草稿，确认后再写入 `customer` / `deal`
- `quote` 报价对象应独立存在，一个 `deal` 可有多次报价
- 跟进任务需要 `dedupe key`，避免重复扫描和重复提醒
- 当前 OCR / AI 能力如未真实接入，应按 mock / provider 降级处理，不应描述为已生产可用
- P2 不自动发送，不控制微信/闲鱼窗口，不连接云同步，不开放非空生产库覆盖恢复

## 验收产物

`artifacts/p1_2_2_final_validation/` 目录当前可作为阶段验收参考，包含：

- 登录页截图
- 主工作台截图
- 客户订单页截图
- 话术库页截图

## 非目标

- 当前 README 不描述微信小程序部署流程
- 当前项目不是完整 CRM
- 当前项目不是纯 AI 聊天机器人
- 当前阶段不承诺云端同步、多端协作、生产级 OCR
