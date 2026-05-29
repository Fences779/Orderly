# LocalBackupService Partial Split Review

## Source

- Original file: `src/Orderly.Data/Services/LocalBackupService.cs`
- Original line count before split: `1491`

## Responsibility Inventory Before Splitting

- Export and backup history
  - `ExportAsync`
  - `GetLatestBackupAsync`
  - manifest assembly
  - table row export
  - launcher account snapshot export
- Validation and manifest parsing
  - `ValidateAsync`
  - `ValidateCoreAsync`
  - manifest JSON parsing
  - checksum calculation
  - validation activity logging
- Restore preview and restore execution
  - `PreviewRestoreAsync`
  - `RestoreBackupAsync`
  - target inspection
  - QA-only / empty-db / non-empty production gating
  - row insert restore
  - launcher snapshot restore
- Shared serialization, metadata and helper logic
  - JSON-to-db value conversion
  - Base64/blob conversion
  - QA tag detection
  - sync/activity metadata builders
  - nested backup/inspection helper types

## Destination Files And Members Moved

- `src/Orderly.Data/Services/LocalBackupService.cs`
  - kept class declaration
  - kept constants, shared arrays, serializer options, shared fields, constructor
- `src/Orderly.Data/Services/LocalBackupService.Export.cs`
  - moved `ExportAsync`
  - moved `GetLatestBackupAsync`
  - moved `BuildManifestAsync`
  - moved `ReadTableRowsAsync`
  - moved `AppendLauncherSnapshotAsync`
  - moved `ResolveCurrentAccountIdAsync`
- `src/Orderly.Data/Services/LocalBackupService.Validation.cs`
  - moved `ValidateAsync`
  - moved `ValidateCoreAsync`
  - moved `ParseManifest`
- `src/Orderly.Data/Services/LocalBackupService.Restore.cs`
  - moved `PreviewRestoreAsync`
  - moved `RestoreBackupAsync`
  - moved `InspectTargetAsync`
  - moved `CountRowsAsync`
  - moved `BuildTargetAssessmentPredicate`
  - moved `BuildExportPredicate`
  - moved `GetQaScopePredicate`
  - moved `GetAuditExclusionPredicate`
  - moved `InsertTableRowsAsync`
  - moved `RestoreLauncherSnapshotAsync`
  - moved `ValidateLauncherSnapshotRow`
  - moved `BuildRestorePreviewResult`
- `src/Orderly.Data/Services/LocalBackupService.Shared.cs`
  - moved `WriteValidationActivityAsync`
  - moved `ConvertJsonValue`
  - moved `ToBase64Nullable`
  - moved `FromBase64`
  - moved `ToDbBlobFromBase64`
  - moved `SanitizeValue`
  - moved `IsQaTaggedBackup`
  - moved `ComputeChecksum`
  - moved `ParseMetadata`
  - moved `ParseCounts`
  - moved `TryGetDateTimeOffset`
  - moved all `Build*MetadataJson` helpers
  - moved `TagQaMetadataIfNeeded`
  - moved `GenerateBackupEntityId`
  - moved `LauncherAccountBackupRow`
  - moved `TargetInspectionResult`

## Compatibility Confirmation

- `LocalBackupService` type name, namespace, constructor, dependencies, implemented interface and public API are unchanged.
- Backup file format, JSON shape, serialization options, checksum payload and metadata builders are unchanged.
- Restore eligibility rules, restore-preview output, QA-only cleanup gate, empty-database checks, non-empty production refusal and risk-confirmation behavior are unchanged.
- Backup validation flow, launcher snapshot validation and safety protections are unchanged.
- No backend fields, interface contracts, database schema, cloud sync rules, payment/order/fulfillment flows, UI/XAML or mini-program behavior were touched.

## Non-Mechanical Edit Requirement

- None.
- Only compile-scope `using` placement changed after members were relocated into separate partial files.

## Diff Review Result

- Root file diff is structural: convert class to `partial` and remove relocated members.
- Added files contain relocated members grouped by responsibility.
- No method signature, parameter default, nullable annotation, error message text or business rule was intentionally changed.

## Build And QA Results

- `dotnet build Orderly.sln -c Debug`
  - PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1`
  - PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-2-pipeline-smoke.ps1`
  - PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-5-search-smoke.ps1`
  - PASS
- `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-6-navigation-smoke.ps1`
  - PASS

## Residual Risk

- The refactor is mechanical, but git currently shows the new partial files as newly added files rather than pure rename tracking; review should focus on member placement rather than rename heuristics.
- Future edits must preserve cross-file coupling between restore/export helpers and shared metadata helpers.

## Commit Safety

- Safe to commit: `yes`
