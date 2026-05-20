# P4 Local Account Security Design

日期：2026-05-16  
状态：设计中  
适用主线：当前 `main` 的 `WPF + .NET 8 + SQLite` 桌面端  
范围边界：只覆盖本地账号、冷启动登录、睡眠恢复 PIN、本地数据加密；**不涉及** `miniprogram/`、`cloudfunctions/`、微信接口、云鉴权、远程同步

## 0. 决策摘要

这份文档是当前“本地多账号 + 本地加密 + 本地解锁”方案的**单一准入文档**。  
后续实现、评审、验收都以这一个文件为准，不再拆平行说明。

当前已定口径：

- 同一台电脑可有多个本地账号，但**账号之间完全隔离**
- 采用“`launcher.db` 注册库 + 每账号独立 `orderly.db`”方案
- 冷启动必须 `账号 + 主密码`
- 睡眠 / 休眠恢复且进程仍在时，只需 `6 位 PIN`
- 首个账号固定为 `Owner`
- 后续账号只能由 `Owner` 创建，普通用户不能自助注册
- 敏感字段做 `AES-GCM` 字段级加密
- 登录成功后系统内自动解密，业务界面继续按明文使用
- `Owner` 忘记主密码时，可用 `Recovery Key` 本地恢复
- 不碰微信、小程序、云函数、云账号、远程同步

## 1. 目标

解决同一台电脑上 2~3 个本地使用者共用 Orderly 时的 4 个问题：

- 软件启动后不能直接看到上一位用户的数据
- 不同本地账号的数据必须相互隔离
- 电脑重启后必须走“账户 + 主密码”登录
- 电脑睡眠 / 休眠恢复后，如果应用进程仍在，只需输入 6 位 PIN 解锁

同时满足：

- 敏感字段落盘加密
- 登录成功后系统内自动解密，可正常浏览、编辑、搜索
- 不把改动扩散到微信、小程序、云函数链路

## 2. 非目标

本设计 **不做**：

- 微信登录、手机号登录、云端账号体系
- 跨账号共享客户/订单/跟进数据
- 行级权限、字段级权限、审批流
- 内存防抓取、反调试、企业级终端安全
- 整库 SQLCipher 改造

V1 的权限模型只有一句话：

- **账号即隔离单元**

V1 的账号角色只有两种：

- `Owner`：首个主账号，可创建/禁用其他本地账号
- `Member`：普通账号，只能登录和使用自己的业务库

也就是：

- 账号 A 登录后只能看到账号 A 自己的本地业务库
- 账号 B 登录后只能看到账号 B 自己的本地业务库
- 不做“同库多账号 ACL”
- 不做“一个客户同时归属多个账号”
- 不做复杂 RBAC，只保留 `Owner / Member` 两级

这样范围最小，隔离最硬，也最不容易把现有业务代码改烂。

## 3. 核心决策

### 3.1 账号隔离方式

采用 **“本地账号注册库 + 每账号独立业务库”**。

原因：

- 当前业务表没有 `OwnerAccountId`、`WorkspaceId` 这类本地主账号隔离字段
- 如果在现有所有业务表里硬加权限字段，会波及所有仓储、查询、投影、搜索、备份和 QA 数据
- 每账号独立 SQLite 文件可以直接得到最强的本地隔离，不需要改业务查询语义

### 3.2 登录与解锁方式

- **冷启动登录**：应用进程不存在，或电脑重启后首次启动应用
  - 输入：`账号 + 主密码`
  - 成功后：打开对应账号业务库，解出该账号的 `DataKey`
- **热解锁**：电脑睡眠 / 休眠恢复，且应用进程仍然存活
  - 输入：`6 位 PIN`
  - 成功后：恢复当前会话 UI 访问
- **关闭应用后再次打开**
  - 一律按冷启动处理
  - 不允许只靠 PIN 重新进入

### 3.3 数据加密方式

采用 **敏感字段 AES-GCM 加密**，不做整库加密。

原因：

- 不引入新依赖
- 能控制改动范围在桌面端本地仓储层
- 满足“磁盘落盘不可直读 + 登录后自动解密”
- 不需要同时改造微信/云端/远程链路

## 4. 存储布局

### 4.1 启动器注册库

新增独立注册库：

- `%LocalAppData%\Orderly-SN\identity\launcher.db`

职责：

- 存本地账号清单
- 存主密码哈希、PIN 哈希
- 存每账号业务库路径
- 存每账号 `DataKey` 的加密包
- 不存业务客户/订单数据

### 4.2 每账号业务库

每个账号一份独立业务库：

- `%LocalAppData%\Orderly-SN\accounts\{accountId}\orderly.db`

职责：

- 存当前账号自己的业务数据
- 继续沿用现有 `Customers / Orders / FollowUps / ...` 表
- 只在本账号登录成功后被打开

### 4.3 路径示例

```text
%LocalAppData%\Orderly-SN\
  identity\
    launcher.db
  accounts\
    7f1f2f2d3f3f4b7e8a1c9b2d4e5f6a7b\
      orderly.db
    2a4d9e8c1b7f4c59a6d3e1f5b8c2d4e6\
      orderly.db
```

## 5. 账号与密钥模型

### 5.1 本地账号模型

注册库新增 `LocalAccounts` 表：

| 字段 | 说明 |
| --- | --- |
| `AccountId` | 账号主键，`GUID/32位hex` |
| `Username` | 登录名，唯一 |
| `DisplayName` | 展示名 |
| `PasswordHash` | 主密码哈希 |
| `PasswordSalt` | 主密码盐 |
| `PasswordIterations` | 主密码 PBKDF2 迭代次数 |
| `PinHash` | PIN 哈希 |
| `PinSalt` | PIN 盐 |
| `PinIterations` | PIN PBKDF2 迭代次数 |
| `RecoveryKeyHash` | 恢复密钥哈希，仅用于 `Owner` 主密码恢复 |
| `RecoveryKeySalt` | 恢复密钥盐 |
| `RecoveryKeyIterations` | 恢复密钥 PBKDF2 迭代次数 |
| `EncryptedDataKey` | 被主密码派生密钥包裹后的业务数据密钥 |
| `DataKeyNonce` | `AES-GCM` nonce |
| `DataKeyTag` | `AES-GCM` tag |
| `DatabasePath` | 该账号业务库绝对路径 |
| `Role` | `Owner` / `Member` |
| `IsEnabled` | 是否启用 |
| `CreatedAt` | 创建时间 |
| `UpdatedAt` | 更新时间 |
| `LastLoginAt` | 最近冷启动登录时间 |

### 5.2 主密码

主密码用于：

- 冷启动登录校验
- 解开 `EncryptedDataKey`

存储方式：

- `PBKDF2-SHA256`
- `Salt` 16~32 字节
- 迭代次数建议 `>= 200000`
- 只存哈希，不存明文

### 5.3 PIN

PIN 用于：

- 睡眠 / 休眠恢复后的热解锁

限制：

- 固定 6 位数字
- 不能替代主密码做冷启动登录
- 不能在应用关闭后恢复业务库访问

存储方式：

- 同样只存 `PBKDF2-SHA256` 哈希
- 独立于主密码，不复用盐值

### 5.4 DataKey

`DataKey` 是真正用于加密业务字段的随机密钥。

规则：

- 每个账号独立一把 `256-bit` 随机 `DataKey`
- 首次创建账号时生成
- 落盘前先用主密码派生密钥二次包裹
- 登录成功后解出到内存
- 应用关闭后丢弃

结果：

- 主密码负责“开箱”
- `DataKey` 负责“读业务数据”
- PIN 只负责“恢复当前会话 UI”

### 5.5 Recovery Key

`Recovery Key` 只用于 `Owner` 忘记主密码时的本地恢复。

规则：

