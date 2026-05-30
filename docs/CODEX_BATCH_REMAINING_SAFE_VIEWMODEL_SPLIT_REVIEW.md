# CODEX Batch Remaining Safe ViewModel Split Review

## Scope

- Batch date: 2026-05-30
- Repository: `D:\Dev\Orderly-SN`
- Batch goal: perform one focused structural-only split pass for `MainViewModel.BackupCommands.cs`, `MainViewModel.InventoryManagement.cs`, and the remaining non-frozen over-budget file `MainViewModel.AccountManagement.cs`
- Hard exclusions respected: no XAML, no `Views/**`, no `*.xaml.cs`, no `MainViewModel.cs`, no previously frozen compliant families, no `MainViewModel.StringNarrationOrders.cs`, no `MainViewModel.ExceptionOrders.cs`, no `MainViewModel.SettingsP0.cs`, no `LoginViewModel.cs`, no `StringNarrationGateway*`, no `CloudInventoryWorkspaceService.cs`, no `DatabaseInitializer.cs`, no cloud functions, no backend/API/schema/protocol changes, no payment/fulfillment/shipping closed-loop changes, no mini-program compatibility changes

## Selected targets and original line counts

| Target | Original lines |
| --- | ---: |
| `src/Orderly.App/ViewModels/MainViewModel.BackupCommands.cs` | 405 |
| `src/Orderly.App/ViewModels/MainViewModel.InventoryManagement.cs` | 398 |
| `src/Orderly.App/ViewModels/MainViewModel.AccountManagement.cs` | 310 |

## Subagent allocation

| Subagent | Assignment |
| --- | --- |
| `Leibniz` | `MainViewModel.BackupCommands.cs` family |
| `Laplace` | `MainViewModel.InventoryManagement.cs` family |

`MainViewModel.AccountManagement.cs` was evaluated and split locally by the coordinator after confirming the workspace still contained only the expected current batch changes.

Both subagents and the coordinator used disjoint write scopes only.

## Destination files, moved member groups, and final line counts

### `MainViewModel.BackupCommands*`

| Path | Final lines | Member groups retained or moved |
| --- | ---: | --- |
| `src/Orderly.App/ViewModels/MainViewModel.BackupCommands.cs` | 174 | retained backup export/select/validate/restore commands, path-change callbacks, restore-preview callbacks, `CanManageBackup`, `CanValidateBackup`, `CanRestoreBackup` |
| `src/Orderly.App/ViewModels/MainViewModel.BackupCommands.Runtime.cs` | 182 | moved recent-backup load/refresh helpers, restore preview application/status helpers, restore result status helper, restore preview property-notification helper, default backup directory resolver |
| `src/Orderly.App/ViewModels/MainViewModel.BackupCommands.Helpers.cs` | 62 | moved `FormatBackupCounts`, `GetRestoreTargetCode`, `GetRestoreTargetLabel` |

### `MainViewModel.InventoryManagement*`

| Path | Final lines | Member groups retained or moved |
| --- | ---: | --- |
| `src/Orderly.App/ViewModels/MainViewModel.InventoryManagement.cs` | 200 | retained `InventoryPageItem`, inventory filter/view/page observable members, page collections/options, property-change callbacks, view/page/sort commands, `ApplyViewColumnVisibility` |
| `src/Orderly.App/ViewModels/MainViewModel.InventoryManagement.Filters.cs` | 158 | moved `_isApplyingInventoryFilters`, filter dropdown projection, `ApplyInventoryLocalFiltersAndPaging` |
| `src/Orderly.App/ViewModels/MainViewModel.InventoryManagement.Paging.cs` | 54 | moved `UpdatePageNumbers` |

### `MainViewModel.AccountManagement*`

| Path | Final lines | Member groups retained or moved |
| --- | ---: | --- |
| `src/Orderly.App/ViewModels/MainViewModel.AccountManagement.cs` | 261 | retained observable account-management inputs/state, `CanManageAccounts`, `CanOperateMember`, owner-change callbacks, managed-account load, refresh/create/disable/reset/change commands |
| `src/Orderly.App/ViewModels/MainViewModel.AccountManagement.CommandState.cs` | 54 | moved `CanCreateMember`, `CanOperateOnSelectedMember`, `CanResetSelectedMemberPassword`, `CanResetSelectedMemberPin`, `CanResetOwnerByRecoveryKey`, `CanChangeCurrentMasterPassword`, `CanChangeCurrentPin` |

