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
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-6-navigation-smoke.ps1`: PASS
- Local preview evidence: `run-p1-smoke.ps1` launched `src/Orderly.App/bin/Debug/net8.0-windows/Orderly.App.exe --qa-mode`, completed UIA smoke PASS, and restored the QA baseline

### `run-p3-5-search-smoke.ps1` resolution (historical failure, now fixed)

- The earlier recorded `run-p3-5-search-smoke.ps1` failure (two consecutive reruns at `tools\qa\run-p3-5-search-smoke.ps1:355` with `Expected search result type not found: ConversationMessage`) was diagnosed as **pre-existing QA fixture leakage**, not a regression introduced by this ViewModel split batch.
- The issue was fixed in commit `7c3753b` (`fix(qa): clean up leaked p3.5 and p3.6 fixtures`).
- Post-fix validation results:
  - polluted QA DB cleanup verified
  - `run-p3-5-search-smoke.ps1` passed twice consecutively with stable common needle: `7`
  - canonical validation sequence passed
- This historical red state is therefore resolved and must not be treated as an active blocker.

## Protected-area and behavior freeze confirmation

- XAML and UI visuals untouched
- `Views/**` and `*.xaml.cs` untouched
- login/authentication behavior, credential handling, recovery-key flow, and account service calls untouched
- backup service implementation and file format untouched
- inventory cloud-sync protocol, gateway options, request shape, service behavior, persistence behavior, and backend contracts untouched
- payment callback, order creation, automatic paid transition, fulfillment/shipping sync, and payment-to-fulfillment closed loop untouched
- mini-program compatibility behavior untouched

## Corrected line-budget thresholds

The earlier single-threshold (`<= 300` for all files) interpretation was incorrect. The canonical project standard is dual-threshold:

- UI / XAML / view-related files: over budget only when `> 300` lines.
- Non-UI logic/source files (services, repositories, non-layout ViewModels, models): over budget only when `> 500` lines.

Under these corrected thresholds, files such as `LocalNavigationRouteService.cs` (341), `LocalAiAssistantService.cs` (392), and the QA/service partials below 500 lines are compliant and are not split candidates.

## Remaining over-budget work (corrected dual thresholds)

There is currently no safe over-budget non-UI logic split candidate outside frozen boundaries. Every genuinely over-budget item below is either deferred UI/XAML structural work or frozen/high-risk domain work requiring explicit future approval.

| Path | Lines | Threshold | Category | Reason |
| --- | ---: | ---: | --- | --- |
| `src/Orderly.App/Views/MainWindow.xaml` | 7975 | 300 | deferred UI/XAML | primary deferred UI structural target; requires explicit UI approval |
| `src/Orderly.App/Views/Resources/MainWindowResources.xaml` | 1768 | 300 | deferred UI/XAML | shared XAML resource dictionary; UI-specialist work |
| `src/Orderly.App/Views/MainWindow.xaml.cs` | 777 | 300 | deferred UI/XAML | main window code-behind; UI-specialist work |
| `src/Orderly.App/App.xaml.cs` | 627 | 300 | deferred UI/XAML | app-startup code-behind; UI/app-startup specialist work |
| `src/Orderly.App/App.xaml` | 330 | 300 | deferred UI/XAML | app-level XAML; UI/app-startup specialist work |
| `src/Orderly.Data/Services/StringNarrationGatewayOrderService.cs` | 1696 | 500 | frozen/high-risk | order/fulfillment gateway; payment-to-fulfillment loop and backend contracts frozen |
| `src/Orderly.App/ViewModels/MainViewModel.StringNarrationOrders.cs` | 1612 | 500 | frozen/high-risk | order fulfillment page behavior frozen |
| `src/Orderly.App/ViewModels/MainViewModel.SettingsP0.cs` | 1183 | 500 | frozen/high-risk | settings page logic frozen |
| `src/Orderly.Data/Services/StringNarrationGatewayBusinessService.cs` | 1086 | 500 | frozen/high-risk | fulfillment/business gateway; shipping-sync and transaction loop frozen |
| `src/Orderly.App/Views/LoginView.xaml` | 1645 | 300 | frozen/high-risk | login page UI frozen |
| `src/Orderly.App/Views/LoginView.xaml.cs` | 915 | 300 | frozen/high-risk | login page code-behind frozen |
| `src/Orderly.App/ViewModels/LoginViewModel.cs` | 812 | 500 | frozen/high-risk | login flow frozen |
| `src/Orderly.Data/Services/CloudInventoryWorkspaceService.cs` | 737 | 500 | frozen/high-risk | cloud-sync protocol frozen |
| `src/Orderly.App/ViewModels/MainViewModel.ExceptionOrders.cs` | 652 | 500 | frozen/high-risk | exception flow frozen |
| `src/Orderly.Data/Sqlite/DatabaseInitializer.cs` | 582 | 500 | frozen/high-risk | schema/migration/contract surface frozen |

## Read-only review readiness

- This batch is ready for read-only review.
- All three selected families are now compliant under the corrected dual thresholds and the diff stayed structural-only.
- The previously red `run-p3-5-search-smoke.ps1` validation was diagnosed as pre-existing QA fixture leakage (not a regression from this batch) and was fixed in commit `7c3753b`; it passed twice consecutively post-fix with stable common needle `7`, and the canonical validation sequence passed.
- Completed safe refactor boundary: there is no safe over-budget non-UI logic split candidate outside frozen boundaries, so logic-layer splitting is paused until a protected domain is deliberately unlocked or deferred UI/XAML work is taken up.
