# Implementation Plan: 设置页与我的页精装升级 (settings-profile-refinement)

## Overview

本计划由已确认的 `design.md` 与 `requirements.md` 推导而来，技术栈为 C# / WPF / .NET（`CommunityToolkit.Mvvm`，沿用 `[ObservableProperty]` / `[RelayCommand]` 约定）。任务遵循「先纯逻辑与后端接缝，再 ViewModel 抽出，最后 XAML 重构与集成接线」的增量顺序，每一步均保持可编译、可运行，避免悬空代码。

本轮在原 BC-1~BC-9 基础上并入一组已确认的产品决策（§10.5 BC-10~BC-14、§8.1.1、§9.5~§9.8），新增/调整任务覆盖：离开页导航闸门 + 失败人话提示、主密码提交门槛放宽（强度仅警告）、成员权限矩阵与删除成员（删除仅移除登录账号、保留名下历史业务数据与来源/创建人归属标签）、凭证修改后会话转移、Owner 紧急启用（独立紧急入口弹窗，不在登录页）与受限权限模式（仅放行数据备份/导入导出恢复等数据抢救操作）、和钱相关机密页面（现金流、经营建议等含财务数据页面）的 PIN 门禁 + 应用级会话锁定触发点（最小化到托盘 / 系统恢复后锁定）、头像格式与大小校验及渐变首字占位、安全审计扩展（`MemberDeleted` + 日期范围查询、默认最近 30 天窗口 + 全量保留）、搜索命中超 12 条的简洁提示。

实施口径（遵循 AGENTS.md 讨论优先约束）：以下任务定义「应做什么」与「按何顺序做」。每个任务动代码前仍以用户的显式施工指令为准；BC-6/BC-11/BC-12/BC-13/BC-14 涉及加密存储、防篡改写入与明文 PIN/密码处理，必须严格遵守 P0/P4 安全底线（加密存储、防篡改、绝不泄露明文凭证、明文即用即清）。

标注约定：
- 带 `*` 的子任务为可选测试任务（单元 / 属性 / 集成测试），可在追求 MVP 时跳过。
- 顶层任务与检查点不带 `*`。
- 属性测试任务显式回链设计文档中的 Property 编号与对应需求条款。

## Tasks

- [ ] 1. 设计令牌与共享样式基座
  - [x] 1.1 新增 `Views/Resources/DesignTokens.xaml` 资源字典
    - 定义圆角刻度（`RadiusSm/Md/Lg/Xl/Pill`）、阴影刻度（`ElevationCard/Raised/Toast`）、间距刻度（`Space*`）、字阶令牌（`Type*`）
    - 新增设置/我的页专用样式键：`RefineNavListStyle`、`RefineNavItemStyle`、`RefineSearchBoxStyle`、`RefineIdentityHeaderStyle`、`RefineToastStyle`、`RefineStatusBadgeStyle`、`RefinePasswordRevealButtonStyle`、`RefineStrengthMeterStyle`
    - 仅供两页消费，沿用既有画刷键，不污染其它页面
    - _Requirements: 15.1, 15.2_

- [x] 2. 头像偏好持久化模型与键 (BC-1, BC-2, BC-3)
  - [x] 2.1 为 `AppPreferences` 新增可空字段 `AvatarReference` 并在 `AppSettingKeys` 新增同风格键
    - 默认 `null` 表示使用默认头像，保持向后兼容
    - _Requirements: 11.1, 11.2_

  - [x] 2.2 在仓储经现有 KV upsert 通道读写 `AvatarReference`
    - `SavePreferencesAsync` / `GetPreferencesAsync` 读写新键，不引入 schema 迁移
    - _Requirements: 11.3_

  - [x] 2.3 编写偏好读写往返单元测试
    - 验证 `AvatarReference` 写入后可正确读回，旧数据（无该键）读取为 `null`
    - _Requirements: 11.1, 11.3_

