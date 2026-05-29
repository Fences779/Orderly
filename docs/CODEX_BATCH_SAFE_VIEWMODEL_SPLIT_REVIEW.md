# CODEX Batch Safe ViewModel Split Review

## Scope

- Batch date: 2026-05-30
- Repository: `D:\Dev\Orderly-SN`
- Batch goal: mechanically split approved `MainViewModel` presentation/coordinator partials to `<= 300` lines without behavior change
- Hard exclusions respected: XAML, `Views/**`, `*.xaml.cs`, `MainViewModel.StringNarrationOrders.cs`, `MainViewModel.ExceptionOrders.cs`, `MainViewModel.SettingsP0.cs`, login page files, `StringNarrationGateway*`, `CloudInventoryWorkspaceService.cs`, `DatabaseInitializer.cs`, cloud functions, backend/API/schema/protocol changes, inventory cloud-sync behavior, payment/fulfillment closed-loop behavior, mini-program compatibility behavior

## Candidate inspection and decisions

| Candidate | Initial lines | Decision | Reason |
| --- | ---: | --- | --- |
| `src/Orderly.App/ViewModels/MainViewModel.cs` | 651 | selected-safe | core constants, dependency fields, constructors, collection/state members, backup/restore state, runtime state, and empty fallback services could be relocated as whole members only |
| `src/Orderly.App/ViewModels/MainViewModel.BusinessPages.cs` | 464 | selected-safe | inventory and cashflow members were already separated enough to split into shared entry/helpers plus inventory/cashflow groups without touching contracts or visual behavior |
| `src/Orderly.App/ViewModels/MainViewModel.SettingsP1.cs` | 446 | selected-safe | inspected references from `MainViewModel.SettingsP0.cs` show this file only owns P1 AI/hotkey/notification extension settings; no login/security/SettingsP0 members were moved or edited |

No inspected candidate was deferred as protected in this batch.

## Subagent allocation

| Subagent | Target family |
| --- | --- |
| `Kant` | `MainViewModel.cs` core state family |
| `Beauvoir` | `MainViewModel.BusinessPages*` |
| `Meitner` | `MainViewModel.SettingsP1*` |

No two subagents were assigned the same existing file family.

## Files changed and exact final line counts

| Path | Final lines |
| --- | ---: |
| `src/Orderly.App/ViewModels/MainViewModel.cs` | 200 |
| `src/Orderly.App/ViewModels/MainViewModel.Collections.cs` | 32 |
| `src/Orderly.App/ViewModels/MainViewModel.CoreState.cs` | 105 |
| `src/Orderly.App/ViewModels/MainViewModel.BackupRestoreState.cs` | 61 |
| `src/Orderly.App/ViewModels/MainViewModel.RuntimeState.cs` | 125 |
| `src/Orderly.App/ViewModels/MainViewModel.EmptyServices.cs` | 156 |
| `src/Orderly.App/ViewModels/MainViewModel.BusinessPages.cs` | 47 |
| `src/Orderly.App/ViewModels/MainViewModel.BusinessPages.Inventory.cs` | 233 |
| `src/Orderly.App/ViewModels/MainViewModel.BusinessPages.Cashflow.cs` | 196 |
| `src/Orderly.App/ViewModels/MainViewModel.SettingsP1.cs` | 141 |
| `src/Orderly.App/ViewModels/MainViewModel.SettingsP1.Ai.cs` | 120 |
| `src/Orderly.App/ViewModels/MainViewModel.SettingsP1.HotkeysNotifications.cs` | 198 |

## Mechanical split summary

### `MainViewModel.cs` family

- Retained in main file: section constants, supported/legacy section sets, dependency fields, both constructors, `NormalizeSection`
- Moved intact into `MainViewModel.Collections.cs`: `ObservableCollection` members, events, `DatabasePath`, `_all*` caches
- Moved intact into `MainViewModel.CoreState.cs`: selection/search/navigation/OCR state plus `Preferences`
- Moved intact into `MainViewModel.BackupRestoreState.cs`: backup/restore state and all restore preview derived read-only properties
- Moved intact into `MainViewModel.RuntimeState.cs`: `StatusMessage`, `ConversationMessageInput`, `IsLoading`, `IsSaving`, `IsGeneratingAiSuggestion`, sync/detail flags
- Moved intact into `MainViewModel.EmptyServices.cs`: all nested `Empty*Service` fallback implementations

### `MainViewModel.BusinessPages*`

