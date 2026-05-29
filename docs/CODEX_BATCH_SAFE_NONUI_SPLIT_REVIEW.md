# CODEX Batch Safe Non-UI Split Review

## Scope

- Batch date: 2026-05-29
- Repository: `D:\Dev\Orderly-SN`
- Batch goal: mechanically split clearly safe oversized non-UI `.cs` files to `<= 500` lines without behavior changes
- Explicit non-goals in this batch: UI/XAML/code-behind, protected order/payment/fulfillment/inventory sync/backend contract/schema areas

## Initial oversized-file inventory

| Path | Initial lines | Category | Decision |
| --- | ---: | --- | --- |
| `src/Orderly.Data/Services/QaDataSeeder.cs` | 1345 | safe non-UI | split in this batch |
| `src/Orderly.Data/Services/DemoDataSeeder.cs` | 597 | safe non-UI | split in this batch |
| `src/Orderly.Data/Services/LocalWorkbenchTaskService.cs` | 677 | safe non-UI | split in this batch |
| `src/Orderly.Data/Services/LocalGlobalSearchService.cs` | 569 | safe non-UI | split in this batch |
| `src/Orderly.Data/Services/LocalAccountManagementService.cs` | 639 | safe non-UI | split in this batch |
| `src/Orderly.Data/Sqlite/DatabaseInitializer.cs` | 582 | protected backend-contract | defer; owns schema creation and migration-sensitive SQL |
| `src/Orderly.Data/Services/CloudInventoryWorkspaceService.cs` | 737 | protected backend-contract | defer; inventory cloud sync behavior explicitly protected |
| `src/Orderly.Data/Services/StringNarrationGatewayBusinessService.cs` | 1086 | protected backend-contract | defer; protected gateway/backend service |
| `src/Orderly.Data/Services/StringNarrationGatewayOrderService.cs` | 1696 | protected transaction | defer; protected order/payment/fulfillment-adjacent flow |
| `src/Orderly.Data/Services/LocalBackupService.Restore.cs` | 551 | already handled | defer; already completed `LocalBackupService*` family |
| `src/Orderly.Core/Models/StringNarrationProductionModels.cs` | 824 | already handled | defer; already completed `StringNarration*Models*` family |
| `src/Orderly.App/App.xaml.cs` | 627 | UI | defer; UI/app startup source |
| `src/Orderly.App/ViewModels/LoginViewModel.cs` | 812 | UI | defer; login page protected |
| `src/Orderly.App/ViewModels/MainViewModel.cs` | 651 | UI | defer; UI-related ViewModel, not approved in this batch |
| `src/Orderly.App/ViewModels/MainViewModel.ExceptionOrders.cs` | 652 | protected transaction | defer; exception page protected |
| `src/Orderly.App/ViewModels/MainViewModel.SettingsP0.cs` | 1183 | UI | defer; settings page protected |
| `src/Orderly.App/ViewModels/MainViewModel.StringNarrationOrders.cs` | 1612 | protected transaction | defer; fulfillment/order page protected |
| `src/Orderly.App/Views/LoginView.xaml.cs` | 915 | UI | defer; code-behind forbidden |
| `src/Orderly.App/Views/MainWindow.xaml.cs` | 777 | UI | defer; code-behind forbidden |
| `src/Orderly.App/App.xaml` | 330 | UI | inventory only; XAML forbidden |
| `src/Orderly.App/Views/LoginView.xaml` | 1645 | UI | inventory only; XAML forbidden |
| `src/Orderly.App/Views/MainWindow.xaml` | 7975 | UI | inventory only; XAML forbidden |
| `src/Orderly.App/Views/Resources/MainWindowResources.xaml` | 1768 | UI | inventory only; XAML forbidden |

## Selected targets

- `src/Orderly.Data/Services/QaDataSeeder.cs`
- `src/Orderly.Data/Services/DemoDataSeeder.cs`
- `src/Orderly.Data/Services/LocalWorkbenchTaskService.cs`
- `src/Orderly.Data/Services/LocalGlobalSearchService.cs`
- `src/Orderly.Data/Services/LocalAccountManagementService.cs`

All five matched the safe category rules:

- QA/data seeding utilities
- pure local helpers/services
- no protected backend contract/interface changes
- no UI/XAML/code-behind edits
- no business-logic rewrite required for the split