- 仅在首次创建 `Owner` 时生成一次
- 建议为高熵随机字符串，例如 `24~32` 位分组码
- UI 只展示一次，由用户自行离线保存
- 系统只保存其哈希，不保存明文
- 不能用于登录
- 不能用于替代 PIN
- 只能用于重置 `Owner` 主密码并重新包裹 `EncryptedDataKey`

## 6. 登录 / 解锁状态机

### 6.1 冷启动

1. 启动应用
2. 打开 `launcher.db`
3. 显示本地登录页
4. 输入 `Username + MasterPassword`
5. 校验账号存在、启用状态、密码哈希
6. 用主密码派生密钥解开 `EncryptedDataKey`
7. 解析该账号 `DatabasePath`
8. 打开该账号自己的 `orderly.db`
9. 初始化仓储、服务、主窗口
10. 进入已登录态

### 6.2 睡眠 / 休眠恢复

前提：

- 应用进程还活着
- 当前账号已处于已登录态

流程：

1. 监听系统 `Suspend`
2. 标记当前会话进入 `PendingPinUnlock`
3. 系统恢复后，主窗口重新激活或显示时拦截
4. 显示 PIN 解锁页
5. 输入 6 位 PIN
6. 校验成功后恢复 UI

说明：

- 不重新打开数据库
- 不重新解 `DataKey`
- 只恢复 UI 访问

### 6.3 关闭应用 / 进程丢失

如果应用进程已退出、崩溃或被杀：

- 内存里的 `DataKey` 视为丢失
- 下次启动必须重新输入 `账号 + 主密码`
- PIN 不具备重建业务会话的能力

这是故意设计，不是限制遗漏。

## 7. 自动解密行为

登录成功后：

- 当前账号的 `DataKey` 常驻当前应用进程内存
- Repository 读数据时自动解密敏感字段
- Repository 写数据时自动加密敏感字段
- ViewModel / UI 层只接触明文模型

这意味着：

- 系统内可以正常查看客户、订单、备注、金额、统计
- 搜索仍然可用
- 编辑仍然按明文操作
- 落盘时自动回到密文

安全边界：

- 这是“已登录会话内自动可读”
- 不是“每看一条数据再输一次密码”
- 已登录态下明文存在于进程内存是允许且必要的

## 8. 敏感字段范围

原则：

- 能识别个人 / 交易对象身份的信息：敏感
- 能还原沟通内容、需求、报价、收货信息的信息：敏感
- 金额、统计、跟进节奏、时间行为画像：敏感
- 主键、状态枚举、布尔开关、系统版本号：非敏感

### 8.1 当前主线建议加密字段

#### `Customers`

- `Name`
- `ContactHandle`
- `Phone`
- `Remark`
- `ExternalId`
- `RawPayload`
- `LastContactAt`

#### `Deals`

- `Title`
- `EstimatedAmount`
- `Requirement`
- `ExpectedCloseAt`
- `ClosedAt`
- `LostReason`

#### `Orders`

- `Title`
- `Amount`
- `Requirement`
- `ExternalId`
- `RawPayload`
- `NextFollowUpAt`

#### `FollowUps`

- `Title`
- `Content`
- `ScheduledAt`
- `CompletedAt`
- `ReminderAt`

#### `CustomerNotes`

- `Content`

#### `ActivityLogs`

- `Title`
- `Description`
- `Operator`
- `MetadataJson`

#### `ConversationMessages`

- `SenderName`
- `Content`
- `MessageTime`
- `SourceMessageId`
- `MetadataJson`

#### `AiSuggestions`

- `SuggestionText`
- `Reason`
- `Confidence`
- `MetadataJson`

#### `OcrResults`

- `SourcePath`
- `SourceName`
- `ExtractedText`
- `ErrorMessage`
- `MetadataJson`

#### `PriceAdjustments`

- `OriginalAmount`
- `AdjustedAmount`
- `Reason`
- `RequestedBy`
- `ApprovedBy`
- `ApprovedAt`

#### `ReplyTemplates`

- `Content`

### 8.2 当前主线建议不加密字段

- 所有主键 / 外键：`Id / CustomerId / DealId / OrderId`
- 状态枚举：`Status / Stage / Type / Priority`
- 软删与同步字段：`DeletedAt / RemoteId / IsSynced / Version`
- 系统时间：`CreatedAt / UpdatedAt`
- 纯 UI 偏好与热键设置

### 8.3 统计类字段说明

你要求统计类也算敏感，V1 按这个口径执行。

当前主线里，已落盘的统计/金额/时间画像主要包括：

- `Deals.EstimatedAmount`
- `Orders.Amount`
- `PriceAdjustments.OriginalAmount`
- `PriceAdjustments.AdjustedAmount`
- `Customers.LastContactAt`
- `Orders.NextFollowUpAt`
- `FollowUps.ScheduledAt / CompletedAt / ReminderAt`
- `AiSuggestions.Confidence`

如果后续新增以下缓存列，也默认按敏感字段处理：

- `TotalOrders`
- `TotalSpent`
- `FollowUpCount`
- `AverageAmount`
- `LastPurchaseAt`

## 9. 加密落地策略

### 9.1 算法

- 对称加密：`AES-GCM`
- 密钥长度：`256-bit`
- 每个字段写入时使用独立随机 `Nonce`

### 9.2 列存储策略

V1 采用 **新增密文字段列**，不直接复用明文字段。

例子：

- `Customers.Phone` 保留用于迁移期兼容
- 新增 `Customers.PhoneCiphertext`
- 迁移完成后：
  - Repository 只读 `PhoneCiphertext`
  - `Phone` 列回填为空串或安全默认值

原因：

- 不污染现有列类型
- 不让 `REAL / INTEGER` 列强行存 Base64 文本
- 迁移更可控
- 出问题时更容易回滚

对金额/数字/时间字段也采用同样策略：

- 原字段保留占位
- 新增 `...Ciphertext TEXT NOT NULL DEFAULT ''`
- 加解密时统一把值序列化为字符串后处理

### 9.3 仓储层职责

所有敏感字段加解密都收口在 Repository。

规则：

- `SELECT` 后先解密再映射到 Model
- `INSERT / UPDATE` 前先加密再落库
- 上层 `Service / ViewModel / XAML` 不处理密钥细节

这能把影响范围压在 `Data` 层，不把加密逻辑扩散到业务层。

## 10. 搜索、统计、排序影响

### 10.1 搜索

当前 `LocalGlobalSearchService` 是先把数据从 Repository 拉出来，再在应用层做匹配。  
这对本设计是利好。

结论：

- 只要 Repository 返回的是已解密模型
- 当前搜索链路仍可工作
- 不需要为 V1 额外设计搜索索引密钥或盲索引

### 10.2 统计

金额和统计字段加密后：

- 不再依赖 SQLite 直接对密文字段做聚合
- 需要在应用层基于解密后的模型计算

当前主线本来就是本地优先和内存投影较多，这个代价可接受。

### 10.3 排序

对于被加密的金额/时间字段：

- 如果列表排序依赖这些字段
- 应在 Repository 拉取后于应用层排序

V1 不新增数据库排序优化。

## 11. 账号创建与切换

### 11.1 首次启动

如果 `launcher.db` 中没有任何账号：

- 显示“创建本地账号”页
- 输入：
  - `Username`
  - `DisplayName`
  - `MasterPassword`
  - `PIN`
- 创建：
  - `LocalAccounts` 记录
  - 账号目录
  - 该账号业务库
  - 该账号专属 `DataKey`
  - 该账号角色为 `Owner`
  - 生成并展示一次 `Recovery Key`

### 11.2 后续新增账号

V1 **不允许任意人自助注册新账号**。

规则：

- 首次启动时创建的第一个账号固定为 `Owner`
- 后续新增账号只能由已登录的 `Owner` 在系统内创建
- `Member` 不能创建其他账号
- 登录页只允许已有账号登录，不提供公开注册入口

设计理由：