- Retained in `MainViewModel.BusinessPages.cs`: section-load entry, busy guard, shared formatting helpers
- Moved intact into `MainViewModel.BusinessPages.Inventory.cs`: inventory collections, observable state, refresh command, mapping helpers, dashboard fallback/projection helpers
- Moved intact into `MainViewModel.BusinessPages.Cashflow.cs`: cashflow collection, observable state, refresh command, dashboard fallback/projection helpers

### `MainViewModel.SettingsP1*`

- Retained in `MainViewModel.SettingsP1.cs`: P1 validation and preference mapping/load-back methods
- Moved intact into `MainViewModel.SettingsP1.Ai.cs`: AI input properties, runtime status properties, AI configuration check command, AI runtime status refresh
- Moved intact into `MainViewModel.SettingsP1.HotkeysNotifications.cs`: runtime hook delegates, hotkey/notification inputs, status properties, notification test command, hotkey runtime apply/rollback helpers, DND parsing and hotkey helper types

## Diff review conclusion

- `git diff --stat` was run after editing.
- Full diffs of the three modified original files were inspected.
- Result: the original files show whole-member removals only; no in-place method-body rewrite, string change, property rename, command rename, or flow rewrite was introduced.
- New files are same-namespace `partial class MainViewModel` files containing relocated complete members only.
- The only follow-up after build was one required using correction: `MainViewModel.SettingsP1.HotkeysNotifications.cs` added `using Orderly.Core.Services;` so the existing `HotkeyTextValidator` reference resolves in its new file. This did not change behavior.

## Compliance proof

- Every selected and changed `MainViewModel*.cs` file in this batch is `<= 300` lines.
- Constructor signatures, injected dependencies, public/internal API surface, commands, properties, fields, events, and string literals were preserved.
- Bindings, command names, navigation behavior, loading behavior, observable notifications, inventory protocol calls, and protected transaction flows were not changed.

## Build and QA results

- `dotnet build Orderly.sln -c Debug`: PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-qa-data-status.ps1`: PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1`: PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-2-pipeline-smoke.ps1`: PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-5-search-smoke.ps1`: PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-6-navigation-smoke.ps1`: PASS
- Local preview evidence: `run-p1-smoke.ps1` launched `src/Orderly.App/bin/Debug/net8.0-windows/Orderly.App.exe --qa-mode`, completed UIA smoke PASS, and restored QA baseline

## Unchanged protected areas confirmation

- XAML and code-behind untouched
- login page untouched
- settings P0/login/security flow untouched
- fulfillment/order protected files untouched
- exception protected files untouched
- backend contracts, request/response protocol, schema/migrations untouched
- inventory cloud-sync behavior/protocol untouched
- payment/fulfillment/shipping closed-loop behavior untouched
- mini-program compatibility behavior untouched

## Remaining deferred UI/ViewModel line-budget violations

| Path | Lines | Reason |
| --- | ---: | --- |
| `src/Orderly.App/ViewModels/MainViewModel.BackupCommands.cs` | 350 | UI/ViewModel file still over 300; not in this approved target batch |
| `src/Orderly.App/ViewModels/MainViewModel.InventoryManagement.cs` | 350 | UI/ViewModel file still over 300; inventory-management family not selected in this batch |
| `src/Orderly.App/ViewModels/MainViewModel.ExceptionOrders.cs` | 652 | protected exception flow |
| `src/Orderly.App/ViewModels/MainViewModel.SettingsP0.cs` | 1183 | protected settings P0 flow |
| `src/Orderly.App/ViewModels/MainViewModel.StringNarrationOrders.cs` | 1612 | protected fulfillment/order flow |
| `src/Orderly.App/ViewModels/LoginViewModel.cs` | 812 | login page protected |
| `src/Orderly.App/Views/MainWindow.xaml` | 7975 | XAML forbidden in this batch |
| `src/Orderly.App/Views/Resources/MainWindowResources.xaml` | 1768 | XAML forbidden in this batch |
| `src/Orderly.App/Views/LoginView.xaml` | 1645 | login XAML protected |
| `src/Orderly.App/Views/MainWindow.xaml.cs` | 777 | code-behind forbidden |
| `src/Orderly.App/Views/LoginView.xaml.cs` | 915 | login code-behind protected |
| `src/Orderly.App/App.xaml.cs` | 627 | app startup/UI source; out of scope |
| `src/Orderly.App/App.xaml` | 330 | XAML forbidden in this batch |

## Read-only review readiness

- This batch is ready for Kiro Opus 4.8 read-only review.
- Reason: selected target files meet the 300-line limit, full build and required QA passed, and diff review stayed within structural-only relocation plus required using/partial declarations.