- [ ] 3. 头像存储服务与校验约束 (BC-4)
  - [x] 3.1 定义 `IAvatarStorageService` 接口、`AvatarReference` 记录与 `AvatarConstraints` 常量
    - 接口含 `SaveAvatarAsync` / `ResolveAvatarPath` / `RemoveAvatarAsync`
    - `AvatarConstraints`：`AcceptedFormats = {JPG, PNG, WebP}`、`MaxFileSizeBytes = 5MB`、`ThumbnailEdge = 256`
    - _Requirements: 11.4, 6.7, 6.1_

  - [x] 3.2 实现头像存储服务
    - 按 `AvatarConstraints` 校验真实编码格式（JPG/PNG/WebP）/大小（≤5MB）/可解码 → 解码再编码剥离 EXIF → 缩放方形缩略图 → 写入 app 数据目录 `avatars/` 子目录并 `HardenFile` 加固
    - 仅存相对引用键；`ResolveAvatarPath` 仅解析到受保护子目录内，拒绝越界绝对路径
    - _Requirements: 6.1, 6.2, 6.7_

  - [x] 3.3 编写头像隔离属性测试
    - **Property 8: 头像隔离**
    - **Validates: Requirements 6.7, 6.4, 11.3**

  - [x] 3.4 编写头像格式与大小校验属性测试
    - **Property 16: 头像格式与大小校验**
    - **Validates: Requirements 6.1, 6.6**

  - [x] 3.5 编写无效/过大图片拒绝单元测试
    - 验证非法格式、不可解码或超过 5MB 的图片被拒绝、抛验证异常、保留原头像并就地提示「图片无效或过大」
    - _Requirements: 6.6_

- [ ] 4. 密码强度评估纯函数
  - [x] 4.1 实现 `PasswordStrengthEvaluator.Evaluate` 与 `PasswordStrength` 枚举
    - 长度/字符类别正向打分 + `WeakPatternPenalty`（长重复串、简单顺序模式）惩罚降档
    - 空串→`Empty`，长度 < 8 一律 `Weak`
    - _Requirements: 8.5, 8.9_

  - [x] 4.2 编写强度单调性（条件性）属性测试
    - **Property 1: 强度单调性（条件性）**
    - **Validates: Requirements 8.9, 8.5**

  - [x] 4.3 编写强度档边界单元测试
    - 长度边界 8/12/16、类别数 2/3/4 的档位正确；弱模式触发降档
    - _Requirements: 8.5_

- [ ] 5. 凭证校验值对象与实时校验逻辑
  - [x] 5.1 定义 `PasswordValidationState` / `PinValidationState` 记录
    - 主密码态含 `IsCurrentProvided` / `IsNewLengthValid` / `IsConfirmMatch` / `IsStrengthWeak`（仅警告，不参与 `CanSubmit`）/ `Message`
    - `CanSubmit` 仅由「当前非空 + 新密码长度 >= 8 + 两次一致」组成；PIN 态由「当前非空 + 恰好 6 位纯数字 + 两次一致」组成
    - _Requirements: 8.1, 8.3, 8.10_

  - [x] 5.2 实现 `RecomputeMasterPasswordValidation` 与 `RecomputePinValidation`
    - 主密码提交门槛放宽：当前非空 + 新密码长度 >= 8 + 两次一致即 `CanSubmit`；新密码强度**不**作为硬门槛，强度低于 `Fair` 时仅置 `IsStrengthWeak=true` 并产出中文偏弱警告文案
    - PIN：当前非空 + 恰好 6 位纯数字 + 两次一致；产出中文实时提示
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.10_

  - [x] 5.3 编写提交门槛一致性属性测试
    - **Property 2: 提交门槛一致（强度非硬门槛）**
    - **Validates: Requirements 8.2, 8.4, 8.1, 8.3, 8.10**

  - [x] 5.4 编写校验文案与边界单元测试
    - 缺当前凭证 / 新值过短或位数错 / 两次不一致 / 全部通过的 `CanSubmit` 与文案；长度 >= 8 但强度偏弱时 `IsStrengthWeak=true` 且 `CanSubmit=true`
    - _Requirements: 8.1, 8.3, 8.10_

- [ ] 6. 设置搜索索引与过滤
  - [x] 6.1 定义 `SettingsSearchEntry` 记录、`ISettingsSearchIndex` 接口与静态条目索引
    - 每条登记标题/描述/关键字/`CategoryKey`(六大分类之一)/非空 `AnchorId`
    - _Requirements: 2.4_

  - [x] 6.2 实现 `Query` 过滤/排序算法与超限标志
    - 不区分大小写；标题前缀 100 > 标题子串 60 > 描述 25 > 关键字 20；命中权重降序、同分按 `CategoryKey` 稳定排序；空/空白查询返回空
    - 上限 12 条；当原始命中数超过 12 时返回截断结果并暴露「结果超限」标志供 UI 提示
    - _Requirements: 2.1, 2.2, 2.5, 2.6, 2.8_

  - [x] 6.3 编写搜索分类闭合属性测试
    - **Property 3: 搜索分类闭合**
    - **Validates: Requirements 2.4, 2.6**

  - [x] 6.4 编写搜索确定性属性测试
    - **Property 4: 搜索确定性**
    - **Validates: Requirements 2.5, 2.2, 2.1**

  - [x] 6.5 编写排序与截断单元测试
    - 前缀 > 子串 > 描述 > 关键字 的排序；MaxResults=12 截断 + 超限标志置位；大小写不敏感
    - _Requirements: 2.1, 2.6, 2.8_

