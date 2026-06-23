# QA Tools

正式 QA 脚本统一放在 `tools/qa`。

## 当前口径

- 日常开发收口与发布前验收分开执行
- 日常开发默认只验“本次修改点”相关链条，不默认跑整套 QA 回归
- 发布前统一入口见 `docs/ORDERLY_RELEASE_CHECKLIST.md`

## 日常开发收口

默认步骤：

```powershell
dotnet build Orderly.sln -c Debug
```

然后拉起最相关入口，只验证这次刚修改的点是否正确。

示例：

- 设置页改动：只验对应设置项的展示、保存、回显、状态文案
- 工作台改动：只验对应卡片、数据加载、跳转
- 登录/锁屏改动：只验登录、解锁、恢复、当前会话状态
- 单条服务或数据链路改动：只验这条链路的输入、落库、回读、界面结果

以下脚本不再作为每次日常改动后的默认必跑项：

- `run-p1-smoke.ps1`
- `run-commerce-smoke.ps1`
- `run-p2-full-regression.ps1`
- `run-p3-full-regression.ps1`

## 前置条件

- 已完成 Debug 构建：`src\Orderly.App\bin\Debug\net8.0-windows\Orderly.App.exe`
- 执行前关闭已有 `Orderly.App`
- 从 `powershell.exe` 启动也可以，脚本会自动切到 `pwsh`

## 发布前必跑命令

```powershell
dotnet build Orderly.sln -c Debug
dotnet test
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-universal-regression.ps1
git status --short
```

## 可选扩展回归

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-commerce-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-full-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-full-regression.ps1
```

## 脚本列表

- `run-qa-data-status.ps1`
  - 输出当前 QA 数据统计
- `reset-qa-data.ps1`
  - 清理并重建 QA 基线
- `clear-qa-data.ps1`
  - 只清理 QA 数据
- `run-uia-smoke.ps1`
  - P1 UIA 主链路 smoke；适合专项 UI 回归，不是默认日常收口
- `run-p1-write-smoke.ps1`
  - P1 非 UIA 写入链路 smoke（OrderService + NoteService + ActivityLog 校验）
- `run-p1-smoke.ps1`
  - `reset -> status -> write-chain -> UIA -> status`；适合专项 P1 回归，不是默认日常收口
- `run-p2-full-regression.ps1`
  - 顺序编排 `run-p2-1` 到 `run-p2-9`
- `run-p3-1-workbench-smoke.ps1`
  - 校验今日行动任务生成、排序稳定、QA reset 恢复
- `run-p3-2-pipeline-smoke.ps1`
  - 校验阶段推导、fallback、不落库、不破坏旧状态
- `run-p3-4-workbench-logic-smoke.ps1`
  - 校验深链字段补齐、去重、recently active 降噪、排序稳定
- `run-p3-5-search-smoke.ps1`
  - 校验统一搜索、`WorkbenchTask` 筛选、`QuickAction` 投影、QA reset 恢复
- `run-p3-6-navigation-smoke.ps1`
  - 校验 route service、`TargetSection / ActionHint` 收口、QuickAction 风险标记、ViewModel 安全定位
- `run-p3-full-regression.ps1`
  - `dotnet build -> P1 -> P2 full -> P3.1 -> P3.2 -> P3.4 -> P3.5 -> P3.6`
- `run-commerce-smoke.ps1`
  - 通用商业 smoke：在隔离 QA 库上按序执行 构建 -> 初始化库 -> 建工作区 -> 建客户 -> 建商品 -> 建库存项 -> 入库 -> 建订单与明细 -> 记收款 -> 推进履约 -> 完成订单并扣减库存 -> 记现金流 -> 生成看板指标 -> 生成经营建议；全部成功退出码 0，任一步失败即停并返回非 0
  - 可用 `-SkipBuild` 跳过构建步骤（由通用回归复用已构建产物）
  - 适合发布前或专项业务闭环回归，不是默认日常收口
- `run-universal-regression.ps1`
  - 通用回归统一入口：`dotnet build -> dotnet test -> 禁用词扫描 -> 安全 smoke -> 备份 smoke -> 通用商业 smoke`，快速失败并标识失败步骤；安全 smoke 与备份 smoke 均按现状调用、不做修改

## 边界

- 默认回归不联网，不调用真实 AI API
- 本目录结论不覆盖最终 UI / XAML 视觉验收
- 本目录结论不覆盖 125% 缩放收尾
- QA 运行产物默认写入 `%TEMP%\Orderly\artifacts\`，可用 `ORDERLY_QA_ARTIFACT_ROOT` 覆盖；不要写入仓库工作区
