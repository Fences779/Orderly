# Orderly 发布前验收清单

本文档是 Orderly 发布前的统一验收入口，汇总构建、测试、QA 脚本、受限词扫描、安全保留与核心业务流（Core_Flow）等放行门禁。所有门禁全部通过，方可视为可发布。

## 门禁总览

| 门禁 | 要求 | 命令 / 依据 |
|---|---|---|
| 构建 | 零错误、无新增未解决告警 | `dotnet build Orderly.sln -c Debug` |
| 测试 | 零失败、无新增无理由跳过 | `dotnet test` |
| 受限词扫描 | 扫描结果零命中 | `ForbiddenTermsRegressionTests` |
| 安全保留 | 安全套件零失败、零新增跳过 | P0 安全自动化套件 |
| QA 脚本 | 全部终端成功 | `tools/qa` 下的 smoke / regression 脚本 |
| 核心业务流 | 端到端可运行 | Core_Flow（见下） |

## 1. 构建门禁

```powershell
dotnet build Orderly.sln -c Debug
```

要求：构建以零错误完成，且不引入新的未解决告警。

## 2. 测试门禁

```powershell
dotnet test
```

测试工程为 `tests/Orderly.Tests`，包含 `Commerce`、`Inventory`、`Cashflow`、`Customers`、`Templates`、`Analytics`、`Regression` 目录，组合了属性测试（对生成输入验证通用正确性，每个属性最少 100 次迭代）、示例 / 单元测试、集成测试与受限词回归测试。

要求：零失败，且无新增无理由跳过。

## 3. 受限词扫描门禁

受限词回归测试 `ForbiddenTermsRegressionTests` 扫描 `src/`、`tests/`、`tools/`、`README.md` 以及 `docs/` 目录中的每个文件。

- 扫描范围明确排除 Kiro 规格文件（`.kiro/` 下的 `requirements.md`、`design.md`、`tasks.md`），它们不属于生产主线。
- 受限词清单在运行时由片段拼接构造，使测试源文件自身不含任何字面受限词，因而不会被自身扫描误报。
- 零命中时通过；命中时失败，并逐条列出命中的文件路径与匹配项。

要求：扫描结果零命中。

> 文档约束：`docs/` 与 `README.md` 不得复制受限词的定义清单，以免被扫描到的文档反而触发生产受限词扫描。

## 4. 安全保留门禁（P0）

发布前重新运行既有的 P0 安全自动化套件，覆盖：

- SQLCipher 全库加密；
- 本地账号系统、启动器数据库、多账号结构；
- DPAPI 密钥保护与字段级敏感数据加密；
- 备份 / 恢复与安全审计；
- `LocalSessionContext` 与 `DataKey` 行为。

要求：相对转换前基线，套件报告零失败、零新增跳过。任何失败或新增跳过都视为回归，须保留 P0 安全行为。当任一转换步骤与安全行为冲突时，以安全行为为准。

## 5. QA 脚本门禁

发布前执行 QA 脚本，全部需返回终端成功：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-universal-regression.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-commerce-smoke.ps1
```

- `run-universal-regression.ps1` 串联：构建 → 测试 → 受限词扫描 → 安全 smoke → 备份 smoke → 经营 smoke。
- `run-commerce-smoke.ps1` 验证通用经营路径的冒烟可用性。
- 既有的 P0 安全 smoke 脚本原样保留运行。

QA 脚本入口与说明详见 `tools/qa/README.md`。

## 6. 核心业务流门禁（Core_Flow）

发布前需验证如下端到端流程在本地可完整运行：

1. 创建客户
2. 创建商品
3. 创建库存项
4. 记录入库变动
5. 创建订单
6. 添加订单项
7. 记录收款
8. 推进履约
9. 完成订单
10. 库存扣减
11. 生成现金流
12. 工作台指标刷新
13. 生成经营洞察

要求：流程端到端可运行；订单完成按 `InventoryItemId` 聚合扣减且在单一事务内原子完成；财务、库存与洞察记录按业务键幂等，不产生重复。

## 7. 工作树整洁

```powershell
git status --short
```

要求：无遗留的构建产物、临时残留文件，或引用受限词的历史脚本。

## 放行判定

以上七项门禁全部通过时，方可放行发布。任一门禁未通过，须先修复并重新运行相关门禁，直至全部通过。
