# CODEX Batch Baseline Overlimit Remediation Review

## Batch scope

- Repo: `D:\Dev\Orderly-SN`
- Date: `2026-05-30`
- Mode: continuation of the same uncommitted remediation batch
- Precondition check:
  - `git status --short` was clean before the original batch started
  - continuation resumed only after confirming the in-progress working tree contained exactly the expected four batch files:
    - `src/Orderly.Data/Services/LocalBackupService.Restore.cs`
    - `src/Orderly.Data/Services/LocalBackupService.Restore.Preview.cs`
    - `src/Orderly.Data/Services/LocalBackupService.Restore.Launcher.cs`
    - `docs/CODEX_BATCH_BASELINE_OVERLIMIT_REMEDIATION_REVIEW.md`
- Existing successful restore split was preserved and not reverted.

## Baseline violations and original line counts

The user prompt carried approximate counts. The coordinator re-measured exact source counts from the working baseline before final remediation decisions:

| Scope | File | Original lines | Final lines | Result |
| --- | --- | ---: | ---: | --- |
| Initial batch target | `src/Orderly.Data/Services/LocalBackupService.Restore.cs` | `618` | `231` | fixed |
| Continuation Target A | `src/Orderly.Core/Models/StringNarrationProductionModels.cs` | `949` | `371` | fixed |
| Continuation Target B | `src/Orderly.Data/Services/LocalBackupService.Shared.cs` | `511` | `410` | fixed |

## Subagent allocation

- Initial batch worker: restore partial split for `LocalBackupService.Restore.cs`
- Continuation `Target A` worker: partial split for `StringNarrationProductionSheetSnapshot`
- Continuation `Target B` worker: compliance split for `LocalBackupService.Shared.cs`
- Coordinator:
  - verified the expected in-progress working tree
  - independently reviewed both continuation diffs against `HEAD`
  - confirmed no protected-area edits
  - measured final line counts across both families
  - ran build and the required QA scripts
  - updated this single consolidated review document

## Files changed in the complete combined batch

- `src/Orderly.Core/Models/StringNarrationProductionModels.cs`
- `src/Orderly.Core/Models/StringNarrationProductionSheetSnapshot.Materials.cs`
- `src/Orderly.Core/Models/StringNarrationProductionSheetSnapshot.Helpers.cs`
- `src/Orderly.Data/Services/LocalBackupService.Restore.cs`
- `src/Orderly.Data/Services/LocalBackupService.Restore.Preview.cs`
- `src/Orderly.Data/Services/LocalBackupService.Restore.Launcher.cs`
- `src/Orderly.Data/Services/LocalBackupService.Shared.cs`
- `src/Orderly.Data/Services/LocalBackupService.Shared.Types.cs`
- `docs/CODEX_BATCH_BASELINE_OVERLIMIT_REMEDIATION_REVIEW.md`

## Target A: `StringNarrationProductionModels.cs`

### Original problem

- The family originally had a single file:
  - `src/Orderly.Core/Models/StringNarrationProductionModels.cs` = `949` lines
- Inside it, `StringNarrationProductionSheetSnapshot` alone occupied roughly `834` lines.
- Earlier complete-type-only relocation rules were insufficient.
- Continuation explicitly authorized a narrow same-type `partial` split with complete-member movement only.

### Structural split applied

`StringNarrationProductionSheetSnapshot` was converted from:

- `public sealed class StringNarrationProductionSheetSnapshot`

to:

- `public sealed partial class StringNarrationProductionSheetSnapshot`

No type name, namespace, visibility, public API, property, method signature, method body, string literal, constant, JSON/serialization contract, or behavior was changed.

### Members retained in `src/Orderly.Core/Models/StringNarrationProductionModels.cs`

- `StringNarrationProductionOrderSnapshot`
- `StringNarrationWorkOrderSnapshot`
- `StringNarrationProductionSheetMaterialItem`
- `StringNarrationProductionSheetSnapshot` static field arrays
- `StringNarrationProductionSheetSnapshot` public state properties
- `StringNarrationProductionSheetSnapshot` public computed properties
- `StringNarrationProductionSheetSnapshot.Create(...)`

