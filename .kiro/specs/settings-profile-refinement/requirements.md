# Requirements Document

> 需求文档：设置页与我的页精装升级 (settings-profile-refinement)

## Introduction

本需求文档由**已确认的设计文档 `design.md` 推导而来**（设计优先工作流：设计 → 需求 → 任务）。设计的主要技术决策（原 OQ-1..OQ-6）已由用户全部确认：头像方案 A（不入加密库）、安全审计后端纳入本期（BC-6）、设置六分类、完整抽出 `SettingsViewModel`、PIN 固定 6 位、离开页 Toast 仅限设置页。

本期范围以**设置页（Settings）**与**我的页（Me / Profile）**两个板块为主，目标是统一两页的视觉语言、信息架构、交互反馈与代码结构，同时保持「自动保存语义不变、加密本地存储不破坏、绝不延长明文凭证生命周期」三条安全底线。在此基础上，本次还纳入一组已确认的产品决策：离开设置页保存失败时的「人话」提示与导航阻止/拉回、凭证修改后的会话处理（主密码改后强制登出、PIN 改后锁定待解锁）、放宽主密码提交门槛（仅长度 >= 8 + 两次一致 + 当前非空，强度低于 Fair 仅警告）、成员管理权限边界与「删除成员」能力、Owner 被停用后的 PIN 紧急启用与受限权限模式、以及现金流等敏感页面的 PIN 门禁访问控制。

文中 UI 文案使用简体中文，代码标识符 / 类型名 / 属性名 / 错误码保持英文。涉及后端 / 共享层的改动（BC-1..BC-9 及本次新增的会话/门禁/审计接缝）虽已确认纳入作用域，但**具体实施仍遵循讨论优先约束**——动代码前需用户给出明确施工指令；本文件仅定义「应做什么」而非「何时施工」。

每条需求均力求精确、可测试，以便设计文档中的正确性属性回链到具体验收标准（`Validates: Requirements X.Y`）。

## Glossary

- **Settings_View**：设置页视图，重构后由左侧竖向导航 + 右侧内容区组成（替代原 `TabControl`）。
- **Settings_Navigation**：设置页左侧竖向导航（`Nav_SettingsCategories`），承载六个分类。
- **Settings_Search**：设置项搜索能力（含静态索引 `ISettingsSearchIndex` 与过滤排序逻辑），由搜索框 `Box_SettingsSearch` 驱动。
- **Settings_AutoSave**：设置页自动保存引擎（即改即存 + 防抖 + `SaveP0SettingsAsync` + `LastSaveOutcome`）。
- **Pane_SettingsContent**：设置页右侧内容区根元素的稳定 QA 内容锚点。
- **Toast_Service**：壳层通用提示服务 `IToastService`（由 `Popup_CopyToast` 泛化而来）。
- **MeProfile_View**：我的页视图，重构后为「顶部身份头图 + 卡片堆叠」单列布局。
- **MeProfile_ViewModel**：我的页独立 ViewModel（`MeProfileViewModel`，新增）。
- **SettingsViewModel**：设置页独立 ViewModel，完整承载原七个 `Settings*` 分部类的状态与逻辑。
- **Member_Management**：我的页成员管理卡（搜索/筛选/状态徽章/空状态/创建·重置·停用·删除操作，含基于角色的权限边界）。
- **Account_Role**：本地账号角色，取值 `Owner`（系统管理员）或 `Member`（系统店员）。仅 `Owner` 拥有成员创建/删除/停用等管理权限。
- **Session_Service**：会话服务 `ISessionService`（或既有会话上下文 `ISessionContextService` 的扩展），负责强制登出（要求重新登录）与会话锁定（进入待 PIN 解锁状态）。
- **Pending_Pin_Unlock**：会话锁定后的待 PIN 解锁状态（`PendingPinUnlock`），复用既有手动锁定机制，需输入正确 PIN 方可解锁，期间不强制登出。
- **Sensitive_Page_Guard**：敏感页面 PIN 门禁，进入现金流等账务核心机密/敏感页面前要求通过 6 位 PIN 验证（`ISensitivePageGuard`）。
- **Restricted_Permission_Mode**：受限权限模式，`Owner` 在被停用状态下凭 PIN「紧急启用」后所处的模式，可执行核心紧急操作（如数据备份）但不可查看现金流等机密/隐私数据。
- **Credential_Form**：我的页凭证修改卡（主密码 + PIN 的实时校验、强度计、显隐切换、就地反馈）。
- **Password_Strength_Evaluator**：密码强度评估纯函数，输出 `PasswordStrength`（`Empty`/`Weak`/`Fair`/`Good`/`Strong`）。
- **Security_Audit_Card**：我的页账户安全 / 登录记录卡。
- **Security_Audit_Service**：安全审计服务 `ISecurityAuditService`（写入接缝 + 读取 API，BC-6）。
- **Avatar_Storage_Service**：头像存储服务 `IAvatarStorageService`（校验/EXIF 剥离/缩放/受保护写入/解析，BC-4）。
- **AppPreferences**：应用偏好模型；新增可空字段 `AvatarReference`。
- **AppSetting_Repository**：偏好持久化仓储 `IAppSettingRepository`（`SavePreferencesAsync` / `GetPreferencesAsync`）。
- **PasswordBoxBinder**：将 `PasswordBox.Password` 桥接到 ViewModel 字符串属性的 MVVM 附加行为。
- **QA_Smoke_Script**：QA 自动化烟测脚本 `tools/qa/run-uia-smoke.ps1`。
- **Orderly_App**：WPF 应用层（`Orderly.App`）。
- **PasswordStrength**：密码强度档枚举，取值 `Empty`/`Weak`/`Fair`/`Good`/`Strong`。
- **SecurityAuditEventKind**：安全审计事件类型枚举（`LoginSucceeded`/`LoginFailed`/`AccountLockedOut`/`CredentialChanged`/`MemberCreated`/`MemberPasswordReset`/`MemberDisabled`/`MemberDeleted`）。