- [ ] 7. 保存结果聚合、稳定错误码与失败人话文案
  - [x] 7.1 定义 `SettingsSaveOutcome` 记录与 `MapToStableErrorCode`
    - 持久化失败→`SET-1001`、校验未过→`SET-1002`、热键失败→`SET-1003`、其它→`SET-1999`；映射只产出短码，不向 UI 文案泄露异常类型名/堆栈
    - _Requirements: 3.7, 3.4_

  - [x] 7.2 实现 `BuildFailureToastMessage` 人话文案拼装
    - 按错误码取面向普通用户的中文「人话」说明为主体，错误码以括注辅助附带（如「…请稍后重试（错误码：SET-1001）」）；不含内部异常细节
    - _Requirements: 3.4_

  - [x] 7.3 编写错误码全覆盖属性测试
    - **Property 6: 错误码全覆盖**
    - **Validates: Requirements 3.7, 3.4**

  - [x] 7.4 编写失败提示人话+错误码属性测试
    - **Property 11: 失败提示含人话说明与错误码**
    - **Validates: Requirements 3.4, 3.7**

  - [x] 7.5 编写错误码映射单元测试
    - IO/DB→SET-1001、校验→SET-1002、热键→SET-1003、其它→SET-1999
    - _Requirements: 3.7_

- [ ] 8. 成员删除服务与权限矩阵 (BC-10)
  - [x] 8.1 实现成员管理权限矩阵纯函数判定（§8.1.1）
    - `CanCreateMember = IsCurrentUserOwner`；`CanDeleteMember(t) = IsCurrentUserOwner AND t 非自身`；`CanDisableMember(t) = IsCurrentUserOwner OR t 为自身`
    - 仅依据「当前账号角色 / 是否自身」，不读取 UI 状态，便于属性测试
    - _Requirements: 7.5, 7.6, 7.7, 7.8, 7.9_

  - [x] 8.2 在 `ILocalAccountManagementService` 新增删除成员能力并实施服务层权限校验
    - 删除=移除该成员的登录账号本身（账号不再存在），与停用（仅置 `IsEnabled=false` 并保留账号）作为彼此区分的两种独立能力
    - 删除仅移除登录账号，保留其名下全部历史业务数据：不级联删除业务数据、不匿名化；业务数据的「来源/创建人」仍显示该（已删除）账号，即保留用于归属展示的账号标签/标识
    - 服务层依据权限矩阵双重校验，被拒绝时不执行后端操作；删除成功后由审计接缝记 `MemberDeleted`（见 11.5）
    - _Requirements: 7.4, 7.6, 7.10_

  - [x] 8.3 编写成员管理权限矩阵属性测试
    - **Property 12: 成员管理权限矩阵**
    - **Validates: Requirements 7.5, 7.6, 7.7, 7.8, 7.9, 7.10**

  - [x] 8.4 编写删除/停用区分单元测试
    - 删除后账号不存在；停用后账号保留且 `IsEnabled=false`、可重新启用；任何人不可删自身、Owner 可停用自身
    - 删除成员后其名下历史业务数据仍全部保留、未被级联删除或匿名化，且业务数据来源/创建人仍展示该已删除账号的标签/标识
    - _Requirements: 7.8, 7.9, 7.10_