- 同一台电脑是受控设备，不应该允许任何接触到软件的人直接自建账号
- 保留最小管理能力即可，不扩展成复杂组织/审批系统
- 仍然不需要碰微信、云端或远程账号体系

### 11.3 切换账号

切换账号等价于：

1. 关闭当前业务会话
2. 清空内存中的 `DataKey`
3. 回到登录页
4. 使用另一个账号冷启动登录

不做热切换共享上下文。

### 11.4 账号管理最小边界

`Owner` 可做：

- 创建 `Member`
- 禁用 `Member`
- 重置 `Member` 主密码
- 重置 `Member` PIN

`Member` 可做：

- 修改自己的主密码
- 修改自己的 PIN
- 查看自己的基本账号信息

V1 不做：

- 多个 `Owner`
- 账号所有权转移
- 账号审批流
- 账号回收站

### 11.5 禁用与删除

V1 只做“禁用账号”，不做物理删除账号。

规则：

- 禁用后，账号不能再次登录
- 该账号业务库文件默认保留，不自动删除
- 不提供 UI 上的一键彻底删除

原因：

- 彻底删除会带来误删、恢复、审计和备份联动风险
- 禁用已经足够满足“不给继续用”的管理需求

### 11.6 修改主密码与 PIN

主密码修改：

- 必须在已登录状态下进行
- 需要先验证旧主密码
- 修改后只重包裹 `EncryptedDataKey`
- 不需要重加密整库业务数据

PIN 修改：

- 必须在已登录状态下进行
- 需要先验证旧 PIN 或由 `Owner` 重置
- 只更新 PIN 哈希，不影响业务库密文

### 11.7 忘记主密码

`Member` 忘记主密码：

- 由 `Owner` 重置 `Member` 主密码并重新包裹该账号 `DataKey`

`Owner` 忘记主密码：

- 允许通过 `Recovery Key` 进入恢复流程
- 校验 `Recovery Key` 成功后，允许设置新的主密码
- 设置新主密码后，重新包裹当前账号的 `EncryptedDataKey`
- 不需要重加密整库业务数据

如果 `Owner` 同时丢失主密码和 `Recovery Key`，则：

- 只能依赖已有登录态下主动修改
- 或从外部备份恢复

这点必须写死：

- 不做明文密码找回
- 不做弱恢复问题答案
- 不做绕过主密码的后门
- `Recovery Key` 只显示一次，不提供明文回看

## 12. 代码落点

### 12.1 应新增

- `src/Orderly.Core/Services/ILocalAuthService.cs`
- `src/Orderly.Core/Services/ISessionLockService.cs`
- `src/Orderly.Core/Services/IFieldEncryptionService.cs`
- `src/Orderly.Data/Repositories/LocalAccountRepository.cs`
- `src/Orderly.Data/Services/LocalAuthService.cs`
- `src/Orderly.Data/Services/SessionLockService.cs`
- `src/Orderly.Data/Services/FieldEncryptionService.cs`
- `src/Orderly.Data/Sqlite/LauncherConnectionFactory.cs`

### 12.2 应修改

- `src/Orderly.App/App.xaml.cs`
  - 启动先走注册库和冷启动登录
  - 已登录后再初始化主工作台
  - 监听睡眠 / 恢复事件并触发 PIN 解锁
- `src/Orderly.App/Views/LoginView.xaml`
- `src/Orderly.App/Views/LoginView.xaml.cs`
- `src/Orderly.App/ViewModels/LoginViewModel.cs`
  - 从“假登录页”改成真正的本地账号登录页
- `src/Orderly.App/Views/Settings*`
  - 增加 `Owner` 账号管理入口
  - 增加 `Recovery Key` 展示/确认保存入口
- `src/Orderly.Data/Sqlite/DatabasePaths.cs`
  - 从单库路径改为“注册库路径 + 账号库路径”
- `src/Orderly.Data/Sqlite/DatabaseInitializer.cs`
  - 增加注册库初始化
  - 增加业务库密文字段迁移
- 全部涉及敏感字段的 Repository
  - 读时解密
  - 写时加密

### 12.3 明确不改

- `miniprogram/`
- `cloudfunctions/`
- 任何微信接口
- 任何云端权限逻辑
- 任何远程用户体系

## 13. 迁移策略

### Phase 1：账号壳与路径切分

- 建 `launcher.db`
- 建 `LocalAccounts`
- 首次启动创建 `Owner`
- 每账号独立业务库路径
- 现有单库用户迁移为首个默认账号

### Phase 2：冷启动登录

- 真正启用 `Username + MasterPassword`
- 只有登录成功后才初始化主窗口

### Phase 3：PIN 热解锁

- 监听睡眠 / 恢复
- 恢复后弹 PIN
- 不做应用关闭后的 PIN 恢复

### Phase 4：敏感字段加密迁移

- 给业务表增加 `...Ciphertext` 列
- 读取旧明文
- 使用当前账号 `DataKey` 回填密文列
- 明文字段清空或置安全默认值
- Repository 全量切到密文字段

### Phase 5：回归验证

- 冷启动登录
- 错误密码拦截
- 睡眠恢复 PIN
- 账号切换隔离
- `Owner` 创建 / 禁用 `Member`
- `Member` 无法创建新账号
- 修改主密码 / PIN
- `Owner` 使用 `Recovery Key` 重置主密码
- 搜索 / 列表 / 详情 / 编辑 / 备份链路回归

## 14. 风险与取舍

### 14.1 已接受的取舍

- 已登录后，明文存在进程内存
- PIN 只保护恢复 UI，不负责重建完整业务会话
- 不做跨账号共享和精细权限
- 统计字段加密后，部分统计要转应用层计算

### 14.2 主要风险

- 现有 Repository 数量较多，密文字段迁移会触及多个表
- `DatabaseInitializer` 需要承担一次真实迁移逻辑，不能只做建表
- 备份/恢复如果直接按 SQLite 文件拷贝，必须确认账号库与注册库关系
- QA seed / demo seed 如果仍然直接插明文 SQL，需要同步更新
- `Recovery Key` 一旦被旁人拿到，等于具备重置 `Owner` 主密码的能力
- `Owner` 如果同时丢失主密码和 `Recovery Key`，恢复能力仍然很弱

## 15. 备份与恢复约束

V1 需要明确以下约束：

- 账号注册库和账号业务库不能被当成一个单文件系统来理解
- 备份时至少要同时考虑：
  - `launcher.db`
  - 对应账号的 `orderly.db`
- 恢复单个账号时，必须同时恢复：
  - 该账号 `LocalAccounts` 元数据
  - 该账号业务库文件

建议口径：

- “整机备份”可同时备份 `identity/` 与 `accounts/`
- “单账号备份”只允许在已登录该账号后导出自己的业务库快照和必要账号元数据
- V1 不做跨账号合并恢复

## 16. 退出登录与锁定

V1 还应明确两种动作：

- 退出登录
  - 清空内存中的 `DataKey`
  - 关闭当前业务会话
  - 回到登录页
- 锁定
  - 仅用于当前已登录会话
  - 可复用 PIN 解锁页

说明：

- 本次需求里，强制 PIN 主要由睡眠/休眠恢复触发
- 但保留一个手动“锁定”入口更完整，且实现成本低

## 17. 实施建议

建议按以下顺序做，不要并行乱改：

1. 先做注册库、账号模型、路径切分
2. 再做冷启动主密码登录
3. 再做睡眠恢复 PIN
4. 再做 `Owner / Member` 最小账号管理
5. 最后做字段级加密迁移

原因：

- 先解决“多账号独立”和“谁的库被打开”
- 再解决“怎么登录”
- 再补“谁能创建别的账号”
- 最后再改“打开后怎么解密”

这样最容易定位问题，也最不容易把现有业务主链一起拖下水。

## 18. 实现前固定口径

真正开工前，以下口径视为已确定，不再反复讨论：