### Members moved to `src/Orderly.Core/Models/StringNarrationProductionSheetSnapshot.Materials.cs`

- `ResolveMaterials`
- `ExtractMaterials`
- `ParseMaterialArray`
- `ParseMaterialItem`
- `ResolveArrangementText`
- `ResolveExampleImageUrl`
- `ResolveMaterialsFromProduct`
- `BuildArrangementFromProduct`
- `ExtractBeadsFromDetail`
- `ExtractBeads`
- `ParseBeadArray`
- `CompressBeadSequence`

### Members moved to `src/Orderly.Core/Models/StringNarrationProductionSheetSnapshot.Helpers.cs`

- `FindNamedValue`
- `ExtractMeaningfulText`
- `ReadPositiveInt`
- `ReadScalarProperty`
- `ReadNonEmptyString`
- `ReadFirstNonEmptyString`
- `ParsePositiveInt`
- `NormalizeScalar`
- `TryGetPropertyIgnoreCase`
- `NormalizeImageUrl`
- `NormalizeLoopType`
- `ParseLeadingInt`
- `FirstNonEmpty`
- `MapStatusTextToCode`
- `BuildValue`
- `ProductionBeadToken`

## Target B: `LocalBackupService.Shared.cs`

### Original problem

- After the successful restore split was preserved, the `LocalBackupService*.cs` family still had one remaining violation:
  - `src/Orderly.Data/Services/LocalBackupService.Shared.cs` = `511` lines

### Structural split applied

A new coherent partial file was added:

- `src/Orderly.Data/Services/LocalBackupService.Shared.Types.cs`

Complete existing nested types were mechanically moved from `LocalBackupService.Shared.cs` into that new partial file:

- `LauncherAccountBackupRow`
- `TargetInspectionResult`

No backup format, checksum behavior, metadata construction, JSON/Base64/blob conversion, QA tag detection, nested-type content, visibility, string literal, error handling, restore gating, restore-preview safety, or launcher snapshot behavior was changed.

## Preserved restore split from the existing in-progress batch

The continuation kept the previously successful restore structural split intact:

- `src/Orderly.Data/Services/LocalBackupService.Restore.cs`
- `src/Orderly.Data/Services/LocalBackupService.Restore.Preview.cs`
- `src/Orderly.Data/Services/LocalBackupService.Restore.Launcher.cs`

That preserved split consists of:

Moved to `LocalBackupService.Restore.Preview.cs`:

- `PreviewRestoreAsync`
- `InspectTargetAsync`
- `CountRowsAsync`
- `BuildTargetAssessmentPredicate`
- `BuildExportPredicate`
- `GetQaScopePredicate`
- `GetAuditExclusionPredicate`
- `BuildRestorePreviewResult`

Moved to `LocalBackupService.Restore.Launcher.cs`:

- `RestoreLauncherSnapshotAsync`
- `ValidateLauncherSnapshotRow`

Retained in `LocalBackupService.Restore.cs`:

- `RestoreBackupAsync`
- `InsertTableRowsAsync`

## Coordinator diff review conclusion

- Reviewed against `HEAD`
- All code edits are structural relocation only
- No behavior-affecting edits found
- No contract/API/serialization/SQL/string/error-message changes found
- No omitted members found
- No duplicated declarations found
- No protected-area file was touched

## Exact final line counts

### `StringNarrationProduction*.cs`

| File | Final lines |
| --- | ---: |
| `src/Orderly.Core/Models/StringNarrationProductionModels.cs` | `371` |
| `src/Orderly.Core/Models/StringNarrationProductionSheetSnapshot.Helpers.cs` | `273` |
| `src/Orderly.Core/Models/StringNarrationProductionSheetSnapshot.Materials.cs` | `317` |

### `LocalBackupService*.cs`