## Subagent allocation

| Subagent | Target family |
| --- | --- |
| `Rawls` | `QaDataSeeder*` |
| `Ptolemy` | `DemoDataSeeder*` |
| `Faraday` | `LocalWorkbenchTaskService*` |
| `Archimedes` | `LocalGlobalSearchService*` |
| `Aquinas` | `LocalAccountManagementService*` |

No two subagents edited the same existing file family.

## Per-target pre-split responsibilities

### `QaDataSeeder*`

- entry flow and QA argument checks
- entity upsert methods for customer/deal/order/followUp/note/priceAdjustment/activityLog/conversationMessage/aiSuggestion/ocrResult/syncRecord
- parameter-binding helpers
- nested result/record types
- static QA seed arrays

### `DemoDataSeeder*`

- demo-mode request detection
- minimum-count orchestration
- insert persistence helpers
- nested record types
- static demo seed arrays

### `LocalWorkbenchTaskService*`

- workbench task loading entrypoints
- finalize/filter/sort helpers
- follow-up/draft/reply-needed/AI/OCR/recently-active task builders
- comparer and supporting record structs

### `LocalGlobalSearchService*`

- search entrypoint and repository fan-in
- entity projector methods
- summary/action helper methods
- query normalization, scoring, comparer helpers

### `LocalAccountManagementService*`

- account listing/create/disable/delete flows
- owner/member credential verification
- current password/PIN changes
- owner recovery and member reset flows
- session/account lookup, summary mapping, hash/wrap/unwrap helpers

## Files changed and exact final line counts

| Path | Final lines |
| --- | ---: |
| `src/Orderly.Data/Services/DemoDataSeeder.cs` | 291 |
| `src/Orderly.Data/Services/DemoDataSeeder.Data.cs` | 106 |
| `src/Orderly.Data/Services/DemoDataSeeder.Persistence.cs` | 211 |
| `src/Orderly.Data/Services/LocalAccountManagementService.cs` | 179 |
| `src/Orderly.Data/Services/LocalAccountManagementService.Credentials.cs` | 350 |
| `src/Orderly.Data/Services/LocalAccountManagementService.Helpers.cs` | 124 |
| `src/Orderly.Data/Services/LocalGlobalSearchService.cs` | 87 |
| `src/Orderly.Data/Services/LocalGlobalSearchService.Matching.cs` | 123 |
| `src/Orderly.Data/Services/LocalGlobalSearchService.Projectors.cs` | 369 |
| `src/Orderly.Data/Services/LocalWorkbenchTaskService.cs` | 276 |
| `src/Orderly.Data/Services/LocalWorkbenchTaskService.Builders.cs` | 406 |
| `src/Orderly.Data/Services/QaDataSeeder.cs` | 114 |
| `src/Orderly.Data/Services/QaDataSeeder.Parameters.cs` | 205 |
| `src/Orderly.Data/Services/QaDataSeeder.SeedData.cs` | 68 |
| `src/Orderly.Data/Services/QaDataSeeder.Types.cs` | 148 |
| `src/Orderly.Data/Services/QaDataSeeder.Upserts.Primary.cs` | 382 |
| `src/Orderly.Data/Services/QaDataSeeder.Upserts.Secondary.cs` | 454 |

## Mechanical split summary

### `QaDataSeeder*`

- Main file now keeps only constants, constructor, QA argument checks, and `SeedIfNeededAsync`.
- Upsert members were relocated as whole methods into two partial files.
- Parameter helpers moved intact into one partial file.
- Nested result/type declarations moved intact into one partial file.
- Static seed arrays moved intact into one partial file.
- Seed values, ordering, baseline counts, reset expectations, and QA script assumptions were preserved.

### `DemoDataSeeder*`

- Main file now keeps request detection, counts, `EnsureMinimum*`, `EnsureCustomerAsync`, `GetIdByTextKeyAsync`, and `DemoCounts`.
- All `Insert*` methods and `ToDbInt` moved intact into one persistence partial.
- Nested records and static seed arrays moved intact into one data partial.

### `LocalWorkbenchTaskService*`

- Main file keeps constructor, public entrypoints, finalization, filtering, comparer, and common helpers.
- Task-building methods plus supporting builder-only record structs moved intact into one builders partial.