- 禁用账号不强制踢掉当前在线会话，只阻止后续登录
- 旧单库数据整体迁入首个 `Owner`` 账号库，不做数据拆分
- 登录页不提供公开注册入口
- 手动“锁定”与睡眠恢复统一复用 PIN 解锁
- 账号管理动作必须记审计日志
- `Recovery Key` 只显示一次，必须确认“已保存”后才能继续
- `Owner` 同时丢失主密码和 `Recovery Key` 时，不提供后门恢复

## 19. 验收清单

实现完成后，至少验证以下场景：

- 首次启动创建 `Owner`
- 已有账号冷启动登录
- 错误主密码拦截
- 睡眠恢复 PIN 解锁
- 关闭应用后不能只靠 PIN 登录
- `Owner` 创建 `Member`
- `Member` 无法创建其他账号
- `Owner` 禁用 `Member` 后，`Member` 无法再次登录
- `Member` 修改自己的主密码与 PIN
- `Owner` 重置 `Member` 主密码与 PIN
- `Owner` 使用 `Recovery Key` 重置自己的主密码
- 登录后敏感字段能正常显示、编辑、搜索
- 切换账号后看不到其他账号数据
- 备份 / 恢复时注册库与账号库能正确匹配

## 20. Execution Guide For GPT-5.3 Codex

本节用于把工作拆给 `5.3-codex`。  
原则：

- 一次只下发一个 `Phase` 或一个子任务
- 指令必须限制边界，避免模型顺手扩改
- 每个任务都要求：
  - 先检查相关代码
  - 只改指定范围
  - 完成后运行最小必要验证
  - 输出修改文件、关键决策、验证结果、剩余风险

建议统一附带的全局约束：

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.
```

## 21. Phase 1 Breakdown: Launcher Storage And Account Isolation Foundation

### 21.1 Phase Goal

建立“注册库 + 每账号独立业务库”的底层骨架，但**先不切真实登录流程**。  
Phase 1 完成后，应具备这些能力：

- 代码层面能区分 `launcher.db` 和账号业务库
- 有本地账号模型和注册库表结构
- 有现有单库迁移到首个 `Owner` 账号的基础辅助逻辑
- 现有业务主链暂时仍能继续跑

### 21.2 Subtask 1.1: Split Database Path Responsibilities

任务目标：

- 把当前只有一个 `orderly.db` 路径的实现，升级为：
  - 注册库路径
  - 账号目录路径
  - 指定账号业务库路径

建议涉及文件：

- `src/Orderly.Data/Sqlite/DatabasePaths.cs`
- 新增 `src/Orderly.Data/Sqlite/LauncherConnectionFactory.cs`
- 可能新增与账号目录计算相关的 helper

任务步骤：

1. 检查当前 `DatabasePaths` 和 `SqliteConnectionFactory` 的职责边界。
2. 设计新的路径 API，但不要破坏现有调用方的编译稳定性。
3. 增加：
   - `GetLauncherDatabasePath()`
   - `GetAccountsRootPath()`
   - `GetAccountDatabasePath(accountId)`
4. 增加专用于注册库的连接工厂。
5. 保持原业务库连接工厂可继续用于指定业务库路径。

完成标准：

- 路径职责清晰分离
- 编译通过
- 不改登录 UI
- 不引入微信/云端逻辑

不要做：

- 不要顺手改 App 启动流程
- 不要改业务仓储查询逻辑
- 不要开始账号登录实现

English instruction:

```text
Implement Phase 1.1 only.

Task:
Split local database path responsibilities so the app can distinguish:
1. the launcher identity database path,
2. the accounts root directory,
3. the per-account business database path.

Requirements:
- Inspect the current DatabasePaths and SQLite connection setup first.
- Keep changes minimal and safe.
- Add path helpers for launcher.db and per-account orderly.db files.
- Add a dedicated connection factory for the launcher database if needed.
- Do not change the login flow yet.
- Do not modify miniprogram/, cloudfunctions/, or any WeChat-related code.
- Do not refactor unrelated repositories.

Deliverables:
- Updated path helpers
- Any new launcher connection factory
- A short summary of design decisions

Validation:
- Run the smallest relevant dotnet build command and report the result.
```

### 21.3 Subtask 1.2: Add Launcher Schema And Local Account Repository

任务目标：

- 在注册库中建立 `LocalAccounts` 基础表结构
- 提供最小可用的本地账号仓储

建议涉及文件：

- `src/Orderly.Data/Sqlite/DatabaseInitializer.cs` 或新增独立初始化器
- `src/Orderly.Core/Models/*` 新账号模型
- `src/Orderly.Core/Repositories/*`
- `src/Orderly.Data/Repositories/LocalAccountRepository.cs`

任务步骤：

1. 决定注册库初始化器是复用现有 `DatabaseInitializer` 还是拆分独立初始化器。
2. 为 `LocalAccounts` 明确字段映射。
3. 新增最小模型：
   - `LocalAccount`
   - 需要的话再加 `LocalAccountRole`
4. 新增最小仓储接口和实现，至少支持：
   - 按用户名查询
   - 按账号 ID 查询
   - 创建账号
   - 更新账号
   - 列出账号
5. 不接 UI，只把数据层能力建好。

完成标准：

- 注册库可初始化
- `LocalAccounts` 可增查改
- 不影响现有业务表初始化

不要做：

- 不要接主窗口
- 不要改现有登录页
- 不要开始密码校验服务

English instruction:

```text
Implement Phase 1.2 only.

Task:
Add the launcher database schema and the minimal local account repository layer.

Requirements:
- Inspect the current DatabaseInitializer and repository patterns first.
- Introduce the LocalAccounts schema in the launcher database.
- Add the minimal domain model and repository interface/implementation for local accounts.
- Support create, get by id, get by username, list, and update.
- Keep the business database schema untouched except for safe infrastructure wiring.
- Do not build the login UI yet.
- Do not implement authentication flow yet.

Deliverables:
- Local account model(s)
- Repository interface(s)
- Repository implementation(s)
- Launcher schema initialization

Validation:
- Run a minimal build and report whether it passes.
```

### 21.4 Subtask 1.3: Add Single-Database Migration Helper For First Owner

任务目标：

- 为“旧单库迁入首个 `Owner` 账号库”准备迁移辅助逻辑
- 先做底层 helper，不在本任务里接 UI

建议涉及文件：

- 新增 `src/Orderly.Data/Services/*` 迁移辅助服务
- `DatabasePaths` 相关 helper
- `App.xaml.cs` 仅在必要时做极小准备，不切流程

任务步骤：

1. 检查当前默认 `orderly.db` 路径。
2. 设计迁移策略：
   - 发现老单库
   - 创建首个账号目录
   - 复制或移动旧库到目标账号路径
   - 标记迁移完成
3. 决定迁移标记存放位置。
4. 先实现 helper/service，不在本任务里触发实际迁移 UI。

完成标准：

- 迁移策略在代码里可复用
- 不影响当前正常启动
- 为后续首次创建 `Owner` 做准备

不要做：

- 不要在此任务中改登录页
- 不要在此任务中真正创建 `Owner`
- 不要直接删除旧库

English instruction:

```text
Implement Phase 1.3 only.

Task:
Add the migration helper that will later move the legacy single orderly.db into the first Owner account database location.

Requirements:
- Inspect the current default database path behavior first.
- Implement a safe helper/service for legacy database detection and migration planning.
- The helper should support the future flow where the first Owner account receives the existing single-user database.
- Do not trigger the migration from the UI yet.
- Do not delete the legacy database automatically.
- Keep the implementation reversible and explicit.

Deliverables:
- Migration helper/service
- Clear migration state handling
- Minimal comments or notes where behavior is non-obvious

Validation:
- Build the solution and report the result.
```

## 22. Phase 2 Breakdown: Cold-Start Login And First-Run Owner Bootstrap

### 22.1 Phase Goal

把当前假登录页替换为真正的冷启动本地登录/首次创建 `Owner` 流程。  
Phase 2 完成后，应具备：