## Requirements

### Requirement 1: 设置页左侧导航信息架构

**User Story:** 作为使用者，我想要通过左侧竖向导航在设置页的六个分类间切换，以便快速到达我需要的设置分类。

#### Acceptance Criteria

1. THE Settings_View SHALL 在左侧呈现包含「外观与启动」「数据与备份」「安全与日志」「AI 助手」「通知提醒」「快捷键」六个分类的竖向导航 `Nav_SettingsCategories`，替代原 `TabControl`。
2. WHEN 使用者选择某个导航分类，THE Settings_View SHALL 在右侧内容区 `Pane_SettingsContent` 显示该分类对应的设置内容。
3. THE Settings_Navigation SHALL 使六个分类全部可达，即每个分类均可被选中并展示其内容。
4. WHEN 设置页加载且无既往分类选择，THE Settings_Navigation SHALL 默认选中「外观与启动」分类。

### Requirement 2: 设置项搜索

**User Story:** 作为使用者，我想要在设置页搜索设置项，以便无需逐个分类浏览即可定位目标设置。

#### Acceptance Criteria

1. WHEN 使用者在搜索框 `Box_SettingsSearch` 输入非空查询，THE Settings_Search SHALL 返回标题、描述或关键字命中的设置项，并按命中权重降序、同分按分类键稳定排序。
2. IF 查询为空或仅由空白字符组成，THEN THE Settings_Search SHALL 返回空结果列表。
3. WHEN 使用者激活某条搜索结果，THE Settings_View SHALL 切换到该结果所属分类、滚动定位到其锚点元素、并对该元素施加短暂高亮。
4. THE Settings_Search SHALL 保证每条返回结果的所属分类 `CategoryKey` 属于六大分类之一，且其锚点 `AnchorId` 非空。
5. WHEN 相同查询被多次执行，THE Settings_Search SHALL 返回顺序一致的相同结果。
6. THE Settings_Search SHALL 将返回结果数量上限限制为 12 条。
7. IF 搜索结果列表为空，THEN THE Settings_View SHALL 不执行任何分类切换、滚动定位或高亮动作。
8. WHEN 某次查询的命中条目数超过 12 条上限，THE Settings_View SHALL 显示一句简洁提示「结果较多，请输入更精确的关键词」。

