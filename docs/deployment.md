# 部署与运行说明

日期：2026-04-29
口径：当前 `main` 主线

## 范围

本文描述当前 `main` 的桌面端构建、运行和发布前验收准备。

旧小程序 / 云函数部署内容不再作为当前发布基线；仓库中的 `miniprogram/`、`cloudfunctions/` 目录仅作历史参考。

## 环境前置

- Windows 开发环境
- .NET 8 SDK
- 可用的 PowerShell

## 本地构建与启动

1. 还原依赖：

```powershell
dotnet restore Orderly.sln
```

2. 构建桌面应用：

```powershell
dotnet build Orderly.sln -c Debug
```

3. 启动方式：

- 在 IDE 中启动 `Orderly.App`
- 或运行 `start-orderly.bat`

## 发布前统一入口

当前发布前验收入口统一为：

- `docs/RELEASE_CHECK.md`
- `docs/QA_AUTOMATION.md`
- `tools/qa/README.md`

其中 `docs/RELEASE_CHECK.md` 是发布前必须遵循的主入口。

## 当前发布基线

- 当前主线采用 `QA-only baseline`
- `dotnet build` 是必跑项
- 当前 QA smoke 脚本是必跑项
- 当前 `main` 不存在正式 tests project
- 未恢复 tests project 前，`dotnet test` 暂不作为必跑项
- UI / 视觉 / XAML / 125% 缩放验收不属于当前阶段，由 Antigravity 后续负责

## 当前不应作为发布判断依据的内容

- 历史小程序页面路由
- 云函数上传部署步骤
- 旧 `capture / quote / dealStage` 文档流程
- 未合入 `main` 的分支能力