- 首次启动无账号时可创建 `Owner`
- 已有账号时可用 `Username + MasterPassword` 登录
- 登录成功后才初始化业务工作台

### 22.2 Subtask 2.1: Implement Authentication And Session Context Services

任务目标：

- 建立本地认证服务、密码哈希、`DataKey` 包裹/解包、会话上下文

建议涉及文件：

- `src/Orderly.Core/Services/ILocalAuthService.cs`
- `src/Orderly.Core/Services/IFieldEncryptionService.cs`
- `src/Orderly.Data/Services/LocalAuthService.cs`
- `src/Orderly.Data/Services/FieldEncryptionService.cs`
- 可能新增 session context model/service

任务步骤：

1. 定义本地认证服务边界。
2. 定义密码哈希与 `Recovery Key` 哈希逻辑。
3. 定义 `DataKey` 生成、包裹、解包接口。
4. 定义“当前已登录账号”的会话上下文对象。
5. 先把服务层做好，不急着接 UI。

完成标准：

- 可以通过服务完成：
  - 创建账号凭证材料
  - 校验主密码
  - 解开 `DataKey`
- 还未接页面，但服务边界完整

不要做：

- 不要顺手实现 PIN 热解锁
- 不要开始账号管理页

English instruction:

```text
Implement Phase 2.1 only.

Task:
Build the local authentication and session context services for cold-start login.

Requirements:
- Add the service interfaces and implementations for:
  - master password hashing and verification,
  - recovery key hashing and verification,
  - DataKey generation,
  - DataKey wrapping/unwrapping,
  - current signed-in account session context.
- Keep the logic local-only. No network, no cloud, no WeChat integration.
- Do not implement PIN resume unlock in this task.
- Do not implement account management UI in this task.

Deliverables:
- Auth service(s)
- Encryption/wrapping service(s)
- Session context object/service

Validation:
- Run a minimal build and report the result.
```

### 22.3 Subtask 2.2: Replace Fake Login View With Real Local Login Flow

任务目标：

- 把当前假登录页改成真正的本地账号登录页
- 支持“无账号时创建 `Owner`”

建议涉及文件：

- `src/Orderly.App/Views/LoginView.xaml`
- `src/Orderly.App/Views/LoginView.xaml.cs`
- `src/Orderly.App/ViewModels/LoginViewModel.cs`

任务步骤：

1. 检查当前登录页现状和绑定方式。
2. 设计两种模式：
   - 首次启动：创建 `Owner`
   - 已有账号：账号 + 主密码登录
3. 增加必要的字段和错误状态显示。
4. 创建 `Owner` 成功后，立即进入登录成功态。
5. 生成 `Recovery Key`，并要求显式确认已保存。

完成标准：

- 假登录逻辑被移除
- 首次启动可以创建 `Owner`
- 登录页不再提供公开注册 `Member`

不要做：

- 不要在这个任务里做睡眠恢复 PIN
- 不要在这个任务里做账号管理页

English instruction:

```text
Implement Phase 2.2 only.

Task:
Replace the current fake login screen with a real local account login and first-run Owner bootstrap flow.

Requirements:
- Inspect the existing LoginView, LoginViewModel, and code-behind first.
- Support two modes:
  1. first-run Owner creation when no accounts exist,
  2. normal username + master password login when accounts exist.
- Remove the fake delay-based login behavior.
- Require explicit confirmation that the Recovery Key was saved during Owner creation.
- Do not add public self-registration for Member accounts.
- Do not implement sleep/PIN unlock here.

Deliverables:
- Updated login UI
- Updated login view model / code-behind
- Clear error handling and first-run behavior

Validation:
- Build the solution and report the result.
```

### 22.4 Subtask 2.3: Gate App Startup On Successful Login

任务目标：

- 只有登录成功后才初始化对应账号业务库和主工作台

建议涉及文件：

- `src/Orderly.App/App.xaml.cs`
- 可能涉及新 session bootstrap helper

任务步骤：

1. 检查当前 `App.xaml.cs` 中登录页和主窗口初始化顺序。
2. 把工作台初始化改成依赖“当前已登录账号”的数据库路径。
3. 登录成功后再构建仓储和服务。
4. 确保关闭登录页时的退出逻辑仍然正确。

完成标准：

- 未登录不能直接初始化业务工作台
- 登录成功后只打开当前账号业务库
- 当前主链可正常进入主窗口

不要做：

- 不要接睡眠/恢复事件
- 不要实现加密列迁移

English instruction:

```text
Implement Phase 2.3 only.

Task:
Change app startup so the workspace initializes only after a successful cold-start local login.

Requirements:
- Inspect App.xaml.cs startup flow first.
- Do not initialize repositories and the main workspace before login succeeds.
- Once login succeeds, initialize the business database for the signed-in account only.
- Preserve existing shutdown behavior when the login window closes without a successful login.
- Do not implement sleep/resume lock in this task.

Deliverables:
- Updated startup flow
- Correct per-account workspace initialization

Validation:
- Run a minimal build and report the result.
```

## 23. Phase 3 Breakdown: Resume PIN Unlock, Manual Lock, And Logout

### 23.1 Phase Goal

给已登录会话加上“睡眠恢复 PIN 解锁”和“手动锁定/退出登录”能力。  
Phase 3 完成后，应具备：

- 电脑睡眠恢复后要求 PIN
- 应用内可手动锁定
- 应用内可退出登录回到登录页

### 23.2 Subtask 3.1: Add Session Lock Service And Suspend/Resume Detection

任务目标：

- 抽出会话锁状态服务，并监听系统睡眠 / 恢复

建议涉及文件：

- `src/Orderly.Core/Services/ISessionLockService.cs`
- `src/Orderly.Data/Services/SessionLockService.cs`
- `src/Orderly.App/App.xaml.cs`

任务步骤：

1. 定义锁状态：
   - Unlocked
   - PendingPinUnlock
   - LoggedOut
2. 接系统事件，进入 `PendingPinUnlock`
3. 保证恢复后不会误初始化新会话
4. 不接 UI 样式，只处理状态和事件

完成标准：

- 睡眠恢复后会话状态正确变化
- 不影响已登录账号上下文

不要做：

- 不要开始实现 PIN 页面视觉重做
- 不要顺手改账号管理

English instruction:

```text
Implement Phase 3.1 only.

Task:
Add the session lock service and detect system suspend/resume so the app can require a PIN after resume.

Requirements:
- Define explicit session lock states.
- Hook the app into suspend/resume detection.
- On resume, transition the current signed-in session into a PIN-required state.
- Do not implement the full PIN UI in this task.
- Do not refactor unrelated startup logic.

Deliverables:
- Session lock service
- Suspend/resume wiring
- Clear state transitions

Validation:
- Build the solution and report the result.
```

### 23.3 Subtask 3.2: Implement PIN Unlock Flow

任务目标：

- 在会话进入 `PendingPinUnlock` 后，真正要求输入 PIN 才能恢复 UI

建议涉及文件：

- `src/Orderly.App/Views/LoginView*` 或新增专门 PIN 解锁视图
- `LoginViewModel` 或新增 unlock view model
- `LocalAuthService`

任务步骤：

1. 决定复用登录页还是独立 PIN 解锁页。
2. 增加 PIN 校验路径。
3. 确保 PIN 只用于当前在线会话恢复。
4. PIN 错误时不销毁会话，但不能放行 UI。

完成标准：

- 睡眠恢复后必须输 PIN
- 关闭应用后不能只靠 PIN 登录
- PIN 逻辑与主密码逻辑职责分离

不要做：

- 不要允许 PIN 打开冷启动会话
- 不要实现 Recovery Key 流程修改

English instruction:

