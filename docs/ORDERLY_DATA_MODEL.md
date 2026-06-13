# Orderly 数据模型与数据层

本文档描述 Orderly 的本地数据层：SQLite/SQLCipher 存储、每实体一表一仓储的结构、幂等的 schema 初始化、非破坏性迁移，以及自定义字段的保存边界校验。数据层位于 `Orderly.Data`，服务于 `Orderly.Core/Commerce` 中的通用领域模型。

## 存储与加密

- 业务数据存储于本地 SQLite 数据库，并通过 SQLCipher 进行全库加密。
- 每个 `BusinessWorkspace` 对应一个 SQLCipher 加密的 SQLite 数据库，存放于 `%LocalAppData%\Orderly` 单一目录下。
- 启动器数据库与多账号工作区结构沿用既有的本地安全子系统（P0 安全系统），在本数据层变更后保持零回归。

> 安全边界：全库加密、本地账号系统、启动器数据库、多账号结构、DPAPI 密钥保护、字段级敏感数据加密、备份 / 恢复、安全审计，以及 `LocalSessionContext` / `DataKey` 行为均必须无回归保留。当任一数据层变更与安全行为冲突时，以安全行为为准。

## 一实体一表一仓储

数据层为通用领域模型中的每个实体提供一张 SQLite/SQLCipher 表与一个仓储，每个仓储暴露其实体的创建、读取、更新、删除（CRUD）操作。

覆盖的实体（共 18 个）：`BusinessWorkspace`、`BusinessTemplate`、`CustomFieldDefinition`、`UnitDefinition`、`Product`、`ProductVariant`、`InventoryItem`、`InventoryMovement`、`Customer`、`CustomerContact`、`Order`、`OrderItem`、`PaymentRecord`、`CashFlowEntry`、`Supplier`、`BusinessTask`、`BusinessInsight`、`BusinessMetricSnapshot`。

## 列映射约定

- 每张表的列对应实体的顶层字段，且全部使用行业无关的中性命名。
- 审计列：`CreatedAt`、`UpdatedAt`（均非空、UTC）、`DeletedAt`（可空、UTC）。
- 生命周期列：对应 `EntityLifecycleStatus`（活跃 / 归档 / 删除）。
- 个性化列：单一可空字符串列 `CustomFieldsJson`，承载该实体的全部个性化数据。
- 工作区列：业务实体携带非空 `WorkspaceId`；系统 / 配置实体按其角色使用内置 / 系统标记、`TemplateId` 或可空 `WorkspaceId`。
- 金额列：以 `decimal` 存储，范围 −999,999,999.99…999,999,999.99，小数位精确为 2 位。

## 幂等的 Schema 初始化

- 应用初始化某工作区数据库时，数据层使用统一的 schema 初始化例程创建或更新通用 Commerce schema。
- 该例程是幂等的：对同一数据库运行两次或更多次后，schema 的最终状态与运行一次完全一致，且不抛出错误。
- 该特性由属性测试覆盖（对任意 N ≥ 1 次重复初始化，最终 schema 状态相同且无错误）。

## 自定义字段保存边界校验

- 实体本身在赋值时不校验 `CustomFieldsJson`，按原值存储。
- 服务层或仓储保存实体时，在持久化之前校验该实体的 `CustomFieldsJson`。
- 若 `CustomFieldsJson` 非空且不是格式良好的 JSON，保存被拒绝并返回“无效自定义字段内容”错误，已持久化的数据保持不变。

## 非破坏性、幂等的迁移

数据层支持从历史的“通用 CRM 数据”迁移到通用领域模型。迁移遵循以下不变量：

- **非破坏性**：迁移保留每一条既有源记录，不删除也不覆盖。
- **先备份**：在施加任何变更之前，先对源数据库创建完整备份。
- **备份失败即中止**：若所需备份无法创建，迁移在施加任何变更之前中止，源数据保持不变，并记录“因备份失败而未运行”的指示。
- **幂等**：对同一源数据运行两次或更多次迁移，产生相同的目标记录集，不产生重复迁移记录。
- **结果日志**：迁移完成或失败时写入日志，记录成功 / 失败以及迁移记录数。

### 历史 CRM 映射规则

| 源（历史通用 CRM） | 目标（通用领域模型） |
|---|---|
| `Customer` | `Customer` |
| `Order` | `Order` |
| `Deal` | 按文档化规则映射为 `Order`、`BusinessTask` 或一条备注 |
| `FollowUp` | `BusinessTask` |
| `CustomerNote` | 一条备注 |
| `ActivityLog` | 原样保留，不变更 |

数据层不迁移历史的客户专属或行业专属远端数据模型，并使此类记录保持不读取、不修改。

迁移行为由自动化测试覆盖，验证非破坏性、幂等可重复性，以及上述历史实体映射。

## 用户数据安全

任何清理或迁移步骤都不得删除真实用户本地数据。迁移在变更前备份，校验仅拒绝单次保存而不影响既有数据，全库加密则保证落盘数据始终受保护。
