# 成交助手 Orderly / PC 成交助手

## 项目定位

Orderly 是一个独立的 Windows 桌面端成交管理工具，当前 `main` 主线为 `WPF + .NET 8 + SQLite`。

它面向微信私域卖家、定制工作室、闲鱼卖家等轻量成交场景，用于管理：

- 咨询记录
- 客户与订单
- 成交推进
- 跟进提醒
- 履约与复购

当前项目不是小程序发布仓库，也不是云函数主线工程；仓库中保留的 `miniprogram/`、`cloudfunctions/` 与相关旧文档仅作历史参考。

## 当前状态

- `P3` closeout 已完成：见 `docs/P3_CLOSEOUT_SUMMARY.md`
- `P4.1` 工程健康审计已完成：见 `docs/P4_ENGINEERING_AUDIT.md`
- 当前进行中：`P4.2A` 文档口径收口与发布验收入口统一
- 当前发布前基线为 `QA-only baseline`
- 当前 `main` 不包含正式 tests project，`dotnet test` 暂不作为必跑项
- UI / 视觉 / `MainWindow.xaml` / 125% 缩放验收不在当前阶段，后续交由 Antigravity

## 技术栈

- .NET 8
- WPF
- C#
- SQLite
- MVVM
- 本地优先数据存储

## 工程结构

- `Orderly.sln`：解决方案入口
- `src/Orderly.App`：桌面应用入口、View、ViewModel、交互层
- `src/Orderly.Core`：核心模型、接口、状态枚举
- `src/Orderly.Data`：SQLite 数据访问、仓储实现、投影与本地服务
- `src/Orderly.Infrastructure`：桌面基础设施适配
- `docs/`：阶段文档、QA 文档、发布验收文档、历史参考文档
- `tools/qa/`：QA 数据维护、smoke、regression 脚本

## 快速运行

1. 安装 .NET 8 SDK。
2. 打开 `Orderly.sln`。
3. 还原依赖：`dotnet restore Orderly.sln`
4. 构建：`dotnet build Orderly.sln -c Debug`
5. 启动：
   - 直接在 IDE 中启动 `Orderly.App`
   - 开发热重载：`dev-watch-sn.bat`
   - 上述脚本会先清理 `src\Orderly.App\*_wpftmp.csproj`，避免 `dotnet watch` 命中 `MSB1011`
   - 等价命令：`dotnet watch run --project .\src\Orderly.App\Orderly.App.csproj`
   - 普通启动：`start-sn.bat`
   - 兼容旧入口：`start-orderly.bat`

## 当前主线能力

- 客户 / 订单 / 成交推进 / 跟进 / 履约 / 复购提醒
- 本地沟通记录录入
- OCR 结果转记录
- AI 建议与回复草稿准备
- 人工复制发送确认
- 本地 JSON 备份、受控恢复、恢复预览
- 工作台任务、只读 `PipelineStage`、搜索 projection、快捷动作 projection、统一路由语义

## 当前发布验收入口

- 发布前统一入口：`docs/RELEASE_CHECK.md`
- QA 自动化说明：`docs/QA_AUTOMATION.md`
- QA 脚本入口：`tools/qa/README.md`

当前发布前至少执行：

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-qa-data-status.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-2-pipeline-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-5-search-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-6-navigation-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
git status --short
```

## 关键边界

- 当前 `main` 以 WPF + SQLite 代码与 QA 文档为准，不以旧小程序/云函数文档为准
- `PipelineStage` 是只读 projection，不替代核心状态源
- 当前发布基线是 `build + QA smoke scripts`
- 未恢复正式 tests project 前，`dotnet test` 不是当前 `main` 的发布前必跑项
- 当前阶段不做 UI / 视觉 / XAML 验收
- 当前阶段不接云同步、不自动发送、不开放生产库覆盖恢复

## 历史文档说明

以下文档保留为 `Legacy / Historical Reference`，不代表当前 `main` 发布基线：

- `docs/flow-and-state-machine.md`
- `docs/data-model.md`
- `docs/manual-test-checklist.md`