```text
Implement Phase 3.2 only.

Task:
Implement the PIN unlock flow for an already authenticated session that resumed from sleep.

Requirements:
- Require the 6-digit PIN when the session is in the pending-unlock state.
- The PIN must only unlock an already existing in-memory session.
- The PIN must not be usable for cold-start login.
- Keep master-password and PIN responsibilities strictly separated.
- Do not redesign unrelated login behaviors.

Deliverables:
- PIN unlock UI flow
- PIN verification integration
- Correct blocked/unblocked session behavior

Validation:
- Build the solution and report the result.
```

### 23.4 Subtask 3.3: Add Manual Lock And Logout

任务目标：

- 增加“锁定”和“退出登录”

建议涉及文件：

- `MainWindow` 或相关命令区域
- `MainViewModel` 或小型会话命令服务
- `App.xaml.cs`

任务步骤：

1. 定义“锁定”和“退出登录”的差异。
2. 锁定：
   - 保留当前已登录上下文
   - 清空可见 UI
   - 走 PIN 解锁
3. 退出登录：
   - 清空 `DataKey`
   - 释放当前业务会话
   - 返回登录页

完成标准：

- 两个动作语义明确且可用
- 不破坏当前主窗口关闭逻辑

English instruction:

```text
Implement Phase 3.3 only.

Task:
Add manual lock and logout actions for the current local session.

Requirements:
- Manual lock must keep the current authenticated session but require PIN to resume.
- Logout must clear the in-memory DataKey, close the current business session, and return to the login screen.
- Keep the behaviors explicit and separate.
- Do not redesign unrelated main-window UI beyond what is necessary to expose the actions.

Deliverables:
- Lock action
- Logout action
- Correct session cleanup behavior

Validation:
- Build the solution and report the result.
```

## 24. Phase 4 Breakdown: Owner/Member Account Management

### 24.1 Phase Goal

在不引入复杂权限系统的前提下，完成最小账号管理。  
Phase 4 完成后，应具备：

- `Owner` 可创建 `Member`
- `Owner` 可禁用 `Member`
- `Owner` 可重置 `Member` 主密码 / PIN
- `Member` 只能改自己的主密码 / PIN

### 24.2 Subtask 4.1: Add Account Management Service And Role Checks

任务目标：

- 建立账号管理服务和最小角色校验

建议涉及文件：

- 新增 `src/Orderly.Core/Services/*`
- `LocalAccountRepository`
- `LocalAuthService`

任务步骤：

1. 定义 `Owner` 能做什么、`Member` 能做什么。
2. 抽出账号管理服务。
3. 服务层拦截非法操作。
4. 先做好服务，再接 UI。

完成标准：

- 服务层已有明确角色边界
- 非 Owner 无法创建或禁用其他账号

English instruction:

```text
Implement Phase 4.1 only.

Task:
Add the minimal account-management service and Owner/Member permission checks.

Requirements:
- Implement role-aware account management rules in the service layer.
- Owner can create Member accounts, disable Members, and reset Member credentials.
- Member cannot manage other accounts.
- Keep the scope local-only and minimal.
- Do not build the full UI in this task.

Deliverables:
- Account management service(s)
- Role checks
- Clear failure behavior for unauthorized operations

Validation:
- Build the solution and report the result.
```

### 24.3 Subtask 4.2: Add Owner Account Management UI

任务目标：

- 给 `Owner` 增加最小账号管理入口

建议涉及文件：

- `src/Orderly.App/Views/Settings*`
- 相关 ViewModel

任务步骤：

1. 在现有设置区寻找最小侵入入口。
2. 只做必要 UI：
   - 账号列表
   - 创建 `Member`
   - 禁用 `Member`
   - 重置 `Member` 凭证
3. 不做大规模视觉重构。

完成标准：

- `Owner` 可执行最小账号管理动作
- `Member` 看不到或不能使用这些入口

English instruction:

```text
Implement Phase 4.2 only.

Task:
Add the minimal Owner-only account management UI.

Requirements:
- Reuse the existing settings area if possible.
- Add only the smallest UI needed for:
  - listing accounts,
  - creating a Member,
  - disabling a Member,
  - resetting Member credentials.
- Member users must not have access to these controls.
- Avoid broad visual refactors.

Deliverables:
- Owner-only account management UI
- Required view model changes

Validation:
- Build the solution and report the result.
```

### 24.4 Subtask 4.3: Add Recovery Key And Credential Reset Flows

任务目标：

- 完成 `Owner` 的 `Recovery Key` 流程和 `Member` 凭证重置流

建议涉及文件：

- 登录页相关视图/VM
- 设置页相关视图/VM
- `LocalAuthService`

任务步骤：

1. 增加 `Recovery Key` 校验入口。
2. 增加 `Owner` 主密码重置流程。
3. 增加 `Owner` 重置 `Member` 主密码 / PIN 流程。
4. 确保 `Recovery Key` 只显示一次，不支持明文回看。

完成标准：

- `Owner` 可用 `Recovery Key` 重置自己的主密码
- `Member` 凭证可由 `Owner` 重置

English instruction:

```text
Implement Phase 4.3 only.

Task:
Add the Recovery Key flow for Owner password recovery and the credential-reset flows for Member accounts.

Requirements:
- Owner must be able to reset the Owner master password using the Recovery Key.
- Owner must be able to reset Member master passwords and PINs.
- Recovery Key must remain one-time-display only; do not implement plaintext recall.
- Keep the flow local-only and do not add any cloud recovery fallback.

Deliverables:
- Recovery Key reset flow
- Member credential reset flow
- Correct re-wrapping of EncryptedDataKey where needed

Validation:
- Build the solution and report the result.
```

## 25. Phase 5 Breakdown: Field Encryption Migration And Repository Wiring

### 25.1 Phase Goal

把敏感字段真正切成“落盘密文，系统内明文”。  
Phase 5 完成后，应具备：

- 敏感字段有密文列
- 旧明文字段迁移到密文列
- Repository 自动加解密
- 搜索/详情/编辑继续可用

### 25.2 Subtask 5.1: Add Ciphertext Columns And Migration Infrastructure

任务目标：

- 为敏感字段新增密文列
- 搭起可重复执行的迁移基础设施

建议涉及文件：

- `src/Orderly.Data/Sqlite/DatabaseInitializer.cs`
- 可能新增 migration helper/service

任务步骤：

1. 根据文档列出当前 V1 敏感字段。
2. 给相应表新增 `...Ciphertext` 列。
3. 保证重复启动不会重复破坏数据。
4. 暂时不要切 Repository 读取逻辑。

完成标准：

- schema 已具备密文字段
- 迁移具备幂等性

English instruction:

```text
Implement Phase 5.1 only.

Task:
Add ciphertext columns for the defined sensitive fields and build the migration infrastructure.

Requirements:
- Use the sensitive-field scope already defined in the design document.
- Add ciphertext columns without breaking repeated startup.
- Keep the migration idempotent.
- Do not switch repository read/write behavior yet in this task.

Deliverables:
- Updated schema migration
- Safe/idempotent migration structure

Validation:
- Build the solution and report the result.
```

### 25.3 Subtask 5.2: Migrate Legacy Plaintext Data Into Ciphertext Columns

任务目标：

- 把旧明文值安全回填到密文字段

任务步骤：

1. 识别未迁移行。
2. 使用当前账号 `DataKey` 生成密文。
3. 回填密文列。
4. 按设计清空或置安全默认值到旧明文字段。
5. 保证失败时不会把数据置于半坏状态。

完成标准：

- 已有敏感数据迁移完成
- 不留可读明文

English instruction:

```text
Implement Phase 5.2 only.

Task:
Migrate legacy plaintext sensitive data into ciphertext columns.

Requirements:
- Detect rows that still contain legacy plaintext.
- Encrypt with the current account DataKey.
- Backfill ciphertext columns.
- Clear or neutralize the legacy plaintext columns according to the design.
- Keep the process safe against partial failure as much as possible.

Deliverables:
- Data backfill logic
- Safe plaintext cleanup behavior

Validation:
- Build the solution and report the result.
```