| File | Final lines |
| --- | ---: |
| `src/Orderly.Data/Services/LocalBackupService.cs` | `92` |
| `src/Orderly.Data/Services/LocalBackupService.Export.cs` | `286` |
| `src/Orderly.Data/Services/LocalBackupService.Restore.cs` | `231` |
| `src/Orderly.Data/Services/LocalBackupService.Restore.Launcher.cs` | `200` |
| `src/Orderly.Data/Services/LocalBackupService.Restore.Preview.cs` | `201` |
| `src/Orderly.Data/Services/LocalBackupService.Shared.cs` | `410` |
| `src/Orderly.Data/Services/LocalBackupService.Shared.Types.cs` | `108` |
| `src/Orderly.Data/Services/LocalBackupService.Validation.cs` | `228` |

## Compliance proof

The two authorized families in this combined in-progress batch now fully comply with the non-UI `<= 500` standard:

- `StringNarrationProduction*.cs`: all files `<= 500`
- `LocalBackupService*.cs`: all files `<= 500`

## Contract and safety invariants

- `StringNarrationProductionSheetSnapshot` caller-facing model contract preserved exactly
- `StringNarrationProductionSheetSnapshot` serialization-facing members preserved exactly
- `LocalBackupService` public API preserved exactly
- Backup manifest/checksum behavior preserved exactly
- Restore gating and QA-clear protections preserved exactly
- Restore-preview refusal logic preserved exactly
- Launcher snapshot restore logic preserved exactly
- SQL ordering and database operations preserved exactly

## Build and QA results

### Build precondition

- `Orderly.App.exe` was not running before the solution build, so no stop was required.

### Commands run

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-qa-data-status.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-2-pipeline-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-5-search-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-6-navigation-smoke.ps1
```

### Results

- `dotnet build Orderly.sln -c Debug`: `PASS`
  - `0 warnings`
  - `0 errors`
- `run-qa-data-status.ps1`: `PASS`
- `run-p1-smoke.ps1`: `PASS`
  - `P1 WRITE SMOKE: PASS`
  - `UIA smoke PASS`
  - UIA report: `artifacts/qa-smoke/20260530_003623_085_a16306/smoke-report.json`
- `run-p3-2-pipeline-smoke.ps1`: `PASS`
- `run-p3-5-search-smoke.ps1`: `PASS`
- `run-p3-6-navigation-smoke.ps1`: `PASS`

## Remaining over-limit files deferred because they are UI or protected backend/transaction areas

### UI or UI-adjacent

- `src/Orderly.App/Views/MainWindow.xaml` — `8302`
- `src/Orderly.App/Views/Resources/MainWindowResources.xaml` — `1854`
- `src/Orderly.App/ViewModels/MainViewModel.StringNarrationOrders.cs` — `1809`
- `src/Orderly.App/Views/LoginView.xaml` — `1708`
- `src/Orderly.App/ViewModels/MainViewModel.SettingsP0.cs` — `1358`
- `src/Orderly.App/Views/LoginView.xaml.cs` — `1063`
- `src/Orderly.App/ViewModels/LoginViewModel.cs` — `923`
- `src/Orderly.App/Views/MainWindow.xaml.cs` — `913`
- `src/Orderly.App/ViewModels/MainViewModel.ExceptionOrders.cs` — `750`
- `src/Orderly.App/ViewModels/MainViewModel.cs` — `746`
- `src/Orderly.App/App.xaml.cs` — `714`
- `src/Orderly.App/ViewModels/MainViewModel.SettingsP1.cs` — `545`
- `src/Orderly.App/ViewModels/MainViewModel.BusinessPages.cs` — `517`
- `src/Orderly.App/App.xaml` — `344`

### Protected backend / transaction

- `src/Orderly.Data/Services/StringNarrationGatewayOrderService.cs` — `1911`
- `src/Orderly.Data/Services/StringNarrationGatewayBusinessService.cs` — `1225`
- `src/Orderly.Data/Services/CloudInventoryWorkspaceService.cs` — `835`
- `src/Orderly.Data/Sqlite/DatabaseInitializer.cs` — `634`

### Additional non-production scratch file outside the authorized target families

- `src/scratch/Program.cs` — `527`

## Kiro Opus 4.8 read-only review readiness

- **Ready for Kiro Opus 4.8 read-only review for this completed combined batch.**
- Reason:
  - all authorized target families in the batch are now compliant
  - the coordinator independently reviewed the diffs
  - build passed
  - all required QA scripts passed
