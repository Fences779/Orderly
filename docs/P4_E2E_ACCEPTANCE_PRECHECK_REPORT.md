# P4 End-to-End Acceptance Precheck Report

## 1. Findings

### 关键结论
- 本地账号安全主链路已基本落地：首启 Owner、冷启动主密码登录、PIN 热解锁、Owner/Member 管理、Recovery Key 重置、按账号库路径初始化。
- 当前更像“功能已接近齐备 + 验收证据不足”，不是“未实现”。
- 已执行构建和 backup/restore smoke，均通过；但本轮实测不覆盖登录/PIN/Owner-Only UI 的真实交互。

### 主要阻塞/不确定项
- `Member` 自助“修改自己的主密码与 PIN”未见独立入口或服务方法，现有仅 `Owner` 重置 `Member`（与设计清单第 814 条存在偏差）。
- 备份恢复脚本实测的是业务库链路；`launcher.db` 身份快照链路本轮仅静态审查，未执行端到端验证。
- 当前解决方案未纳入正式测试项目，`dotnet test` 不是可用验收证据主路径。

## 2. Acceptance Scenario Checklist

| Scenario | Current Status | Evidence | Confidence | Still Need Validation |
|---|---|---|---|---|
| first-run Owner creation | Likely implemented and testable now | 首启模式由账号计数决定；创建前强制“无账号”；创建账号角色写死 Owner。见 [LoginViewModel.cs:72](/D:/Dev/Orderly-SN/src/Orderly.App/ViewModels/LoginViewModel.cs:72)、[LocalAuthService.cs:42](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalAuthService.cs:42)、[LocalAuthService.cs:96](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalAuthService.cs:96) | 静态: 高 / 实测: 低 | 手工首启流程（空 launcher.db）+ 创建后重启回归 |
| Recovery Key generation and acknowledgement | Likely implemented and testable now | 创建 Owner 时生成 Recovery Key；未确认“已保存”不放行进入工作台。见 [LocalAuthService.cs:62](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalAuthService.cs:62)、[LoginViewModel.cs:172](/D:/Dev/Orderly-SN/src/Orderly.App/ViewModels/LoginViewModel.cs:172)、[LoginView.xaml:101](/D:/Dev/Orderly-SN/src/Orderly.App/Views/LoginView.xaml:101) | 静态: 高 / 实测: 低 | UI 手工验证“必须勾选确认才可继续” |
| cold-start login with username + master password | Likely implemented and testable now | 登录入口仅用户名+主密码；成功后才初始化业务工作区。见 [LoginView.xaml:31](/D:/Dev/Orderly-SN/src/Orderly.App/Views/LoginView.xaml:31)、[LocalAuthService.cs:123](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalAuthService.cs:123)、[App.xaml.cs:152](/D:/Dev/Orderly-SN/src/Orderly.App/App.xaml.cs:152) | 静态: 高 / 实测: 低 | 手工验证冷启动登录成功路径 |
| rejection of wrong master password | Likely implemented and testable now | 主密码哈希校验失败即拒绝，返回统一错误。见 [LocalAuthService.cs:142](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalAuthService.cs:142) | 静态: 高 / 实测: 低 | 手工验证错误主密码提示与不可进入工作台 |
| per-account database isolation | Implemented but risky / needs directed validation | 账号库路径按 `accountId` 分目录；登录后按 session.DatabasePath 初始化仓储。见 [DatabasePaths.cs:52](/D:/Dev/Orderly-SN/src/Orderly.Data/Sqlite/DatabasePaths.cs:52)、[LocalAuthService.cs:58](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalAuthService.cs:58)、[App.xaml.cs:152](/D:/Dev/Orderly-SN/src/Orderly.App/App.xaml.cs:152) | 静态: 中高 / 实测: 低 | 需要双账号交叉写读验证“切换后看不到对方数据” |
| Owner creates Member | Likely implemented and testable now | Owner-only 服务校验 + UI 命令路径。见 [LocalAccountManagementService.cs:37](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalAccountManagementService.cs:37)、[MainViewModel.AccountManagement.cs:106](/D:/Dev/Orderly-SN/src/Orderly.App/ViewModels/MainViewModel.AccountManagement.cs:106)、[MainWindow.xaml:1668](/D:/Dev/Orderly-SN/src/Orderly.App/Views/MainWindow.xaml:1668) | 静态: 高 / 实测: 低 | 手工验证 Owner 创建 Member 并可登录 |
| Member cannot create accounts | Implemented but risky / needs directed validation | 管理服务强制 `RequireOwnerSession`；非 Owner UI 不展示管理区。见 [LocalAccountManagementService.cs:229](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalAccountManagementService.cs:229)、[MainWindow.xaml:1627](/D:/Dev/Orderly-SN/src/Orderly.App/Views/MainWindow.xaml:1627)、[MainWindow.xaml:1701](/D:/Dev/Orderly-SN/src/Orderly.App/Views/MainWindow.xaml:1701) | 静态: 高 / 实测: 低 | 需要 Member 账号实登后验证 UI 与服务双重拦截 |
| Owner disables Member | Implemented but risky / needs directed validation | 禁用仅允许针对 Member；登录时检查 `IsEnabled` 阻止后续登录。见 [LocalAccountManagementService.cs:103](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalAccountManagementService.cs:103)、[LocalAuthService.cs:137](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalAuthService.cs:137) | 静态: 中高 / 实测: 低 | 需要“禁用前已登录/禁用后重登失败”双场景验证 |
| Member password reset | Partially implemented | 已实现 Owner 重置 Member 主密码；未见 Member 自助修改主密码入口/接口。见 [ILocalAccountManagementService.cs:10](/D:/Dev/Orderly-SN/src/Orderly.Core/Services/ILocalAccountManagementService.cs:10)、[LocalAccountManagementService.cs:123](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalAccountManagementService.cs:123)、[MainWindow.xaml:1674](/D:/Dev/Orderly-SN/src/Orderly.App/Views/MainWindow.xaml:1674) | 静态: 中 / 实测: 低 | 明确验收口径：是“Owner重置”还是“Member自助修改”；若要求自助，当前缺实现 |
| Member PIN reset | Partially implemented | 已实现 Owner 重置 Member PIN；未见 Member 自助改 PIN 能力。见 [ILocalAccountManagementService.cs:11](/D:/Dev/Orderly-SN/src/Orderly.Core/Services/ILocalAccountManagementService.cs:11)、[LocalAccountManagementService.cs:153](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalAccountManagementService.cs:153)、[MainWindow.xaml:1679](/D:/Dev/Orderly-SN/src/Orderly.App/Views/MainWindow.xaml:1679) | 静态: 中 / 实测: 低 | 同上，先确认验收口径 |
| Owner Recovery Key password reset | Likely implemented and testable now | 登录页与设置页均可触发；服务校验 RecoveryKeyHash 后重包 DataKey。见 [LoginView.xaml:41](/D:/Dev/Orderly-SN/src/Orderly.App/Views/LoginView.xaml:41)、[LoginViewModel.cs:184](/D:/Dev/Orderly-SN/src/Orderly.App/ViewModels/LoginViewModel.cs:184)、[LocalAccountManagementService.cs:175](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalAccountManagementService.cs:175) | 静态: 高 / 实测: 低 | 手工验证重置后旧主密码失效、新主密码可登录 |
| sleep/resume PIN unlock | Implemented but risky / needs directed validation | 系统 Resume 触发 PendingPinUnlock；PIN 验证成功后解锁。见 [App.xaml.cs:422](/D:/Dev/Orderly-SN/src/Orderly.App/App.xaml.cs:422)、[SessionLockService.cs:21](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/SessionLockService.cs:21)、[App.xaml.cs:492](/D:/Dev/Orderly-SN/src/Orderly.App/App.xaml.cs:492) | 静态: 中高 / 实测: 低 | 真机睡眠恢复测试（含错误 PIN 重试、取消后退出登录） |
| manual lock | Likely implemented and testable now | 设置页按钮触发 `LockSessionCommand`，进入同一 PIN 解锁链路。见 [MainWindow.xaml:1611](/D:/Dev/Orderly-SN/src/Orderly.App/Views/MainWindow.xaml:1611)、[MainViewModel.CommandInfrastructure.cs:162](/D:/Dev/Orderly-SN/src/Orderly.App/ViewModels/MainViewModel.CommandInfrastructure.cs:162)、[App.xaml.cs:564](/D:/Dev/Orderly-SN/src/Orderly.App/App.xaml.cs:564) | 静态: 高 / 实测: 低 | 手工验证锁定后 UI 不可操作、PIN 正确后恢复 |
| logout back to login | Likely implemented and testable now | 退出触发会话清理、DataKey 清零、工作区销毁并回登录页。见 [App.xaml.cs:517](/D:/Dev/Orderly-SN/src/Orderly.App/App.xaml.cs:517)、[SessionContextService.cs:35](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/SessionContextService.cs:35) | 静态: 高 / 实测: 低 | 手工验证退出后必须重新主密码登录 |
| encrypted sensitive fields still readable/editable/searchable after login | Implemented but risky / needs directed validation | 写入时存密文字段，读取时优先解密；搜索服务基于仓储返回的解密模型做匹配。见 [CustomerRepository.cs:214](/D:/Dev/Orderly-SN/src/Orderly.Data/Repositories/CustomerRepository.cs:214)、[CustomerRepository.cs:170](/D:/Dev/Orderly-SN/src/Orderly.Data/Repositories/CustomerRepository.cs:170)、[LocalGlobalSearchService.cs:63](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalGlobalSearchService.cs:63) | 静态: 中高 / 实测: 低 | 需要实库回归：新增/编辑后重启再搜、跨实体搜命中 |
| backup/restore consistency for launcher identity data plus account business data | Partially implemented | 代码已支持附加 `LocalAccountsSnapshot` 与恢复校验；但脚本实测链路未注入 launcher 工厂，未覆盖身份快照恢复。见 [LocalBackupService.cs:461](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalBackupService.cs:461)、[LocalBackupService.cs:942](/D:/Dev/Orderly-SN/src/Orderly.Data/Services/LocalBackupService.cs:942)、[tools/qa/run-p2-7-backup-smoke.ps1](/D:/Dev/Orderly-SN/tools/qa/run-p2-7-backup-smoke.ps1) | 静态: 中 / 实测: 中(仅业务库) | 增加“带 launcher + 会话上下文”的恢复验证，至少覆盖当前账号快照一致性 |