### Requirement 3: 自动保存与离开页保存结果提示

**User Story:** 作为使用者，我想要设置即改即存并在离开设置页时获得一次保存结果反馈，以便确认我的改动是否成功落盘。

#### Acceptance Criteria

1. WHEN 使用者修改任一设置项，THE Settings_AutoSave SHALL 以防抖方式将改动自动持久化，保持即改即存语义不变。
2. WHEN 使用者尝试离开设置页（导航旧值为「设置」且新值不为「设置」）且本次停留期间发生过保存，THE Settings_View SHALL 先依据最近一次保存结果决定放行或阻止离开（见 3.3、3.8）。
3. WHEN 使用者尝试离开设置页且最近一次保存成功，THE Settings_View SHALL 放行导航离开，且 THE Toast_Service SHALL 显示成功提示「设置已保存」。
4. IF 使用者尝试离开设置页且最近一次保存失败，THEN THE Toast_Service SHALL 显示一条失败提示，其内容 SHALL 以面向普通用户的中文「人话」说明为主（如写入失败、校验未过、热键失败等场景的通俗说明），并将稳定错误码（`SET-1001`、`SET-1002`、`SET-1003` 或 `SET-1999` 之一）作为辅助信息附带呈现。
5. IF 本次停留设置页期间未发生任何保存，THEN THE Toast_Service SHALL 不弹出保存结果提示，且 THE Settings_View SHALL 正常放行导航离开。
6. WHEN 保存结果提示弹出后且导航已放行，THE Settings_AutoSave SHALL 清空已消费的 `LastSaveOutcome`，以避免重复提示。
7. IF 任一保存失败，THEN THE Settings_AutoSave SHALL 将该失败映射为 `SET-1001`/`SET-1002`/`SET-1003`/`SET-1999` 之一（持久化写入失败→`SET-1001`、输入校验未过→`SET-1002`、热键应用失败→`SET-1003`、其它未分类→`SET-1999`），且不向 UI 文案泄露内部异常细节。
8. IF 使用者尝试离开设置页且本次停留期间最近一次保存为失败，THEN THE Settings_View SHALL 阻止该次导航离开、使使用者保持停留在（或被自动拉回）设置页，以便其看到失败提示并处理；仅当最近一次保存成功或本次停留期间未发生保存时，导航离开 SHALL 被放行。

### Requirement 4: 稳定 QA 内容锚点

**User Story:** 作为 QA / 回归测试维护者，我想要设置页提供稳定的内容锚点，以便 smoke 校验不再依赖已被移除的保存按钮。

#### Acceptance Criteria

1. THE Settings_View SHALL 在右侧内容区根元素提供稳定锚点 AutomationId `Pane_SettingsContent`，替代已不存在的 `Btn_SavePreferences`。
2. THE Settings_View SHALL 为左侧导航提供稳定锚点 AutomationId `Nav_SettingsCategories`。
3. THE Settings_View SHALL 为搜索框提供稳定锚点 AutomationId `Box_SettingsSearch`。
4. THE QA_Smoke_Script SHALL 将设置页内容锚点引用由 `Btn_SavePreferences` 更新为 `Pane_SettingsContent`。

### Requirement 5: 我的页新布局

**User Story:** 作为使用者，我想要我的页采用「顶部身份头图 + 功能卡片堆叠」布局，以便信息层次清晰、操作集中。

#### Acceptance Criteria

1. THE MeProfile_View SHALL 在顶部呈现身份头图，包含头像、显示名、角色徽章以及锁定与登出快捷操作。
2. THE MeProfile_View SHALL 在身份头图下方以单列纵向堆叠方式呈现功能卡片。
3. WHERE 当前账号为 Owner，THE MeProfile_View SHALL 显示成员管理卡。
4. THE MeProfile_View SHALL 根据当前账号角色显示角色徽章文案：Owner 显示「系统管理员 Owner」，否则显示「系统店员 Member」。
5. IF 角色判定逻辑失败，THEN THE MeProfile_View SHALL 回退显示「系统店员 Member」徽章文案。