### 25.4 Subtask 5.3: Update Repositories To Read/Write Encrypted Fields

任务目标：

- Repository 成为唯一的加解密边界

建议涉及文件：

- 所有涉及敏感字段的仓储
- 必要时增加公共加解密辅助

任务步骤：

1. 逐仓储切换敏感字段读取逻辑。
2. `SELECT` 后解密成现有 Model。
3. `INSERT / UPDATE` 前把敏感字段转为密文列写入。
4. 保持上层业务模型不变。

完成标准：

- UI / Service 层不需要知道密钥细节
- 搜索、详情、编辑都能拿到明文模型

English instruction:

```text
Implement Phase 5.3 only.

Task:
Update repositories so they become the sole encryption/decryption boundary for sensitive fields.

Requirements:
- For reads, decrypt ciphertext columns and map back to the existing models.
- For writes, encrypt sensitive values before persistence.
- Keep the upper layers unaware of the encryption details.
- Avoid unrelated refactors while touching repositories.

Deliverables:
- Repository updates for encrypted field handling
- Any shared helper methods needed for consistency

Validation:
- Build the solution and report the result.
```

### 25.5 Subtask 5.4: Backup/Restore And Regression Validation

任务目标：

- 确认账号注册库、账号业务库、恢复流程和加密迁移在一起能跑通

任务步骤：

1. 检查当前备份/恢复实现是否假设单库。
2. 做最小必要改造，让它理解：
   - `launcher.db`
   - 某个账号的 `orderly.db`
3. 跑关键回归：
   - 登录
   - 切换账号
   - PIN
   - Recovery Key
   - 搜索
   - 编辑
   - 备份/恢复

完成标准：

- 账号与业务库绑定关系在备份/恢复里不丢失
- 主要业务主链仍可用

English instruction:

```text
Implement Phase 5.4 only.

Task:
Update backup/restore assumptions where necessary and run the final regression validation for the local-account security feature set.

Requirements:
- Inspect the existing backup/restore implementation for single-database assumptions.
- Make only the minimal changes required to support launcher.db plus per-account business databases.
- Validate the end-to-end flows:
  - cold-start login,
  - account isolation,
  - PIN resume unlock,
  - Recovery Key reset,
  - encrypted field read/write,
  - backup/restore consistency.
- Do not expand the feature scope beyond local desktop security.

Deliverables:
- Minimal backup/restore updates if needed
- Final verification summary

Validation:
- Run the relevant build/check commands and report the real results.
```

## 26. Recommended Task Dispatch Strategy

如果你要按 `5.3-codex` 分轮执行，建议顺序如下：

1. `Phase 1.1`
2. `Phase 1.2`
3. `Phase 1.3`
4. `Phase 2.1`
5. `Phase 2.2`
6. `Phase 2.3`
7. `Phase 3.1`
8. `Phase 3.2`
9. `Phase 3.3`
10. `Phase 4.1`
11. `Phase 4.2`
12. `Phase 4.3`
13. `Phase 5.1`
14. `Phase 5.2`
15. `Phase 5.3`
16. `Phase 5.4`

不要把这些任务合并成一个大 prompt。  
原因：

- `5.3-codex` 在小范围、边界清晰的任务上更稳
- 这个特性跨 `App / Core / Data / Sqlite / Settings UI`
- 一次改太多，容易顺手污染其他模块

## 27. Direct Prompt Pack For GPT-5.3 Codex

Use the prompts below as direct task inputs.  
Each prompt is intentionally self-contained.  
Send only one prompt at a time.

### Prompt 1: Phase 1.1

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 1.1 only.

Task:
Split local database path responsibilities so the app can distinguish:
1. the launcher identity database path,
2. the accounts root directory,
3. the per-account business database path.

Repository context:
- Current desktop mainline is WPF + .NET 8 + SQLite.
- Current business database path logic is in src/Orderly.Data/Sqlite/DatabasePaths.cs.
- This task must only prepare the filesystem/database-path foundation for local multi-account support.

Requirements:
- Inspect the current DatabasePaths and SQLite connection setup first.
- Keep changes minimal and safe.
- Add path helpers for launcher.db and per-account orderly.db files.
- Add a dedicated connection factory for the launcher database if needed.
- Preserve compatibility with the current business database connection flow as much as possible.
- Do not change the login flow yet.
- Do not modify miniprogram/, cloudfunctions/, or any WeChat-related code.
- Do not refactor unrelated repositories.

Expected deliverables:
- Updated path helpers
- Any new launcher connection factory
- A short summary of design decisions

Validation:
- Run the smallest relevant dotnet build command and report the result.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 2: Phase 1.2

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 1.2 only.

Task:
Add the launcher database schema and the minimal local account repository layer.

Repository context:
- The launcher identity database must remain separate from each account business database.
- The local account schema should support future cold-start login, Owner/Member roles, PIN, Recovery Key, and encrypted DataKey storage.

Requirements:
- Inspect the current DatabaseInitializer and repository patterns first.
- Introduce the LocalAccounts schema in the launcher database.
- Add the minimal domain model and repository interface/implementation for local accounts.
- Support create, get by id, get by username, list, and update.
- Keep the business database schema untouched except for safe infrastructure wiring.
- Do not build the login UI yet.
- Do not implement authentication flow yet.
- Do not add broad role-management logic yet beyond what is required by the model shape.

Expected deliverables:
- Local account model(s)
- Repository interface(s)
- Repository implementation(s)
- Launcher schema initialization

Validation:
- Run a minimal build and report whether it passes.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 3: Phase 1.3

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 1.3 only.

Task:
Add the migration helper that will later move the legacy single orderly.db into the first Owner account database location.

Repository context:
- The current app still assumes a single default orderly.db for the desktop app.
- The future flow is: when the first Owner account is created, the legacy business database becomes that Owner account’s business database.

Requirements:
- Inspect the current default database path behavior first.
- Implement a safe helper/service for legacy database detection and migration planning.
- The helper should support the future flow where the first Owner account receives the existing single-user database.
- Do not trigger the migration from the UI yet.
- Do not delete the legacy database automatically.
- Keep the implementation reversible and explicit.

Expected deliverables:
- Migration helper/service
- Clear migration state handling
- Minimal comments or notes where behavior is non-obvious

Validation:
- Build the solution and report the result.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 4: Phase 2.1

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 2.1 only.

Task:
Build the local authentication and session context services for cold-start login.

Repository context:
- Authentication is local-only.
- Cold-start login requires username + master password.
- The system also needs Recovery Key hashing/verification and DataKey wrapping/unwrapping.
- PIN resume unlock is not part of this task.

Requirements:
- Add the service interfaces and implementations for:
  - master password hashing and verification,
  - recovery key hashing and verification,
  - DataKey generation,
  - DataKey wrapping/unwrapping,
  - current signed-in account session context.
- Keep the logic local-only. No network, no cloud, no WeChat integration.
- Do not implement PIN resume unlock in this task.
- Do not implement account management UI in this task.

Expected deliverables:
- Auth service(s)
- Encryption/wrapping service(s)
- Session context object/service

Validation:
- Run a minimal build and report the result.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 5: Phase 2.2

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 2.2 only.

Task:
Replace the current fake login screen with a real local account login and first-run Owner bootstrap flow.

Repository context:
- If no local accounts exist, the login flow must switch into first-run Owner creation mode.
- If accounts already exist, the screen must support cold-start local login with username + master password.
- Owner creation must generate a Recovery Key and require explicit confirmation that it was saved.

Requirements:
- Inspect the existing LoginView, LoginViewModel, and code-behind first.
- Support two modes:
  1. first-run Owner creation when no accounts exist,
  2. normal username + master password login when accounts exist.
- Remove the fake delay-based login behavior.
- Require explicit confirmation that the Recovery Key was saved during Owner creation.
- Do not add public self-registration for Member accounts.
- Do not implement sleep/PIN unlock here.

