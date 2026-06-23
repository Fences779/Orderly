# Orderly QA 自动化说明

本文档记录 `tools/qa` 下 QA 自动化脚本的用途、隔离机制与运行方式，作为发布前 QA 门禁的可追溯依据。

> 说明：本页主要服务发布前或专项回归，不代表每次日常改动后的默认验收动作。日常开发默认只验证本次修改点相关链条。

## 运行环境约束

- QA 脚本要求 PowerShell 7（pwsh）以兼容 UTF-8 与 .NET 8；非 Core 版会自动转交 pwsh 执行。
- 运行前需先构建：`dotnet build Orderly.sln -c Debug`（脚本从 `bin/Debug/net8.0-windows` 加载产品程序集与原生 SQLCipher 库）。
- 运行前不得有正在运行的 Orderly.App 进程（脚本会断言）。

## 隔离与安全

- QA 入口受 P0 特权启动门禁约束：必须设置 `ORDERLY_RUNTIME_ENV=QA`（或等价的 dev/test/local）、`ORDERLY_ENABLE_PRIVILEGED_QA_STARTUP=1`，且 `ORDERLY_QA_DB_PATH` 必须位于 `ORDERLY_QA_DATA_ROOT` 指定的隔离临时目录之下。
- `run-universal-regression.ps1` 自行编排隔离 QA 环境：将上述环境变量指向临时目录，运行结束后还原环境变量并清理临时目录；并自检拒绝在真实用户数据目录（`%LocalAppData%\Orderly`）下执行破坏性重置。
- QA 数据库为 SQLCipher 加密库。脚本以产品一致的方式加载 QA 会话数据密钥（与应用相同的路径派生与 DPAPI 解保护），用带密钥的连接工厂打开加密库；密钥不写入任何日志。
- 涉及真实 `%LocalAppData%` 规范账号目录的安全 smoke，在结束时断言其临时账号目录已被清理，残留即判失败。

## 关键脚本

| 脚本 | 作用 |
|---|---|
| `run-universal-regression.ps1` | 串联：构建 → 测试（排除受限词）→ 受限词扫描 → 安全 smoke → 备份 smoke → 经营 smoke；任一步失败即快速失败并定位失败步骤。 |
| `run-commerce-smoke.ps1` | 在隔离 QA 库上端到端走通通用经营路径（客户 / 商品 / 库存 / 订单 / 收款 / 履约 / 完成扣库 / 现金流 / 工作台 / 经营洞察）。 |
| `run-p2-7-backup-smoke.ps1` | 本地备份导出 / 校验 / 篡改拒绝 / 审计记录 / 重置还原；备份在 SQLCipher 加密会话下导出，按返回清单而非明文文件断言结构。 |
| `run-p4-local-account-encryption-restore-smoke.ps1` | 真实字段加密的备份 → 恢复 → 重新登录 → 解密读取校验；结束时断言临时账号目录已清理。 |
| `run-uia-smoke.ps1` | 九个主导航（工作台 / 订单 / 商品 / 库存 / 客户 / 现金流 / 经营建议 / 设置 / 我的）逐页切换并校验内容区非空。适合专项 UI 回归，不作为默认日常收口。 |
| `reset-qa-data.ps1` | 通过应用重置 QA 数据，并以 SQLCipher 兼容方式清理 / 回填字段密文。 |

## 推荐运行

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/qa/run-universal-regression.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/qa/run-commerce-smoke.ps1
```

要求：全部脚本返回终端成功（exit 0）。脚本入口与更多说明见 `tools/qa/README.md`。