- [ ] 9. 会话转移、受限权限模式与敏感页面门禁接缝 (BC-11, BC-12, BC-13)
  - [x] 9.1 扩展会话上下文以表达受限权限模式
    - 新增 `SessionPermissionMode` 枚举（`Normal` / `Restricted_Permission`）；在既有 `ISessionContextService` 上扩展只读标志 `IsRestrictedPermissionMode`（最小改动，不新增账号角色）
    - _Requirements: 17.1_

  - [x] 9.2 实现凭证修改后的会话转移 `OnCredentialChangeCompleted`（§9.6）
    - 成功路径先经 `ISecurityAuditService` 记 `CredentialChanged`（仅元数据，绝不含明文）；主密码改成功 → `ISessionContextService` 强制登出并要求重新登录；PIN 改成功 → `ISessionLockService.LockManually` 进入 `PendingPinUnlock`（不强制登出）
    - 失败或取消 → 会话状态保持不变，既不登出也不锁定
    - _Requirements: 16.1, 16.2, 16.3, 16.4_

  - [x] 9.3 实现 Owner 紧急启用 `TryEmergencyEnable` 与受限模式允许操作白名单（§9.7）
    - 前置：账号为 `Owner` 且 `IsEnabled=false`；校验正确 6 位 PIN → 进入 `Restricted_Permission_Mode`；PIN 错误 → 拒绝并给中文错误提示「PIN 不正确，无法紧急启用」
    - 受限模式仅放行「数据抢救类」操作白名单：数据备份、数据导出/导入恢复；其余一律拒绝（现金流/经营建议等所有和钱相关机密页面、成员管理创建/删除/停用/重置、设置内安全与数据高危项、日常业务数据编辑）。白名单以纯函数 `IsOperationAllowedInRestrictedMode(operationKind)` 表达，便于服务层与 VM 层复用并测试
    - 入口为「独立的紧急入口弹窗」采集的 6 位 PIN（非登录页，登录页保持不变），后端接缝只接收 PIN 与目标 Owner 账号
    - 成功/失败均经 `ISecurityAuditService` 记审计（不含明文 PIN）；明文 PIN 校验后即清，不延长、不写日志
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 17.5, 13.2_

  - [x] 9.4 实现敏感页面门禁 `ISensitivePageGuard.TryEnterAsync`（§9.8）
    - 门禁范围限定为「和钱相关的机密页面」：现金流，以及经营建议等含财务数据的页面；库存、商品等非财务敏感页面不纳入门禁，避免过度打扰
    - 进入机密页面前若当前会话处于锁定（`PendingPinUnlock`）→ 先走解锁流程；被锁定期间机密内容恒不渲染（门禁与应用级会话锁定协同）
    - 受限模式短路：处于 `Restricted_Permission_Mode` → `BlockedByRestricted`（先于 PIN 校验）；PIN 错误 → `PinRejected`（中文提示）；PIN 正确且非受限 → `Granted`
    - 未通过验证时机密内容恒不渲染；明文 PIN 校验后即清，不延长其内存生命周期
    - _Requirements: 18.1, 18.2, 18.3, 18.4, 18.5, 17.4, 13.3_

  - [x] 9.5 编写凭证修改后会话转移属性测试
    - **Property 13: 凭证修改后会话转移**
    - **Validates: Requirements 16.1, 16.2, 16.3, 16.4**

  - [x] 9.6 编写敏感页面门禁与受限模式机密保护属性测试
    - **Property 14: 敏感页面 PIN 门禁与受限模式机密保护**
    - **Validates: Requirements 18.1, 18.2, 18.3, 18.4, 18.5, 17.1, 17.2, 17.3, 17.4, 17.5**

  - [x] 9.7 编写明文 PIN 即用即清单元测试
    - 验证紧急启用与门禁校验后明文 PIN 不残留、不写日志/诊断
    - _Requirements: 18.5, 17.5, 14.5_

  - [x] 9.8 扩展应用级会话锁定触发点（PIN 保护主模型）
    - 以「应用级会话锁定」作为 PIN 保护主模型：新增/扩展锁定触发点——最小化到托盘时锁定（立即锁定，并附「最小化后经过空闲时限再锁定」的可选时限策略，默认立即锁定）、系统睡眠/恢复后锁定（复用既有 `LockBySystemResume`）
    - 锁定统一进入 `PendingPinUnlock`（复用既有 `ISessionLockService.LockManually` 与解锁流程）；锁定后再次进入应用须输入正确 PIN 方可解锁
    - 仅扩展触发点与可选时限策略，不新增账号角色、不改动 `PendingPinUnlock` 既有解锁交互
    - _Requirements: 18.1, 18.2, 13.3_

  - [x] 9.9 编写会话锁定触发点单元测试
    - 最小化到托盘立即锁定；启用空闲时限策略时到时锁定、未到不锁定；系统恢复后锁定；锁定后须 PIN 解锁
    - _Requirements: 18.1, 18.2_

- [ ] 10. 壳层通用 Toast 服务 (BC-7)
  - [x] 10.1 定义 `IToastService` 接口与 `ToastSeverity` 枚举
    - `Show(message, severity, duration?)`
    - _Requirements: 10.7_

  - [x] 10.2 在 `MainWindow` 壳层泛化 `Popup_CopyToast` 并实现 `IToastService`
    - 复制提示与设置保存提示统一经其呈现；ViewModel 不直接操作控件；成功/失败用不同 `ToastSeverity` 着色
    - _Requirements: 10.7_

