# Opus 4.8 — StringNarrationOrderModels.cs God-File Split Review

## 1. Summary

This is a strictly mechanical file-organization refactor. The oversized model
aggregation file for the 串述 (string-narration) order module was split into
seven cohesive model files. No type, member, namespace, attribute, default
value, nullable annotation, serialization contract, or business behavior was
changed. No caller files were touched.

## 2. Original File

- **Path:** `src/Orderly.Core/Models/StringNarrationOrderModels.cs`
- **Original line count:** 2081 lines (`Get-Content .Count` = 2081; total bytes = 76,718)
- **Encoding:** UTF-8 without BOM, CRLF line endings
- **Namespace:** `Orderly.Core.Models`
- **Only explicit using:** `using System.Text.Json;` (project has `ImplicitUsings=enable`, `Nullable=enable`)
- **Status after refactor:** Deleted (all 31 types relocated; no residual group remained).

## 3. Pre-Split Type Inventory (31 types)

All types were declared at the top level in namespace `Orderly.Core.Models`:

| # | Type | Kind |
|---|------|------|
| 1 | `StringNarrationOrderQuery` | sealed class |
| 2 | `StringNarrationPageInfo` | sealed class |
| 3 | `StringNarrationOrderListResult` | sealed class |
| 4 | `StringNarrationGatewayResponse<T>` | sealed generic class |
| 5 | `StringNarrationWhoamiResult` | sealed class |
| 6 | `StringNarrationFulfillmentStatusMetric` | sealed class |
| 7 | `StringNarrationFulfillmentStats` | sealed class |
| 8 | `StringNarrationFulfillmentStatusDefinition` | sealed class |
| 9 | `StringNarrationFulfillmentStatusCatalog` | static class |
| 10 | `StringNarrationExceptionSnapshot` | sealed class |
| 11 | `StringNarrationBusinessTrendPoint` | sealed class |
| 12 | `StringNarrationFulfillmentPressureMetric` | sealed class |
| 13 | `StringNarrationWorkbenchDashboardStats` | sealed class |
| 14 | `StringNarrationOrderSummary` | class (base) |
| 15 | `StringNarrationOrderDetail` | sealed class : StringNarrationOrderSummary |
| 16 | `StringNarrationOrderItemSnapshot` | sealed class |
| 17 | `StringNarrationAddressSnapshot` | sealed class |
| 18 | `StringNarrationProductionOrderSnapshot` | sealed class |
| 19 | `StringNarrationWorkOrderSnapshot` | sealed class |
| 20 | `StringNarrationProductionSheetMaterialItem` | sealed class |
| 21 | `StringNarrationProductionSheetSnapshot` | sealed class |
| 22 | `StringNarrationFulfillmentUpdateRequest` | sealed class |
| 23 | `StringNarrationExceptionActionRequest` | sealed class |
| 24 | `StringNarrationExceptionActionResult` | sealed class |
| 25 | `StringNarrationExceptionSampleReplayRequest` | sealed class |
| 26 | `StringNarrationExceptionSample` | sealed class |
| 27 | `StringNarrationExceptionSampleReplayResult` | sealed class |
| 28 | `StringNarrationExceptionSampleReplayItem` | sealed class |
| 29 | `StringNarrationGenerateProductionOrderRequest` | sealed class |
| 30 | `StringNarrationExceptionAuditEntry` | sealed class |
| 31 | `StringNarrationStatusLog` | sealed class |

> Note: `StringNarrationExceptionFieldCatalog` is referenced by several of these
> types but is defined in a separate, pre-existing file
> (`StringNarrationExceptionFieldCatalog.cs`) and was **not** part of this split.

## 4. Destination Files and Types Moved

All destination files remain in `src/Orderly.Core/Models/` under namespace
`Orderly.Core.Models`.

| Destination file | Types moved | `using System.Text.Json;` |
|---|---|---|
| `StringNarrationOrderQueryModels.cs` | `StringNarrationOrderQuery`, `StringNarrationPageInfo`, `StringNarrationOrderListResult`, `StringNarrationGatewayResponse<T>`, `StringNarrationWhoamiResult` | not needed |
| `StringNarrationFulfillmentStatusModels.cs` | `StringNarrationFulfillmentStatusMetric`, `StringNarrationFulfillmentStats`, `StringNarrationFulfillmentStatusDefinition`, `StringNarrationFulfillmentStatusCatalog` | not needed |
| `StringNarrationExceptionSnapshot.cs` | `StringNarrationExceptionSnapshot` | included (uses `JsonElement`) |
| `StringNarrationWorkbenchDashboardModels.cs` | `StringNarrationBusinessTrendPoint`, `StringNarrationFulfillmentPressureMetric`, `StringNarrationWorkbenchDashboardStats` | not needed |
| `StringNarrationOrderSummaryModels.cs` | `StringNarrationOrderSummary`, `StringNarrationOrderDetail`, `StringNarrationOrderItemSnapshot`, `StringNarrationAddressSnapshot` | included (uses `JsonElement`/`JsonSerializer`) |
| `StringNarrationProductionModels.cs` | `StringNarrationProductionOrderSnapshot`, `StringNarrationWorkOrderSnapshot`, `StringNarrationProductionSheetMaterialItem`, `StringNarrationProductionSheetSnapshot` | included (uses `JsonElement`) |
| `StringNarrationOrderOperationModels.cs` | `StringNarrationFulfillmentUpdateRequest`, `StringNarrationExceptionActionRequest`, `StringNarrationExceptionActionResult`, `StringNarrationExceptionSampleReplayRequest`, `StringNarrationExceptionSample`, `StringNarrationExceptionSampleReplayResult`, `StringNarrationExceptionSampleReplayItem`, `StringNarrationGenerateProductionOrderRequest`, `StringNarrationExceptionAuditEntry`, `StringNarrationStatusLog` | included (uses `JsonElement`) |