### Requirement 6: 本地头像上传（方案 A）

**User Story:** 作为使用者，我想要从本地上传头像，以便个性化展示我的身份。

#### Acceptance Criteria

1. WHEN 使用者选择一张图片更换头像，THE Avatar_Storage_Service SHALL 校验图片格式（仅接受 JPG、PNG、WebP）、尺寸与文件大小（单张不超过 5MB），对图片解码再编码以剥离 EXIF 元数据，并缩放为方形缩略图。
2. WHEN 头像通过校验与处理，THE Avatar_Storage_Service SHALL 将文件写入 app 数据目录下的 `avatars/` 子目录，并对目录与文件施加 `HardenFile` 加固。
3. WHEN 头像保存成功，THE MeProfile_ViewModel SHALL 将相对引用键持久化至 `AppPreferences.AvatarReference`，并立即刷新头像显示。
4. WHEN 应用重启后存在已保存的 `AvatarReference`，THE MeProfile_View SHALL 加载并显示该头像。
5. WHERE 当前账号无头像引用，THE MeProfile_View SHALL 显示默认占位头像，其形式为「渐变底色 + 用户名首字（中文取首个汉字、拉丁文取首字母）」。
6. IF 所选图片格式不受支持、无效或超过 5MB 大小上限，THEN THE Avatar_Storage_Service SHALL 拒绝该图片、就地提示「图片无效或过大」并保留原头像。
7. THE Avatar_Storage_Service SHALL 仅以相对引用键存储头像位置，且解析得到的路径必须位于 app 数据目录受保护子目录内，拒绝越界绝对路径。

### Requirement 7: 成员管理增强与权限边界

**User Story:** 作为 Owner，我想要在受清晰权限边界约束下更高效地管理成员，以便快速筛选、识别状态并安全地执行创建、停用与删除操作。

#### Acceptance Criteria

1. WHEN 使用者在成员搜索框输入查询，THE Member_Management SHALL 按查询过滤成员列表。
2. THE Member_Management SHALL 基于 `LocalAccountSummary.IsEnabled` 为每个成员显示启用或禁用状态徽章，且该徽章 SHALL 始终与账号真实 `IsEnabled` 状态保持一致（启用显示启用徽章、禁用显示禁用徽章）。
3. WHILE 过滤后的成员列表为空，THE Member_Management SHALL 显示空状态提示。
4. THE Member_Management SHALL 提供创建成员、重置密码、重置 PIN、停用成员与删除成员操作。
5. WHERE 当前账号角色为 `Owner`，THE Member_Management SHALL 显示创建成员入口；WHERE 当前账号角色为 `Member`，THE Member_Management SHALL 不显示创建成员入口。
6. IF 当前账号角色不是 `Owner`，THEN THE Member_Management SHALL 拒绝执行删除成员操作。
7. IF 当前账号角色不是 `Owner` 且操作目标不是自身，THEN THE Member_Management SHALL 拒绝执行停用成员操作。
8. WHERE 当前账号角色为 `Member`，THE Member_Management SHALL 允许该成员停用自身，且 SHALL 拒绝该成员删除自身。
9. THE Member_Management SHALL 允许 `Owner` 停用自身，且 SHALL 拒绝 `Owner` 删除自身。
10. THE Member_Management SHALL 将「删除成员」与「停用成员」作为彼此区分的两种独立能力：删除 SHALL 移除该成员账号，停用 SHALL 仅将其 `IsEnabled` 置为禁用而保留账号。

### Requirement 8: 凭证修改（主密码 + PIN）

**User Story:** 作为使用者，我想要在修改主密码与 PIN 时获得实时校验与强度反馈，以便设置足够强且正确的凭证。

#### Acceptance Criteria