## AccountManagement safety classification

- `MainViewModel.AccountManagement.cs` touches local account management, password/PIN handling, recovery-key reset flow, owner/member state, and binding-visible account-management commands/properties, so it is login/security-sensitive at the ViewModel layer.
- The file does not itself modify XAML, backend/API contracts, persistence schema, payment/fulfillment behavior, or inventory cloud-sync behavior.
- Safe-action decision: mechanical split allowed within this batch because the only change was relocation of complete existing command-state predicate members into a same-namespace `partial class MainViewModel` file with no behavior edits.

## Mechanical diff review conclusion

- Reviewed the diffs of all three original target files against `HEAD`.
- All three originals only show whole-member removals.
- New files are same-namespace `partial class MainViewModel` files containing relocated complete members only.
- No member was renamed, altered, duplicated, or dropped.
- No non-mechanical edits were required.

## Compliance confirmation

- Every modified or newly created file in all three selected families is `<= 300` lines.
- `MainViewModel.BackupCommands*` now satisfies the `<= 300` rule.
- `MainViewModel.InventoryManagement*` now satisfies the `<= 300` rule.
- `MainViewModel.AccountManagement*` now satisfies the `<= 300` rule.
- Binding-visible member names, command names, observable notifications, backup export/preview/restore behavior, inventory loading/filtering/paging behavior, account-management behavior, inventory cloud-sync protocol usage, backend contracts, and protected transaction flows remain unchanged.

## Build and QA results

- Pre-build `Orderly.App.exe` check: no running process found
- `dotnet build Orderly.sln -c Debug`: PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1`: PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-5-search-smoke.ps1`: FAIL
- Failure detail: two consecutive reruns failed at `tools\qa\run-p3-5-search-smoke.ps1:355` with `Expected search result type not found: ConversationMessage`
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-6-navigation-smoke.ps1`: PASS
- Local preview evidence: `run-p1-smoke.ps1` launched `src/Orderly.App/bin/Debug/net8.0-windows/Orderly.App.exe --qa-mode`, completed UIA smoke PASS, and restored the QA baseline

## Protected-area and behavior freeze confirmation

- XAML and UI visuals untouched
- `Views/**` and `*.xaml.cs` untouched
- login/authentication behavior, credential handling, recovery-key flow, and account service calls untouched
- backup service implementation and file format untouched
- inventory cloud-sync protocol, gateway options, request shape, service behavior, persistence behavior, and backend contracts untouched
- payment callback, order creation, automatic paid transition, fulfillment/shipping sync, and payment-to-fulfillment closed loop untouched
- mini-program compatibility behavior untouched

## Remaining deferred line-budget violations

| Path | Lines | Reason |
| --- | ---: | --- |
| `src/Orderly.App/ViewModels/MainViewModel.StringNarrationOrders.cs` | 1809 | protected fulfillment/order flow |
| `src/Orderly.App/ViewModels/MainViewModel.SettingsP0.cs` | 1358 | protected settings flow |
| `src/Orderly.App/Views/MainWindow.xaml` | 8302 | UI-specialist XAML work |
| `src/Orderly.App/Views/Resources/MainWindowResources.xaml` | 1854 | UI-specialist XAML resource work |
| `src/Orderly.App/Views/LoginView.xaml` | 1708 | protected login UI |
| `src/Orderly.App/Views/LoginView.xaml.cs` | 1063 | protected login UI/code-behind |
| `src/Orderly.App/ViewModels/LoginViewModel.cs` | 923 | protected login flow |
| `src/Orderly.App/Views/MainWindow.xaml.cs` | 913 | UI-specialist code-behind work |
| `src/Orderly.App/ViewModels/MainViewModel.ExceptionOrders.cs` | 750 | protected exception flow |
| `src/Orderly.App/App.xaml.cs` | 714 | UI/app-startup specialist work |
| `src/Orderly.App/App.xaml` | 344 | UI/app-startup specialist work |

## Read-only review readiness

- This batch is not yet ready for Kiro Opus 4.8 read-only review.
- Reason: all three selected families are now compliant and the diff stayed structural-only, but the required `run-p3-5-search-smoke.ps1` validation is currently red and must be triaged or restored to green first.