### `LocalGlobalSearchService*`

- Main file keeps fields, constructor, and `SearchAsync`.
- All entity projector methods and their direct summary/action helpers moved intact into one projectors partial.
- Query normalization, scoring, match structs, and comparer moved intact into one matching partial.

### `LocalAccountManagementService*`

- Main file keeps account list/create/disable/delete flows.
- Credential/reset/recovery flows moved intact into one credentials partial.
- Session/account lookup, summary mapping, PIN/hash, key wrap/unwrap, and workspace delete helpers moved intact into one helpers partial.

## Compliance proof

- Every selected/refactored non-UI target family now has no file over 500 lines.
- `git status --short` after the batch shows edits only inside the five selected target families.
- `git diff` on the original files shows:
  - `class -> partial class`
  - whole-member removals from the original file
  - no in-place logic rewrite in the retained members
- New files are same-namespace partials containing relocated complete members/types only.
- No new abstractions, interface changes, DTO changes, SQL changes, JSON changes, state-machine changes, or behavior cleanup were introduced.

## Unchanged protected areas confirmation

- UI/XAML/code-behind untouched
- login page untouched
- settings page untouched
- order fulfillment page untouched
- exception-handling page untouched
- backend fields/interfaces/API contracts untouched
- database schema/migration files untouched
- payment/order creation/callback/automatic paid-state flows untouched
- fulfillment/shipping sync untouched
- inventory cloud sync untouched
- mini-program files untouched

## Build and QA results

- `dotnet build Orderly.sln -c Debug`: PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-qa-data-status.ps1`: PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1`: PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-2-pipeline-smoke.ps1`: PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-5-search-smoke.ps1`: PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-6-navigation-smoke.ps1`: PASS
- Local preview evidence: `run-p1-smoke.ps1` launched `Orderly.App.exe --qa-mode`, completed UIA smoke PASS, then restored QA baseline

## Deferred violations

### UI files still over 300 lines

| Path | Lines |
| --- | ---: |
| `src/Orderly.App/App.xaml` | 330 |
| `src/Orderly.App/Views/LoginView.xaml` | 1645 |
| `src/Orderly.App/Views/MainWindow.xaml` | 7975 |
| `src/Orderly.App/Views/Resources/MainWindowResources.xaml` | 1768 |

### Protected or non-edited non-UI files still over 500 lines

| Path | Lines | Reason |
| --- | ---: | --- |
| `src/Orderly.App/App.xaml.cs` | 627 | UI/app startup source |
| `src/Orderly.App/ViewModels/LoginViewModel.cs` | 812 | login page protected |
| `src/Orderly.App/ViewModels/MainViewModel.cs` | 651 | UI-related ViewModel, not approved in this batch |
| `src/Orderly.App/ViewModels/MainViewModel.ExceptionOrders.cs` | 652 | exception page protected |
| `src/Orderly.App/ViewModels/MainViewModel.SettingsP0.cs` | 1183 | settings page protected |
| `src/Orderly.App/ViewModels/MainViewModel.StringNarrationOrders.cs` | 1612 | fulfillment/order page protected |
| `src/Orderly.App/Views/LoginView.xaml.cs` | 915 | code-behind forbidden |
| `src/Orderly.App/Views/MainWindow.xaml.cs` | 777 | code-behind forbidden |
| `src/Orderly.Core/Models/StringNarrationProductionModels.cs` | 824 | already handled family |
| `src/Orderly.Data/Services/CloudInventoryWorkspaceService.cs` | 737 | inventory cloud sync protected |
| `src/Orderly.Data/Services/LocalBackupService.Restore.cs` | 551 | already handled family |
| `src/Orderly.Data/Services/StringNarrationGatewayBusinessService.cs` | 1086 | protected gateway/backend service |
| `src/Orderly.Data/Services/StringNarrationGatewayOrderService.cs` | 1696 | protected order/payment/fulfillment-adjacent flow |
| `src/Orderly.Data/Sqlite/DatabaseInitializer.cs` | 582 | schema/migration-sensitive SQL |

### Uncertain files needing separate authorization

- none in this batch classification

## Final batch safety assessment

- Result: safe for independent read-only review
- Reason: only selected safe non-UI target families changed; validation passed; protected/UI areas remained untouched
- Commit status: not committed