1. WHEN 使用者编辑主密码修改表单字段，THE Credential_Form SHALL 实时校验「当前密码已提供」「新密码长度 >= 8」「两次输入一致」，并实时显示对应中文提示文案。
2. THE Credential_Form SHALL 仅当当前密码非空、新密码长度 >= 8、两次输入一致时，允许提交主密码修改（`CanSubmit` 为真）；新密码强度不作为提交的硬性门槛。
3. WHEN 使用者编辑 PIN 修改表单字段，THE Credential_Form SHALL 实时校验「当前 PIN 已提供」「新 PIN 为恰好 6 位纯数字」「两次输入一致」。
4. THE Credential_Form SHALL 仅当当前 PIN 非空、新 PIN 为恰好 6 位纯数字、两次输入一致时，允许提交 PIN 修改（`CanSubmit` 为真）。
5. THE Credential_Form SHALL 提供新密码强度计，将强度映射为 `Empty`/`Weak`/`Fair`/`Good`/`Strong` 五档。
6. THE Credential_Form SHALL 为凭证输入提供密码显隐切换。
7. WHEN 主密码或 PIN 修改命令完成，THE Credential_Form SHALL 在卡片内就地反馈结果，而不使用离开页 Toast。
8. WHEN 凭证相关命令完成（无论成功或失败），THE Credential_Form SHALL 清空相关凭证输入框。
9. WHEN 在已有密码后追加字符，IF 追加内容构成重复或可预测的弱模式，THEN THE Password_Strength_Evaluator MAY 返回更低强度档；否则在字符类别集合不减少时 SHALL 返回不低于原密码的强度档。
10. IF 新密码长度 >= 8 但强度低于 `Fair`，THEN THE Credential_Form SHALL 显示强度偏弱的中文警告提示，且 SHALL 不因此阻止提交（提交门槛仍以 8.2 为准）。

### Requirement 9: 账户安全 / 登录记录卡

**User Story:** 作为使用者，我想要查看真实的登录与安全记录，以便了解账户的安全状况。

#### Acceptance Criteria

1. WHEN 使用者查看账户安全卡且存在审计记录，THE Security_Audit_Card SHALL 展示真实的安全审计历史（登录成功、登录失败、账户锁定、凭证变更）以及最近登录时间。
2. THE Security_Audit_Card SHALL 通过 `ISecurityAuditService` 读取 API 获取审计记录。
3. IF 查询无任何审计记录，THEN THE Security_Audit_Card SHALL 显示空状态文案，且不臆造数据。
4. IF 审计读取失败，THEN THE Security_Audit_Card SHALL 显示「安全记录读取失败」、不渲染半截列表、且不泄露异常细节。
5. THE Security_Audit_Card SHALL 将 `LocalAccount.LastLoginAt` 直接展示为最近登录时间。
6. THE Security_Audit_Service SHALL 完整保留全部安全审计记录，不对历史记录做截断或自动清除。
7. THE Security_Audit_Card SHALL 支持按日期范围检索/筛选审计记录，经 `ISecurityAuditService` 读取 API 的时间范围参数查询并展示落在所选范围内的记录。

### Requirement 10: 代码结构与 MVVM 重构

**User Story:** 作为开发者，我想要清晰的 ViewModel 分层与 MVVM 纯净度，以便两个页面更易维护与扩展。

#### Acceptance Criteria

1. THE Orderly_App SHALL 抽取独立的 `MeProfileViewModel`，承载我的页的状态与命令。
2. THE Orderly_App SHALL 将全部七个 `Settings*` 分部类的状态与逻辑完整迁移至独立的 `SettingsViewModel`。
3. THE Orderly_App SHALL 以 MVVM 附加行为 `PasswordBoxBinder` 替换 `PasswordBox` 的 code-behind 双向同步逻辑。
4. WHEN ViewModel 中绑定的密码字符串被置空，THE PasswordBoxBinder SHALL 清空对应 `PasswordBox` 控件内容。
5. THE MainWindow SHALL 统一以 `SelectedSection` 控制 `MeProfileView` 的可见性，并移除其根元素上的内联可见性绑定。
6. WHEN `SelectedSection` 指示我的页处于激活，THE MainWindow SHALL 确保 `MeProfileView` 可见。
7. THE Orderly_App SHALL 将 `Popup_CopyToast` 泛化为通用 `IToastService`，复制提示与设置保存提示统一经其呈现。

### Requirement 11: 头像持久化后端（BC-1..BC-4）