Expected deliverables:
- Updated login UI
- Updated login view model / code-behind
- Clear error handling and first-run behavior

Validation:
- Build the solution and report the result.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 6: Phase 2.3

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 2.3 only.

Task:
Change app startup so the workspace initializes only after a successful cold-start local login.

Repository context:
- The app currently shows a login shell but still uses a single business database initialization path.
- After this task, the main workspace must initialize only for the signed-in account’s business database.

Requirements:
- Inspect App.xaml.cs startup flow first.
- Do not initialize repositories and the main workspace before login succeeds.
- Once login succeeds, initialize the business database for the signed-in account only.
- Preserve existing shutdown behavior when the login window closes without a successful login.
- Do not implement sleep/resume lock in this task.

Expected deliverables:
- Updated startup flow
- Correct per-account workspace initialization

Validation:
- Run a minimal build and report the result.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 7: Phase 3.1

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 3.1 only.

Task:
Add the session lock service and detect system suspend/resume so the app can require a PIN after resume.

Repository context:
- PIN unlock is only for an already authenticated in-memory session.
- This task is about session lock state and suspend/resume detection, not the final PIN UI flow.

Requirements:
- Define explicit session lock states.
- Hook the app into suspend/resume detection.
- On resume, transition the current signed-in session into a PIN-required state.
- Do not implement the full PIN UI in this task.
- Do not refactor unrelated startup logic.

Expected deliverables:
- Session lock service
- Suspend/resume wiring
- Clear state transitions

Validation:
- Build the solution and report the result.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 8: Phase 3.2

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 3.2 only.

Task:
Implement the PIN unlock flow for an already authenticated session that resumed from sleep.

Repository context:
- The session is already authenticated.
- The DataKey is already in memory.
- The PIN must only unlock the current in-memory session.

Requirements:
- Require the 6-digit PIN when the session is in the pending-unlock state.
- The PIN must only unlock an already existing in-memory session.
- The PIN must not be usable for cold-start login.
- Keep master-password and PIN responsibilities strictly separated.
- Do not redesign unrelated login behaviors.

Expected deliverables:
- PIN unlock UI flow
- PIN verification integration
- Correct blocked/unblocked session behavior

Validation:
- Build the solution and report the result.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 9: Phase 3.3

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 3.3 only.

Task:
Add manual lock and logout actions for the current local session.

Repository context:
- Manual lock keeps the current authenticated session and requires PIN to resume.
- Logout destroys the current business session and returns to the login screen.

Requirements:
- Manual lock must keep the current authenticated session but require PIN to resume.
- Logout must clear the in-memory DataKey, close the current business session, and return to the login screen.
- Keep the behaviors explicit and separate.
- Do not redesign unrelated main-window UI beyond what is necessary to expose the actions.

Expected deliverables:
- Lock action
- Logout action
- Correct session cleanup behavior

Validation:
- Build the solution and report the result.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 10: Phase 4.1

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 4.1 only.

Task:
Add the minimal account-management service and Owner/Member permission checks.

Repository context:
- Owner can create Member accounts, disable Members, and reset Member credentials.
- Member cannot manage other accounts.
- This task is service-layer only.

Requirements:
- Implement role-aware account management rules in the service layer.
- Owner can create Member accounts, disable Members, and reset Member credentials.
- Member cannot manage other accounts.
- Keep the scope local-only and minimal.
- Do not build the full UI in this task.

Expected deliverables:
- Account management service(s)
- Role checks
- Clear failure behavior for unauthorized operations

Validation:
- Build the solution and report the result.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 11: Phase 4.2

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 4.2 only.

Task:
Add the minimal Owner-only account management UI.

Repository context:
- The UI should live in the existing desktop settings area if possible.
- This task should not become a broad visual redesign.

Requirements:
- Reuse the existing settings area if possible.
- Add only the smallest UI needed for:
  - listing accounts,
  - creating a Member,
  - disabling a Member,
  - resetting Member credentials.
- Member users must not have access to these controls.
- Avoid broad visual refactors.

Expected deliverables:
- Owner-only account management UI
- Required view model changes

Validation:
- Build the solution and report the result.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 12: Phase 4.3

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 4.3 only.

Task:
Add the Recovery Key flow for Owner password recovery and the credential-reset flows for Member accounts.

Repository context:
- Owner master-password recovery is local-only and uses Recovery Key.
- Member credential reset is performed by Owner.

Requirements:
- Owner must be able to reset the Owner master password using the Recovery Key.
- Owner must be able to reset Member master passwords and PINs.
- Recovery Key must remain one-time-display only; do not implement plaintext recall.
- Keep the flow local-only and do not add any cloud recovery fallback.

Expected deliverables:
- Recovery Key reset flow
- Member credential reset flow
- Correct re-wrapping of EncryptedDataKey where needed

Validation:
- Build the solution and report the result.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 13: Phase 5.1

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 5.1 only.

Task:
Add ciphertext columns for the defined sensitive fields and build the migration infrastructure.

Repository context:
- Sensitive-field scope is already defined in docs/P4_LOCAL_ACCOUNT_SECURITY_DESIGN.md.
- This task is schema and migration infrastructure only.

Requirements:
- Use the sensitive-field scope already defined in the design document.
- Add ciphertext columns without breaking repeated startup.
- Keep the migration idempotent.
- Do not switch repository read/write behavior yet in this task.

Expected deliverables:
- Updated schema migration
- Safe/idempotent migration structure

Validation:
- Build the solution and report the result.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 14: Phase 5.2

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 5.2 only.

Task:
Migrate legacy plaintext sensitive data into ciphertext columns.

Repository context:
- Ciphertext columns already exist from the previous task.
- This task should backfill existing plaintext rows safely.

Requirements:
- Detect rows that still contain legacy plaintext.
- Encrypt with the current account DataKey.
- Backfill ciphertext columns.
- Clear or neutralize the legacy plaintext columns according to the design.
- Keep the process safe against partial failure as much as possible.

Expected deliverables:
- Data backfill logic
- Safe plaintext cleanup behavior

Validation:
- Build the solution and report the result.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 15: Phase 5.3

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 5.3 only.

Task:
Update repositories so they become the sole encryption/decryption boundary for sensitive fields.

Repository context:
- The upper layers should keep using the existing business models in plaintext.
- Repositories must absorb the encryption/decryption complexity.

Requirements:
- For reads, decrypt ciphertext columns and map back to the existing models.
- For writes, encrypt sensitive values before persistence.
- Keep the upper layers unaware of the encryption details.
- Avoid unrelated refactors while touching repositories.

Expected deliverables:
- Repository updates for encrypted field handling
- Any shared helper methods needed for consistency

Validation:
- Build the solution and report the result.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```

### Prompt 16: Phase 5.4

```text
Follow the local AGENTS.md instructions. Work implementation-first. Inspect the existing repository before changing code. Keep the change set minimal and safe. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Do not introduce new dependencies unless clearly necessary. Do not refactor unrelated code. After changes, run the smallest relevant build/check commands and report the real results.

Implement Phase 5.4 only.

Task:
Update backup/restore assumptions where necessary and run the final regression validation for the local-account security feature set.

Repository context:
- The app will now have launcher.db plus per-account business databases.
- Backup/restore must not assume a single database file anymore.

Requirements:
- Inspect the existing backup/restore implementation for single-database assumptions.
- Make only the minimal changes required to support launcher.db plus per-account business databases.
- Validate the end-to-end flows:
  - cold-start login,
  - account isolation,
  - PIN resume unlock,
  - Recovery Key reset,
  - encrypted field read/write,
  - backup/restore consistency.
- Do not expand the feature scope beyond local desktop security.

Expected deliverables:
- Minimal backup/restore updates if needed
- Final verification summary

Validation:
- Run the relevant build/check commands and report the real results.

Final response format:
1. Done
2. Changed files
3. Key decisions
4. Verification / commands run
5. Remaining risks or next step
```