## 3. Scenarios That Are Likely Ready

- first-run Owner creation  
- Recovery Key generation and acknowledgement  
- cold-start login with username + master password  
- rejection of wrong master password  
- Owner creates Member  
- Owner Recovery Key password reset  
- manual lock  
- logout back to login  

## 4. Scenarios That Need Focused Validation

- per-account database isolation  
- Member cannot create accounts  
- Owner disables Member  
- sleep/resume PIN unlock  
- encrypted sensitive fields readable/editable/searchable after login  

## 5. Scenarios Blocked By Code Or Review Uncertainty

- Member password reset（若验收定义为“Member 自助修改主密码”）  
- Member PIN reset（若验收定义为“Member 自助修改 PIN”）  
- backup/restore consistency for launcher identity + account business data（当前仅业务库链路有实测）  

## 6. Verification / Commands Run

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-7-backup-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-8-restore-smoke.ps1
dotnet sln Orderly.sln list
git status --short
```

结果摘要：
- `dotnet build`：成功，0 warning / 0 error。
- `run-p2-7-backup-smoke.ps1`：成功。
- `run-p2-8-restore-smoke.ps1`：成功。
- `dotnet sln list`：仅 App/Core/Data/Infrastructure，未包含正式 tests project。
- 当前工作区已有大量既存未提交改动，本次未做业务代码改动，仅新增本报告。

## 7. Recommended Manual / Scripted Validation Order

1. 空身份库首启：创建 Owner -> 必须确认 Recovery Key -> 进入工作台。  
2. 冷启动登录：正确主密码成功，错误主密码失败。  
3. Owner/Member：Owner 创建 Member；Member 登录后验证无法创建账号。  
4. 禁用链路：Owner 禁用 Member；Member 重新冷启动登录必须被拒绝。  
5. 凭证链路：先验证 Owner 重置 Member 主密码/PIN；再确认是否需要补“Member 自助修改”。  
6. 会话锁链路：手动锁定 + 睡眠恢复，验证 PIN 错误重试与退出登录分支。  
7. 数据隔离：两个账号分别写入客户/订单后互相不可见。  
8. 加密可用性：登录后对敏感字段做增删改查+搜索，并重启回归。  
9. 备份恢复：补一条“包含 launcher 快照”的实测脚本，验证身份库与账号库匹配恢复。  