**User Story:** 作为开发者，我想要头像引用以最小作用域持久化，以便头像在重启后可恢复且不污染加密库。

#### Acceptance Criteria

1. THE AppPreferences SHALL 新增可空字段 `AvatarReference`，默认 `null` 表示使用默认头像，且向后兼容。
2. THE AppSettingKeys SHALL 新增与现有键风格一致的 `AvatarReference` 键。
3. THE AppSetting_Repository SHALL 经现有 KV upsert 通道读写 `AvatarReference`，不引入 schema 迁移。
4. THE Orderly_App SHALL 提供 `IAvatarStorageService` 接口及实现，支持 `SaveAvatarAsync`、`ResolveAvatarPath` 与 `RemoveAvatarAsync`。

### Requirement 12: 安全审计后端（BC-6）

**User Story:** 作为安全负责人，我想要认证与账户敏感操作被防篡改地审计，以便我的页能展示真实的安全历史且不泄露明文凭证。

#### Acceptance Criteria

1. THE Orderly_App SHALL 定义安全审计事件类型集 `SecurityAuditEventKind`（`LoginSucceeded`、`LoginFailed`、`AccountLockedOut`、`CredentialChanged`、`MemberCreated`、`MemberPasswordReset`、`MemberDisabled`、`MemberDeleted`），独立于业务 `ActivityLog.ActivityType`。
2. WHEN 发生登录成功、登录失败、账户锁定、凭证变更或成员创建/重置/停用/删除之一，THE Security_Audit_Service SHALL 在认证 / 账户服务层经防篡改写入接缝恰好记录一条对应类型的审计记录。
3. THE Security_Audit_Service SHALL 将审计记录写入加密本地存储（SQLCipher），并以追加式加完整性校验保持防篡改特性。
4. THE Security_Audit_Service SHALL 在审计记录中仅保存事件类型、时间、账号标签与脱敏 detail，绝不记录明文凭证（密码 / PIN 原文）。
5. THE Security_Audit_Service SHALL 提供 `QueryAsync` 读取 API，按账号或时间范围返回顺序稳定的 `SecurityAuditEntry` 列表。

### Requirement 13: UI 改动边界（约束）

**User Story:** 作为维护者，我想要本期 UI 改动严格收敛在两个目标页面，以便不影响其它已稳定的页面。

#### Acceptance Criteria

1. THE Orderly_App SHALL 将本期 UI 改动严格限定在设置页与我的页，及与之直接相关的共享样式、壳层 Toast 与 QA 锚点。
2. THE Orderly_App SHALL 保持登录页、订单履约、异常处理、工作台、订单、商品、库存、客户、现金流与经营建议等其它页面的 UI 不变；其中登录页 SHALL 在本期发布周期内保持完全不变，即使为追求视觉一致性也不对其进行任何 UI 调整。
3. WHERE 本期新增敏感页面 PIN 门禁（Requirement 18），THE Orderly_App SHALL 仅以进入前的 PIN 验证遮罩/拦截这一访问控制层叠加于现金流等敏感页面之上，且 SHALL 不改动该敏感页面进入后自身的既有 UI 结构与布局。

### Requirement 14: P0 安全底线（约束）

**User Story:** 作为安全负责人，我想要本期改动维持既有 P0 安全保障，以便不引入凭证泄露或加密回退风险。

#### Acceptance Criteria

1. THE Orderly_App SHALL 维持敏感本地数据的加密存储（SQLCipher），且安全审计记录必须存入加密存储。
2. THE Orderly_App SHALL 保持安全审计记录的防篡改特性（追加式 + 完整性校验）。
3. THE Orderly_App SHALL 在日志、诊断与持久化中均不出现明文凭证（密码 / PIN 原文）。
4. WHEN 凭证相关命令完成（无论成功或失败），THE Orderly_App SHALL 清空相应凭证输入。
5. THE Orderly_App SHALL 将明文凭证在内存中的存在时间限定在命令执行所需期间，不予延长。

### Requirement 15: 统一设计系统（约束）

**User Story:** 作为使用者，我想要两个页面共享同一套现代设计语言，以便视觉体验一致且主题切换无异常。