- [ ] 11. 安全审计后端 (BC-6, BC-14)
  - [x] 11.1 定义 `SecurityAuditEventKind` 枚举与 `SecurityAuditEntry` 记录
    - 含 `LoginSucceeded`/`LoginFailed`/`AccountLockedOut`/`CredentialChanged`/`MemberCreated`/`MemberPasswordReset`/`MemberDisabled`/`MemberDeleted`，独立于 `ActivityLog.ActivityType`
    - _Requirements: 12.1_

  - [x] 11.2 定义 `ISecurityAuditService` 接口（`RecordAsync` / `QueryAsync`）
    - 写入接缝绝不接收/记录明文凭证；`QueryAsync` 增加 `from/to` 时间范围参数，按账号或时间范围返回顺序稳定列表
    - _Requirements: 12.5, 9.7_

  - [x] 11.3 实现防篡改加密存储写入与全量保留
    - 写入 SQLCipher 加密本地存储，追加式 + 完整性校验保持防篡改；仅保存事件类型/时间/账号标签/脱敏 detail；完整保留全部历史，不截断或自动清除
    - _Requirements: 12.2, 12.3, 12.4, 14.1, 14.2, 9.6_

  - [x] 11.4 实现 `QueryAsync` 日期范围读取 API
    - 按账号/时间范围（`from/to`，含边界）返回顺序稳定的 `SecurityAuditEntry` 列表，仅返回落在范围内的子集；空结果返回空列表不抛异常
    - _Requirements: 12.5, 9.2, 9.7_

  - [x] 11.5 在认证/账户服务层植入审计写入接缝
    - `ILocalAuthService` 登录成功/失败、账户锁定；`ILocalAccountManagementService` 凭证变更、成员创建/重置/停用/**删除**，各恰好记录一条对应类型（`MemberDeleted` 由 8.2 删除路径触发）
    - _Requirements: 12.2_

  - [x] 11.6 编写安全审计完整性与降级属性测试
    - **Property 9: 安全审计完整性与降级**
    - **Validates: Requirements 12.2, 12.1, 12.3, 12.5, 9.3, 14.2**

  - [x] 11.7 编写审计按日期筛选与全保留属性测试
    - **Property 15: 审计按日期筛选与全保留**
    - **Validates: Requirements 9.6, 9.7, 12.5**

  - [x] 11.8 编写凭证不泄露属性测试
    - **Property 7: 凭证不泄露**
    - **Validates: Requirements 14.3, 14.4, 8.8, 12.4**

- [x] 12. 检查点 - 纯逻辑、后端接缝与安全审计
  - 确保所有测试通过，如有疑问询问用户。

- [ ] 13. 完整抽出 `SettingsViewModel` (BC-8, BC-9)
  - [x] 13.1 创建 `SettingsViewModel` 骨架并迁移 P0 状态与映射
    - 迁入 `SettingsP0.cs` / `SettingsP0.Mapping.cs` 的状态与 `*Input`↔`AppPreferences` 规范化映射，保持可编译
    - _Requirements: 10.2_

  - [x] 13.2 迁移自动保存引擎与保存结果记录
    - 入队防抖 `ProcessQueuedSettingsAutoSaveAsync` → `SaveP0SettingsAsync`（即改即存语义不变），成功/异常路径写入 `LastSaveOutcome`
    - _Requirements: 3.1, 3.7_

  - [x] 13.3 迁移设置命令与状态文案
    - 迁入 `SettingsP0.Commands.cs` / `SettingsP0.Status.cs`（备份/校验/导入恢复触发、`SettingsStatusMessage`）
    - _Requirements: 10.2_

  - [x] 13.4 迁移 P1 / AI 诊断 / 快捷键与通知分部
    - 迁入 `SettingsP1.cs`、`SettingsP1.Ai.cs`、`SettingsP1.HotkeysNotifications.cs`（含 `TryApplyRuntimeHotkeysBeforeSave`）；`*Input` 按六大分类成组
    - _Requirements: 10.2_

  - [x] 13.5 集成设置搜索状态与命中跳转信号
    - `SettingsSearchQuery` / `SearchResults` / `SelectedCategoryKey` / `PendingScrollAnchorId` 与 `ActivateSearchResultCommand`；暴露「结果超限」信号供 UI 提示
    - _Requirements: 2.3, 2.7, 2.8_

  - [x] 13.6 实现离开页保存结果聚合与导航闸门 `TryLeaveSettings`（§9.5）
    - `MainViewModel.OnSelectedSectionChanging` 捕获旧值；当旧=「设置」且新≠「设置」时 `FlushPendingAutoSaveAsync` → 依最近一次保存结果决定放行/阻止
    - 放行 ⟺（最近一次成功 ∨ 本次停留未保存）：成功弹「设置已保存」并清空 `LastSaveOutcome`；最近一次失败 → 阻止离开（取消导航或拉回 `SelectedSection="设置"`）并经 `BuildFailureToastMessage` 弹人话+错误码警示 Toast，不清空以便再次拦截
    - _Requirements: 3.2, 3.3, 3.4, 3.5, 3.6, 3.8_

  - [x] 13.7 编写 Toast 不重复属性测试
    - **Property 5: Toast 不重复**
    - **Validates: Requirements 3.5, 3.6**

  - [x] 13.8 编写离开页导航闸门属性测试
    - **Property 10: 离开页导航闸门**
    - **Validates: Requirements 3.8, 3.2, 3.3, 3.5**

  - [x] 13.9 编写离开页聚合单元测试
    - 旧=设置且新≠设置才触发；无保存不弹；成功消费后清空避免重复；失败阻止离开并保留结果
    - _Requirements: 3.2, 3.5, 3.6, 3.8_

- [ ] 14. 抽出 `MeProfileViewModel` (BC-8)
  - [x] 14.1 创建 `MeProfileViewModel` 骨架并迁移身份/成员/凭证状态
    - 从 `MainViewModel` 迁入我的页相关状态；角色徽章文案与失败回退「系统店员 Member」
    - _Requirements: 10.1, 5.4, 5.5_

  - [x] 14.2 接线成员管理与权限矩阵命令
    - `ManagedAccountsView` 搜索过滤、基于 `IsEnabled` 的状态徽章、空状态；创建/重置密码/重置 PIN/停用/`DeleteMemberCommand`，命令 `CanExecute` 复用权限矩阵（§8.1.1），删除入口须二次确认，被拒绝给中文提示且不调后端
    - 删除二次确认文案明确「仅移除登录账号，其名下历史业务数据与来源/创建人归属标签保留」，避免误解为级联删除业务数据
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8, 7.9, 7.10_

  - [x] 14.3 接线凭证修改表单与会话转移调用
    - 主密码/PIN 实时校验状态、强度计、`CanSubmit`（强度仅警告不阻断）、就地反馈、命令完成后清空相关输入；命令成功后调用 `OnCredentialChangeCompleted`（主密码→强制登出、PIN→`PendingPinUnlock`）
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.7, 8.8, 8.10, 14.4, 16.1, 16.2_

  - [x] 14.4 接线头像命令
    - `ChangeAvatarCommand` / `RemoveAvatarCommand`：经 `IAvatarStorageService` 按 `AvatarConstraints` 处理、持久化 `AvatarReference`、即时刷新 `AvatarImageSource`；无引用时回退「渐变底色 + 用户名首字（中文取首个汉字、拉丁取首字母大写）」占位；拒绝时保留原头像并就地提示
    - _Requirements: 6.3, 6.4, 6.5, 6.6_

  - [x] 14.5 接线账户安全/登录记录与日期范围筛选
    - 经 `ISecurityAuditService.QueryAsync(from,to)` 拉取 `SecurityAuditEntries`；`AuditRangeStart`/`AuditRangeEnd` 与 `ApplyAuditDateRangeCommand`；`IsSecurityAuditAvailable` 判定、空状态/读取失败降级、`LastLoginAt` 直显
    - 默认展示「最近 30 天」窗口：初始 `AuditRangeStart=今天-30天`、`AuditRangeEnd=今天`；用户可经日期范围筛选查更早记录，底层全量记录仍全部保留
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7_

  - [x] 14.6 接线 Owner 紧急启用与受限模式状态
    - 经「独立的紧急入口弹窗」采集 6 位 PIN → 调 `TryEmergencyEnable`（不放在登录页，登录页保持不变）
    - 暴露受限模式只读状态供 UI 提示与能力门控：仅放行数据抢救类操作（数据备份、数据导出/导入恢复），其余入口（现金流/经营建议等机密页面、成员管理、设置内安全与数据高危项、日常业务编辑）一律禁用
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 13.2_

  - [x] 14.7 编写我的页 ViewModel 单元测试
    - 角色徽章回退、成员空状态、权限矩阵命令 `CanExecute`、审计空/失败降级、命令完成后清空输入
    - _Requirements: 5.5, 7.3, 9.3, 9.4, 8.8_

- [x] 15. 检查点 - ViewModel 抽出与回归
  - 确保所有测试通过，如有疑问询问用户。

- [ ] 16. `PasswordBox` MVVM 附加行为 (BC-8)
  - [x] 16.1 实现 `PasswordBoxBinder` 附加属性
    - 桥接 `PasswordBox.Password` ↔ VM 字符串属性；VM 值置空时清空控件；防重入；不缓存、不写日志
    - _Requirements: 10.3, 10.4_

  - [x] 16.2 编写置空清空行为单元测试
    - 验证 VM 字符串被置空时控件内容随之清空，无循环回写
    - _Requirements: 10.4_

- [ ] 17. 重构 `SettingsView`（XAML）
  - [x] 17.1 以左导航 + 右内容区替换 `TabControl`
    - 左 `ListBox`(六分类) + 右 `ContentControl`/分区 `ScrollViewer`；`DataContext` 绑定 `{Binding Settings}`；默认选中「外观与启动」；加入稳定锚点 `Pane_SettingsContent`、`Nav_SettingsCategories`、`Box_SettingsSearch`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 4.1, 4.2, 4.3_

  - [x] 17.2 接入搜索框、命中跳转与超限提示
    - 绑定 `SettingsSearchQuery`；轻量附加行为监听 `PendingScrollAnchorId` → `BringIntoView` + 临时高亮；空结果不触发跳转/高亮；命中超 12 条显示「结果较多，请输入更精确的关键词」
    - _Requirements: 2.3, 2.7, 2.8_

  - [x] 17.3 应用设计令牌并清理错号注释
    - `SettingsTab*` 子控件按六大分类重新归类宿主、绑定路径调整为相对 `SettingsViewModel`；消除旧 Tab 错号注释
    - _Requirements: 15.1, 15.2_

- [ ] 18. 重构 `MeProfileView`（XAML）
  - [x] 18.1 重建为「身份头图 + 卡片堆叠」单列布局
    - 顶部 `IdentityHeader`（头像 + 显示名 + 角色徽章 + 锁定/登出快捷操作）；下方卡片纵向堆叠并居中约束最大宽度
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [x] 18.2 成员管理卡 UI
    - 搜索框、启用/禁用状态徽章、空状态提示；创建/重置/停用/删除操作入口，按权限矩阵显隐（`Member` 不显示创建入口），删除入口二次确认
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.8, 7.9_

  - [x] 18.3 凭证修改卡 UI
    - 实时校验文案、强度计（`RefineStrengthMeterStyle`）、密码显隐切换、经 `PasswordBoxBinder` 绑定；长度达标但强度偏弱时显示中文警告但不禁用提交
    - _Requirements: 8.1, 8.3, 8.5, 8.6, 8.7, 8.10_

  - [x] 18.4 头像 UI
    - 点击更换头像、无引用时默认「渐变底色 + 首字」占位、就地「图片无效或过大」提示
    - _Requirements: 6.1, 6.5, 6.6_

  - [x] 18.5 账户安全/登录记录卡 UI
    - 审计历史列表、日期范围筛选控件（起止日期 + `ApplyAuditDateRangeCommand`）、空状态/读取失败文案、最近登录时间展示
    - 默认显示「最近 30 天」窗口（默认起止日期 = 今天-30天 / 今天），并提示可调整日期范围查看更早记录
    - _Requirements: 9.1, 9.3, 9.4, 9.5, 9.6, 9.7_

  - [x] 18.6 Owner 紧急启用入口与受限模式提示
    - 紧急启用入口为「独立的紧急入口弹窗」（不放在登录页，登录页保持不变），弹窗采集 6 位 PIN 调 `TryEmergencyEnable`
    - 受限模式下提示「仅数据备份/导入导出恢复等数据抢救操作可用」，并禁用机密数据入口（现金流/经营建议等）、成员管理与设置内高危项入口
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 13.2_

  - [x] 18.7 应用设计令牌并验证主题切换
    - 与设置页一致的圆角/阴影/留白；深浅色主题无视觉异常
    - _Requirements: 15.1, 15.2_

- [ ] 19. 敏感页面 PIN 门禁接线 (BC-12)
  - [x] 19.1 和钱相关机密页面进入前 PIN 门禁接线
    - 门禁范围限定为「和钱相关的机密页面」：现金流，以及经营建议等含财务数据的页面；库存、商品等非财务敏感页面不接线门禁
    - 以进入前 PIN 验证遮罩 / 路由拦截叠加在目标页面之上；进入前若会话处于锁定（`PendingPinUnlock`）先解锁；未通过验证时机密内容恒不渲染（仅显示 PIN 遮罩或被拦截回退）；不改动该页面进入后自身既有 UI 结构与布局
    - _Requirements: 18.1, 18.2, 18.3, 13.3_

  - [x] 19.2 受限模式拒绝机密页面接线
    - 处于 `Restricted_Permission_Mode` 时门禁恒返回 `BlockedByRestricted`，拒绝进入现金流/经营建议等和钱相关机密页面，与受限模式仅放行数据抢救类操作的白名单口径一致
    - _Requirements: 18.4, 17.4_

  - [x] 19.3 编写门禁接线集成测试
    - PIN 正确放行渲染、PIN 错误拒绝且机密不渲染、会话锁定先解锁、受限模式拒绝；库存/商品等非财务页面不触发门禁；不改动页面内部 UI
    - _Requirements: 18.1, 18.3, 18.4, 13.3_

- [ ] 20. 可见性统一与 QA 锚点修复 (BC-9 / QA)
  - [x] 20.1 上移 `MeProfileView` 可见性绑定至 `MainWindow.xaml`
    - 用 `SelectedSection` + `SectionVisibilityConverter` 统一控制，移除根元素内联可见性绑定
    - _Requirements: 10.5, 10.6_

  - [x] 20.2 更新 QA smoke 脚本锚点
    - `tools/qa/run-uia-smoke.ps1` 将 settings 项 `ContentId` 由 `Btn_SavePreferences` 改为 `Pane_SettingsContent`
    - _Requirements: 4.4_

- [ ] 21. 集成与接线
  - [x] 21.1 注册服务并接线 DataContext
    - DI 注册 `IToastService`、`IAvatarStorageService`、`ISecurityAuditService`、`ISettingsSearchIndex`、`ISensitivePageGuard` 及会话受限模式扩展；`MainViewModel` 暴露 `Settings` / `MeProfile` VM；UI 改动严格限定在两页及相关共享样式/壳层 Toast/QA 锚点 + 敏感页面门禁访问控制层，其它页面（含登录页）保持不变
    - _Requirements: 10.1, 10.2, 10.7, 13.1, 13.2, 13.3_

  - [x] 21.2 编写端到端集成测试
    - 改设置 → 离开页成功放行/失败阻止并人话 Toast；搜索 → 跳转分类 + 定位高亮 + 超限提示；头像上传 → 重启保留；凭证修改 → 审计写入且无明文 + 主密码改强制登出/PIN 改锁定；删除成员 → 账号移除并记 `MemberDeleted`；敏感页面 → PIN 门禁拦截
    - _Requirements: 3.2, 3.8, 2.3, 6.4, 12.2, 14.3, 16.1, 18.1_

- [x] 22. 最终检查点 - 全量回归
  - 确保所有测试通过，如有疑问询问用户。

## Notes

- 带 `*` 的任务为可选测试任务，可在快速 MVP 时跳过；核心实现任务不可跳过。
- 每个任务回链具体需求条款（granular），便于追溯。
- 属性测试覆盖设计文档 Property 1–16；单元测试覆盖具体边界与示例。
- 检查点用于增量验证，避免一次性大爆炸式改动（尤其 `SettingsViewModel` 完整抽出，回归风险较高）。
- 安全相关任务（BC-6/BC-11/BC-12/BC-13/BC-14 审计、会话、门禁、凭证、头像）须守住 P0/P4：加密存储、防篡改、绝不泄露明文凭证、明文 PIN/密码即用即清。
- 离开页导航闸门（13.6 / Property 10）与失败人话提示（7.2 / Property 11）协同：保存失败时阻止离开并拉回设置页。
- 遵循 AGENTS.md 讨论优先约束：每项动代码前以用户显式施工指令为准。

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "2.1", "3.1", "4.1", "5.1", "6.1", "7.1", "8.1", "9.1", "10.1", "11.1"] },
    { "id": 1, "tasks": ["2.2", "3.2", "4.2", "5.2", "6.2", "7.2", "8.2", "9.2", "10.2", "11.2"] },
    { "id": 2, "tasks": ["2.3", "3.3", "4.3", "5.3", "6.3", "7.3", "8.3", "9.3", "11.3"] },
    { "id": 3, "tasks": ["3.4", "5.4", "6.4", "7.4", "8.4", "9.4", "11.4"] },
    { "id": 4, "tasks": ["3.5", "6.5", "7.5", "9.5", "9.8", "11.5"] },
    { "id": 5, "tasks": ["9.6", "9.7", "9.9", "11.6", "11.7", "11.8"] },
    { "id": 6, "tasks": ["13.1", "14.1", "16.1"] },
    { "id": 7, "tasks": ["13.2", "14.2", "16.2"] },
    { "id": 8, "tasks": ["13.3", "14.3"] },
    { "id": 9, "tasks": ["13.4", "14.4"] },
    { "id": 10, "tasks": ["13.5", "14.5"] },
    { "id": 11, "tasks": ["13.6", "14.6"] },
    { "id": 12, "tasks": ["13.7", "14.7"] },
    { "id": 13, "tasks": ["13.8", "13.9"] },
    { "id": 14, "tasks": ["17.1", "18.1"] },
    { "id": 15, "tasks": ["17.2", "18.2"] },
    { "id": 16, "tasks": ["17.3", "18.3"] },
    { "id": 17, "tasks": ["18.4"] },
    { "id": 18, "tasks": ["18.5"] },
    { "id": 19, "tasks": ["18.6"] },
    { "id": 20, "tasks": ["18.7"] },
    { "id": 21, "tasks": ["19.1", "20.1", "20.2"] },
    { "id": 22, "tasks": ["19.2"] },
    { "id": 23, "tasks": ["19.3", "21.1"] },
    { "id": 24, "tasks": ["21.2"] }
  ]
}
```