Each type body was relocated as a contiguous verbatim slice of the original
file. Type ordering within each destination file matches the original ordering.
The `using System.Text.Json;` directive was added only to the four files whose
relocated types reference `System.Text.Json` members; the other three files
correctly omit it (verified by build with 0 warnings).

## 5. Preservation Confirmation

- **Namespace:** All seven files use `namespace Orderly.Core.Models;` — unchanged.
- **Type names / member names:** Verbatim slices; no renames. All 31 expected type names confirmed present (1:1, zero missing, zero duplicates).
- **Visibility / sealed / static / inheritance:** Preserved (e.g., `StringNarrationOrderDetail : StringNarrationOrderSummary` kept in the same file as its base).
- **Property types, nullable annotations, default values:** Untouched (byte-for-byte slice).
- **Attributes / serialization contracts:** No attributes were present; JSON handling is via `System.Text.Json` `JsonElement`/`JsonSerializer` calls inside property bodies, which were relocated unchanged. Caller-facing API shapes are identical.
- **Constructors / method bodies / enum members / constants:** Unchanged, including the Chinese inline comment on `StringNarrationFulfillmentStatusCatalog.Exception` (verified intact and correctly encoded after fixing an encoding pitfall — see §8).
- **No new abstractions / base classes / interfaces / mapping helpers** were introduced.
- **No business logic changed. No UI/XAML changed. No mini-program files changed.**

## 6. Caller File Modifications

**None.** Because the namespace (`Orderly.Core.Models`) is unchanged, all callers
resolve the relocated types without any edit. `git status` shows exactly one
deletion and seven additions, all under `src/Orderly.Core/Models/`.

## 7. Build and QA Results

Pre-build check: no `Orderly.App` process was running (no build-output lock).

| Command | Result |
|---|---|
| `dotnet build Orderly.sln -c Debug` | **PASS** — 0 warnings, 0 errors (Orderly.Core, Orderly.Infrastructure, Orderly.Data, Orderly.App all built) |
| `tools\qa\run-p1-smoke.ps1` | **PASS** — UIA smoke PASS; write-chain PASS; QA baseline restored |
| `tools\qa\run-p3-2-pipeline-smoke.ps1` | **PASS** — pipeline stages resolved; no schema mutation; OrderStatus/DealStage unmutated |
| `tools\qa\run-p3-5-search-smoke.ps1` | **PASS** — search coverage 35; workbench tasks 65 |
| `tools\qa\run-p3-6-navigation-smoke.ps1` | **PASS** — workbench tasks 71; 5 route summaries checked; local-only confirmed |

`git diff --cached --stat` (src/ only): 8 files changed, 2094 insertions(+),
2082 deletions(-). The net +12 lines is solely the repeated
`namespace`/`using` header lines across the seven new files — expected mechanical
overhead, not content change.

## 8. Non-Mechanical Edits

**None.** The only deviation from a naive copy is the per-file header
(`namespace Orderly.Core.Models;` plus, where required, `using System.Text.Json;`),
which is mandatory for the relocated types to compile and is structural, not
behavioral.

**Encoding pitfall encountered and corrected:** A first split attempt used
PowerShell `Get-Content`, which defaulted to the ANSI code page and corrupted
UTF-8 Chinese characters (and merged one comment line). This was detected before
build, fully reverted via `git checkout`, and the split was redone using
explicit UTF-8 (no BOM) read/write with CRLF preservation. The final files were
verified to be UTF-8 no-BOM, CRLF, with zero Unicode replacement characters, and
the Chinese comment on the `Exception` constant is intact on its own line.

## 9. Residual Risks

- **Low.** The change is pure file relocation within one namespace and one
  project. The compiler resolves all references identically.
- No public-network, AI API, payment-callback, fulfillment-sync, or
  inventory-sync code paths were in scope; the smoke suites that exercise the
  pipeline, search, and navigation projections all pass.
- The only subtle risk class (text encoding) was explicitly checked and cleared.

## 10. Protected-Flow Confirmation

The following were **untouched** by this refactor:
- WeChat payment callback / order creation, automatic paid transition,
  fulfillment/shipping synchronization, and payment-to-fulfillment closed-loop
  behavior — no logic in those paths was modified (only model declarations moved).
- Inventory cloud sync behavior — unchanged.
- UI / XAML — unchanged.
- Mini-program compatibility — no mini-program files changed.

## 11. Commit Safety

**Safe to commit.** The refactor is structural type relocation only, builds
clean with 0 warnings / 0 errors, passes all four required QA smoke suites,
touches no caller files, and preserves every API and serialization contract.