#### Acceptance Criteria

1. THE Settings_View 与 MeProfile_View SHALL 同时一致应用同一套统一设计系统（柔和阴影 + 大圆角 + 充裕留白），即两页均须采用该设计系统，不允许仅其中一页应用。
2. WHEN 使用者切换深色或浅色主题，THE Settings_View 与 MeProfile_View SHALL 正确渲染且不出现视觉异常。

### Requirement 16: 凭证修改后的会话处理

**User Story:** 作为使用者，我想要在修改主密码或 PIN 成功后，会话以与变更凭证相匹配的方式被重新验证，以便新凭证立即生效且不留下使用旧凭证的会话。

#### Acceptance Criteria

1. WHEN 主密码修改命令成功完成，THE Session_Service SHALL 强制登出当前会话并要求使用者用新主密码重新登录。
2. WHEN PIN 修改命令成功完成，THE Session_Service SHALL 将当前会话锁定进入 `Pending_Pin_Unlock` 待 PIN 解锁状态（复用既有手动锁定机制），要求使用者用新 PIN 重新解锁，且 SHALL 不强制登出。
3. IF 主密码或 PIN 修改命令未成功（失败或取消），THEN THE Session_Service SHALL 保持当前会话状态不变，既不登出也不锁定。
4. WHEN 因主密码修改而强制登出或因 PIN 修改而锁定会话，THE Orderly_App SHALL 经 `ISecurityAuditService` 记录对应的 `CredentialChanged` 审计事件，且 SHALL 不在该记录或会话切换过程中泄露明文凭证。

### Requirement 17: Owner 紧急启用与受限权限模式

**User Story:** 作为处于被停用状态的 Owner，我想要凭 PIN 紧急启用以执行核心紧急操作，同时被限制查看机密数据，以便在不破坏机密数据保护的前提下应对紧急情况。

#### Acceptance Criteria

1. WHILE 当前 Owner 账号处于被停用状态，WHEN 该 Owner 提供正确的 6 位 PIN 发起紧急启用，THE Orderly_App SHALL 授予其进入 `Restricted_Permission_Mode` 受限权限模式。
2. IF 紧急启用时提供的 PIN 不正确，THEN THE Orderly_App SHALL 拒绝紧急启用并给出中文错误提示，且 SHALL 不进入受限权限模式。
3. WHILE 处于 `Restricted_Permission_Mode`，THE Orderly_App SHALL 允许该 Owner 执行核心紧急操作（如数据备份）。
4. WHILE 处于 `Restricted_Permission_Mode`，THE Orderly_App SHALL 拒绝该 Owner 查看现金流等隐私/机密数据，与 Requirement 18 的机密数据保护口径保持一致。
5. WHEN 紧急启用成功或失败，THE Orderly_App SHALL 经 `ISecurityAuditService` 记录对应的安全审计事件，且 SHALL 不泄露明文 PIN。

### Requirement 18: 敏感页面 PIN 门禁访问控制

**User Story:** 作为使用者，我想要现金流等账务核心机密/敏感页面在进入前要求 PIN 验证，以便机密数据不被未经验证的访问者查看。

#### Acceptance Criteria

1. WHEN 使用者尝试进入现金流页面或其它账务相关的核心机密/敏感页面，THE Sensitive_Page_Guard SHALL 先要求其通过 6 位 PIN 验证，验证通过后方可进入并查看该页面内容。
2. IF 使用者提供的 PIN 不正确，THEN THE Sensitive_Page_Guard SHALL 拒绝进入该敏感页面并给出中文错误提示。
3. WHILE 使用者尚未通过 PIN 验证，THE Sensitive_Page_Guard SHALL 不显示该敏感页面的机密内容。
4. WHILE 当前会话处于 `Restricted_Permission_Mode`（Requirement 17），THE Sensitive_Page_Guard SHALL 拒绝访问现金流等机密数据，与受限权限模式的机密数据保护口径保持一致。
5. THE Sensitive_Page_Guard SHALL 在 PIN 验证过程中不泄露明文 PIN，且 SHALL 不延长明文 PIN 在内存中的存在时间，超出验证所需即予清除。
