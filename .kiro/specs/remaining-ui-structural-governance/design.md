# Design — Remaining UI Structural Governance

## Overview

This design defines the mechanical, behavior-preserving decomposition of the remaining over-budget
UI-layer files in `Orderly.App`. Every transformation is a verbatim relocation: code-behind methods
move into same-class `partial` files; XAML resources move into merged `ResourceDictionary` files;
and (where safe) XAML visual subtrees move into `UserControl` files whose `DataContext` is inherited
unchanged from the parent. No logic, values, bindings, commands, events, animations, or visual
output change.

### Project compile model (verified)

`src/Orderly.App/Orderly.App.csproj` is SDK-style with `UseWPF=true`. C# files and `Page`-typed XAML
are **implicitly globbed** — new `*.cs` partials and new `ResourceDictionary` `.xaml` files are
auto-included in compilation with no `.csproj` edit required. This keeps the project file untouched
(satisfies the "no non-UI change" boundary; the csproj is build config, not business logic, and
needs no edit here).

### Naming convention (verified)

The already-governed app family uses dotted partial names: `App.Composition.cs`, `App.SessionLock.cs`,
`App.WorkspaceComposition.cs`. This design follows the identical convention for `MainWindow` partials
(`MainWindow.TrendCharts.cs`, etc.) and for resource dictionaries under `Views/Resources/`.

---

## Starting Inventory & Source Maps

### MainWindow.xaml.cs (913 lines) — full member map

| Lines | Member | Responsibility |
|-------|--------|----------------|
| 12–34 | field `_copyToastCts`, ctor, `OnClosing` | **Core lifecycle** (must stay in `MainWindow.xaml.cs`) |
| 36–60 | `Btn_SelectDateRange_Click`, `Btn_ClearDateRange_Click`, `Btn_ApplyDateRange_Click` | Workbench date-range popup |
| 61–106 | `QuickFulfillmentUpdate_Click`, `ContactCustomer_Click`, `ModifyInfo_Click`, `CancelOrder_Click` | Fulfillment actions |
| 107–154 | `CloseDetails_Click` | Fulfillment detail-panel close animation |
| 155–172 | `StringNarrationOrdersList_MouseDoubleClick` | Fulfillment list interaction |
| 173–191 | `ExceptionOrderCard_MouseDoubleClick` | Exception list interaction |
| 192–214 | `SettingsTextInput_LostFocus` | Settings auto-save hook |
| 215–237 | `ViewModel_PropertyChanged` | **Core dispatch** (routes to chart updates + toast) |
| 238–258 | `ShowCopyToast` | Copy-toast helper |
| 259–271 | `StepperNode_MouseLeftButtonDown` | Fulfillment stepper |
| 272–288 | `FulfillmentInput_KeyDown` | Fulfillment input |
| 289–336 | `CloseExceptionDetails_Click` | Exception detail-panel close animation |
| 337–346 | `JumpToOrderFulfillment_Click` | Exception→fulfillment navigation |
| 347–362 | `CopyText_Click` | Copy helper |
| 363–377 | `FindAncestor<T>` | **Core helper** (used by double-click handlers) |
| 378–550 | `TrendCanvas_SizeChanged`, `TrendTooltip_SizeChanged`, `UpdateTrendChart`, `UpdateTrendTooltipPosition` | Trend chart |
| 551–565 | `CashflowTrendCanvas_SizeChanged`, `CanvasIncomeDonut_SizeChanged`, `CanvasExpenseDonut_SizeChanged` | Chart size events |
| 566–748 | `UpdateCashflowTrendChart` | Cashflow chart |
| 749–855 | `UpdateDonutCharts`, `UpdateSingleDonutChart` | Donut charts |
| 856–878 | `DrawPlaceholderDonut` | Donut placeholder |
| 879–912 | `MainWindow_SizeChanged`, `UpdateCashflowTrendCardVisibility` | Responsive layout |

### App.xaml (344 lines) — resource map

Global brushes (8–14), implicit `Window`/`ScrollBar`/`Button`/`TextBox`/`PasswordBox`/`ComboBox`
styles (17–198), then keyed styles `NavButtonStyle`, `CardBorderStyle`, `DialogPrimaryButtonStyle`,
`DialogSecondaryButtonStyle`, `DialogFieldLabelStyle`, `DialogSectionTitleStyle`, `ChipButtonStyle`,
`ChipButtonActiveStyle` (200–343).

### MainWindowResources.xaml (1854 lines) — resource map

- 5–36: converters, color brushes, `CardShadow`
- 38–626: shared component styles (text, cards, lists, toolbar buttons, exception buttons, paging, copy buttons, pills, tab radio)
- 627–782: `CustomerListItemTemplate`, `OrderListItemTemplate` (data templates)
- 783–1458: **Settings** styles (FROZEN domain — see constraint below)
- 1460–1854: navigation/profile button styles (`MainMenuButtonStyle`, `ProfileMenuButtonStyle`, `ProfileMenuGlassButtonStyle`, `SubtleActionButtonStyle`, `InvisibleContainerButtonStyle`, `InvisibleContainerToggleButtonStyle`)

### MainWindow.xaml (8302 lines) — visual-tree map

- 10–253: `Window.Resources` (inline `FulfillmentInputTextBoxStyle`, `FulfillmentComboBoxToggleButtonStyle`, ComboBox item/main styles + merged dictionary reference)
- 255–419: root `Grid`, left nav sidebar + "我的" button + shell header
- 420–1271: **工作台 / Workbench** section (`ConverterParameter=工作台`)
- 1273–2057: **库存管理 / Inventory** section (`ConverterParameter=库存管理`)
- 2059–2593: **现金流 / Cashflow** section (`ConverterParameter=现金流`)
- 2595–5249: **订单履约 / Order Fulfillment** section (`ConverterParameter=订单履约`) — **FROZEN**
- 5251–6397: **异常处理 / Exception** section (`ConverterParameter=异常处理`) — **FROZEN**
- 6399–7977: **设置 / Settings** TabControl (`ConverterParameter=设置`) — **FROZEN**
- 7979–8287: **我的 / Me-Profile** section (`ConverterParameter=我的`)
- 8288–8302: `Popup_CopyToast` + closing tags

---

## Architecture

### Decomposition Strategy

### Mechanism A — Code-behind partial classes (`MainWindow.xaml.cs`)

`MainWindow` stays one class; methods are split into `partial class MainWindow` files. The compiler
treats all partials as one type, so event wiring in XAML (`Click="..."`, `MouseDoubleClick="..."`)
and `x:Name` field access continue to resolve identically. **Zero behavior risk** — this is the same
mechanism already accepted for the `App` family.

#### Chart-code destination decision (move-once rule)

Chart/rendering methods reference many window-level `x:Name` elements and are dispatched from the
window-level `ViewModel_PropertyChanged` (subscribed to the shared `MainViewModel` in the ctor).
Because `ViewModel_PropertyChanged` lives at the window level and dispatches to `UpdateTrendChart`,
`UpdateCashflowTrendChart`, and `UpdateDonutCharts`, and because the trend chart lives in the Workbench
section while the cashflow/donut charts live in the Cashflow section, the chart code is potentially
cross-coupled across sections and the window.

To avoid moving chart code twice, a **self-containment scan is performed in Task 1 (pre-flight)** to
choose the single final destination of each chart handler group:

- **If** the Workbench and/or Cashflow visual subtrees are proven safely extractable into UserControls
  under the per-subtree gate (no window-level dispatch coupling that would require changing event
  routing), **then** the related chart/rendering handlers move **directly** into the final extracted
  control's code-behind during section extraction (Batch 5) — never into a temporary partial first.
- **Else** (window-level dispatch coupling or cross-section `x:Name`/`ElementName` coupling prevents
  clean extraction), chart handlers fall back to the planned `MainWindow.*Charts.cs` partial split,
  performed once in Batch 1.

Chart methods are therefore relocated exactly once, to their final home.

Planned partial files (all under `Views/`):

| New file | Methods moved (verbatim) | Est. lines |
|----------|--------------------------|-----------|
| `MainWindow.xaml.cs` (residual core) | field, ctor, `OnClosing`, `ViewModel_PropertyChanged`, `FindAncestor<T>`, `ShowCopyToast`, `CopyText_Click` | ~120 |
| `MainWindow.TrendCharts.cs` | `TrendCanvas_SizeChanged`, `TrendTooltip_SizeChanged`, `UpdateTrendChart`, `UpdateTrendTooltipPosition` | ~175 |
| `MainWindow.CashflowChart.cs` | `CashflowTrendCanvas_SizeChanged`, `UpdateCashflowTrendChart` | ~190 |
| `MainWindow.DonutCharts.cs` | `CanvasIncomeDonut_SizeChanged`, `CanvasExpenseDonut_SizeChanged`, `UpdateDonutCharts`, `UpdateSingleDonutChart`, `DrawPlaceholderDonut` | ~130 |
| `MainWindow.WorkbenchInteractions.cs` | `Btn_SelectDateRange_Click`, `Btn_ClearDateRange_Click`, `Btn_ApplyDateRange_Click` | ~35 |
| `MainWindow.FulfillmentInteractions.cs` | `QuickFulfillmentUpdate_Click`, `ContactCustomer_Click`, `ModifyInfo_Click`, `CancelOrder_Click`, `CloseDetails_Click`, `StringNarrationOrdersList_MouseDoubleClick`, `StepperNode_MouseLeftButtonDown`, `FulfillmentInput_KeyDown` | ~200 |
| `MainWindow.ExceptionInteractions.cs` | `ExceptionOrderCard_MouseDoubleClick`, `CloseExceptionDetails_Click`, `JumpToOrderFulfillment_Click` | ~95 |
| `MainWindow.SettingsInteractions.cs` | `SettingsTextInput_LostFocus` | ~30 |
| `MainWindow.ResponsiveLayout.cs` | `MainWindow_SizeChanged`, `UpdateCashflowTrendCardVisibility` | ~40 |

All targets ≤ 300. Although Fulfillment/Exception/Settings handlers belong to frozen *domains*,
moving them between partials is purely a file relocation that does not alter the handler bodies,
names, or XAML wiring — so frozen runtime behavior is preserved. Each partial keeps the exact `using`
directives it needs; bodies are byte-identical.

### Mechanism B — Merged ResourceDictionaries

A `ResourceDictionary` can `MergedDictionaries` other dictionaries. WPF resolves `StaticResource` by
walking the merged set; **merge order is preserved** so lookup precedence is identical. Cross-dictionary
`StaticResource` references (e.g. a style referencing `PrimaryBrush`) resolve as long as the referenced
key's dictionary is merged **before** the referencing dictionary in the same parent. The split keeps
base brushes/converters first, then component styles, then templates, then nav styles — matching the
existing top-to-bottom declaration order.

#### App.xaml split

`App.xaml` keeps `<Application.Resources>` with a `<ResourceDictionary>` whose `MergedDictionaries`
pull in new files under `Resources/App/`:

| New file | Contents | Est. lines |
|----------|----------|-----------|
| `Resources/App/AppBaseResources.xaml` | brand image, 8 brushes, implicit `Window`/`ScrollBar`/`Button` styles | ~40 |
| `Resources/App/AppInputStyles.xaml` | implicit `TextBox`, `PasswordBox`, `ComboBox` styles | ~165 |
| `Resources/App/AppButtonStyles.xaml` | `NavButtonStyle`, `CardBorderStyle`, `DialogPrimaryButtonStyle`, `DialogSecondaryButtonStyle`, `DialogFieldLabelStyle`, `DialogSectionTitleStyle`, `ChipButtonStyle`, `ChipButtonActiveStyle` | ~150 |
| `App.xaml` (residual) | `<Application.Resources>` + merged-dictionary list only | ~15 |

`CardBorderStyle` references `PanelBrush`/`BorderBrushSoft`; `ChipButtonActiveStyle` is `BasedOn`
`ChipButtonStyle`. Both resolve because base resources merge first. Implicit (key-less) styles like
`Window`/`Button`/`TextBox` apply application-wide identically whether declared inline or in a merged
dictionary.

#### MainWindowResources.xaml split

`MainWindowResources.xaml` becomes a thin shell merging concern dictionaries under `Resources/Main/`:

| New file | Source lines | Est. lines |
|----------|-------------|-----------|
| `Resources/Main/MainSharedResources.xaml` | converters + brushes + `CardShadow` (5–36) | ~35 |
| `Resources/Main/MainComponentStyles.xaml` | text/card/list/toolbar/exception-button/paging/copy/pill/tab styles (38–626) | ~290 |
| `Resources/Main/MainListTemplates.xaml` | `CustomerListItemTemplate`, `OrderListItemTemplate` (627–782) | ~157 |
| `Resources/Main/MainSettingsResources.xaml` | all Settings styles (783–1458) — see frozen note | over 300 → see Risk R-1 |
| `Resources/Main/MainNavigationStyles.xaml` | nav/profile button styles (1460–1854) | ~395 → see Risk R-2 |

The shell `MainWindowResources.xaml` retains its current key set transitively via merged dictionaries,
so `MainWindow.xaml`'s single reference to it is unchanged and all `StaticResource` lookups resolve.

### Mechanism C — UserControl extraction (`MainWindow.xaml`)

A section subtree moves into a `UserControl`; the parent replaces it with `<local:WorkbenchView />`.
The `UserControl` inherits `DataContext` from its placement in the visual tree (no explicit DataContext
set), so all `{Binding ...}` paths, command bindings, and `ConverterParameter`s resolve against the
same `MainViewModel` exactly as before. Event handlers (`Click`, `MouseDoubleClick`) referenced by the
moved XAML must have their code-behind handlers move to the **UserControl's** code-behind — this is the
delicate part and is gated (see Risk R-3).

Because the section `Grid`s are bound by `SectionVisibilityConverter` on `SelectedSection`, the parent
keeps that visibility binding on the hosting element wrapping each extracted control, preserving
navigation/visibility behavior.

---

## Constraints From Workspace Rules (AGENTS.md) — Frozen Domains

`AGENTS.md` freezes specific product surfaces. The user has clarified that **"frozen" means behavior
is frozen, not file location** — a self-contained frozen-page visual subtree MAY be relocated verbatim
into a dedicated `UserControl`, provided behavior, bindings, commands, event routing, animations,
DataContext semantics, visual output, and protected transaction behavior all remain unchanged, and the
relocation requires **no** ViewModel/Service/Data/Core/gateway/cloudfunction/QA-script/documentation
change.

Authorized for mechanical extraction under strict controls (option b):

- **Settings page** (rule 2) — settings section, `SettingsInteractions` handler, Settings resources.
- **Order Fulfillment page** (rule 3) — fulfillment section, fulfillment handlers, fulfillment resources.
- **Exception handling page** (rule 5) — exception section, exception handlers.

Still fully frozen / not edited this run:

- **Login** (rule 1) — separate decision gate (Requirement 8); audit read-only, no edit without a
  separate explicit approval after reporting its plan and validation coverage.

### Absolute protected business boundaries (must remain unchanged regardless of extraction)

- payment callback verification
- automatic transition to paid
- WeChat shipping / fulfillment synchronization
- payment-success-to-fulfillment-reporting transaction behavior
- settings save / validation / runtime-hook behavior
- exception-order behavior
- cloud-sync behavior
- schema / data / API / gateway contracts

### Per-subtree extraction gate (applies to every frozen-page subtree)

Before extracting any frozen-page subtree, the implementation MUST verify ALL of:

1. The subtree is **self-contained** — no `x:Name` inside it is referenced by code-behind outside the
   subtree, and no `ElementName=` binding crosses the subtree boundary (in either direction).
2. Extraction needs **only** verbatim XAML relocation + verbatim handler relocation to the new control's
   code-behind (Mechanism A+C), with no edit to any ViewModel/Service/Data/Core/gateway/cloudfunction/
   QA/doc file.
3. No command, command parameter, binding path, event name/routing, animation value, or visual output
   changes.

If any check fails for a specific subtree, **stop on that subtree and report it as a blocker** rather
than forcing compliance. Cross-control coupling is resolved by choosing a smaller seam or deferring.

---

## Risk Register & Blocker Handling

| ID | Risk | Mitigation / Decision |
|----|------|----------------------|
| R-1 | `MainSettingsResources.xaml` (~675 src lines) exceeds 300 even after isolation; contains frozen Settings styles | Split Settings resources into ≤300 sub-dictionaries by mechanical key grouping (e.g. `MainSettingsResources.Base.xaml`, `.Controls.xaml`, `.Rows.xaml`). Pure relocation of keyed styles — no value change. If any single style block alone > 300 lines, leave intact and report (R-blocker). |
| R-2 | `MainNavigationStyles.xaml` (~395 lines) exceeds 300; individual templates are large | Split by key into ≤300 files (e.g. `MainMenuButtonStyle` ~110, profile styles grouped). If a single `ControlTemplate` block > 300 lines, report as irreducible blocker. |
| R-3 | UserControl extraction (Mechanism C) requires moving event handlers + may hit `x:Name` cross-references between a section and shell/other sections | Before extracting any section, scan for `x:Name` referenced by code-behind or by `ElementName` bindings outside the section. If self-contained → extract. If cross-coupled → choose smaller seam or defer + report. |
| R-4 | `Window.Resources` inline styles in MainWindow.xaml (10–253) are used only by fulfillment combo/input | Move to a merged dictionary `Resources/Main/MainWindowInlineStyles.xaml`; keep merge in `Window.Resources`. Inert relocation. |
| R-5 | Frozen section (Fulfillment/Exception/Settings) XAML extraction could disturb behavior | Authorized under the **per-subtree extraction gate** above. Each frozen subtree is extracted only after passing all three gate checks (self-contained, verbatim-only, no protected-behavior change). Any subtree failing a check is left in place and reported as a blocker requiring a smaller seam or owner follow-up. |

When any file cannot reach ≤300 without rewriting a single indivisible block, the outcome is: **leave
the block intact, record exact file + block + line count as an irreducible blocker** (Requirement 1.5).

---

## Implementation Sequence (Commit Batches)

| Batch | Scope | Files | Validation gate |
|-------|-------|-------|-----------------|
| 1 | Code-behind partial split | `MainWindow.xaml.cs` + 9 new `MainWindow.*.cs` | build + all safe QA + diff review |
| 2 | App.xaml resource split | `App.xaml` + 3 new `Resources/App/*.xaml` | build + all safe QA |
| 3 | MainWindowResources split | `MainWindowResources.xaml` + new `Resources/Main/*.xaml` | build + all safe QA + visual check |
| 4 | MainWindow.xaml inline-resource extraction (R-4) | `MainWindow.xaml` + `Resources/Main/MainWindowInlineStyles.xaml` | build + all safe QA + visual check |
| 5 | MainWindow.xaml **non-frozen** view extraction (Workbench, Inventory, Cashflow, Me, shell) | `MainWindow.xaml` + new `Views/Sections/*.xaml(.cs)` | build + all safe QA + visual check 100%/125% |
| 6 | MainWindow.xaml **frozen** view extraction — Cashflow already covered; Fulfillment, Exception, Settings subtrees (each gated per-subtree) | `MainWindow.xaml` + new `Views/Sections/*.xaml(.cs)` | build + all safe QA + visual check 100%/125% + protected-behavior reasoning |
| 7 | Login audit report (no edit) → await approval | (report only) | n/a |

Batches are committed independently only after their gate passes. Frozen-section XAML extraction
(Fulfillment/Exception/Settings, Batch 6) proceeds only per the per-subtree extraction gate; any
subtree that fails a gate check is left in place and reported as a blocker (R-5).

---

## Validation Strategy

### Automated (every batch, before commit)

```
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-qa-data-status.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-1-workbench-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-2-pipeline-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-4-workbench-logic-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-5-search-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p3-6-navigation-smoke.ps1
git status --short
```

All listed scripts are confirmed present under `tools/qa/`. QA scripts are **read-only here** — never
edited (frozen by `AGENTS.md` and mission scope).

### Visual (Requirement 7)

For each modified visible surface, capture before/after at 100% and 125% scaling where the local
environment can reliably render the WPF app. If a state/scale cannot be reliably reached, record the
limitation explicitly. Purely structural batches (1, 2, partly 3) are not blocked by an unreachable
visual state once build + QA pass; visible batches (4, 5) require the strongest practical evidence
before commit.

### Equivalence reasoning per mechanism

- **A (partials):** single compiled type; IL-equivalent; XAML wiring unchanged.
- **B (merged dictionaries):** identical keys/values, merge order preserved → identical resolution.
- **C (UserControl):** inherited DataContext; identical bindings/handlers relocated verbatim; only applied where subtree is self-contained.

---

## Components and Interfaces

This mission introduces no new runtime components, public interfaces, or APIs. All new files are
structural containers for existing, unchanged UI definitions:

- **Partial code-behind files** (`MainWindow.*.cs`): each is `partial class MainWindow : Window` in
  `namespace Orderly.App.Views`. They expose no new members beyond the relocated `private`/`protected`
  handlers and helpers. The public surface of `MainWindow` is unchanged.
- **ResourceDictionary files** (`Resources/App/*.xaml`, `Resources/Main/*.xaml`): each is a
  `ResourceDictionary` root containing relocated keyed/implicit resources. They are consumed only via
  `MergedDictionaries` from `App.xaml` / `MainWindow.xaml` / `MainWindowResources.xaml`. Resource keys
  (the effective "interface") are unchanged.
- **UserControl subviews** (`Views/Sections/*View.xaml(.cs)`): each is a `UserControl` in
  `namespace Orderly.App.Views.Sections` (xmlns alias `sections:`) hosting a relocated visual subtree.
  Each inherits `DataContext` from its placement in `MainWindow.xaml` (no DataContext is set), so its
  binding "interface" against `MainViewModel` is identical to the original inline subtree. Event
  handlers referenced by the moved XAML are relocated to the control's own code-behind verbatim.

No constructor signatures, DI registrations, or composition wiring change. `MainWindow(MainViewModel)`
remains the single injection point.

## Data Models

This mission defines and modifies **no** data models. All types (`MainViewModel`,
`StringNarrationOrderSummary`, `StringNarrationCashflowHealthDashboardBreakdownItem`, etc.) are
referenced exactly as today from their existing namespaces. No DTO, entity, schema, migration, or
serialization contract is touched. Binding paths against existing model/view-model properties are
preserved byte-for-byte.

## Correctness Properties

The decomposition is correct iff it is **observably equivalent** to the original. The following
properties must hold after every batch:

### Property 1: Build equivalence
`dotnet build Orderly.sln -c Debug` succeeds with no new warnings/errors.
**Validates: Requirements 6.1**

### Property 2: Resource-resolution equivalence
Every `StaticResource`/`DynamicResource` key resolvable before the split is resolvable after, to the
same value (verified by successful build + QA + visual check).
**Validates: Requirements 2.5, 1.2, 1.3**

### Property 3: Binding equivalence
Every `{Binding}` path, command, command parameter, and `ConverterParameter` resolves against the same
`DataContext` as before (verified by navigation/interaction QA + visual).
**Validates: Requirements 3.1, 3.3, 3.5**

### Property 4: Event-routing equivalence
Every XAML-wired handler fires the same relocated method body.
**Validates: Requirements 3.2, 5.1**

### Property 5: Visual equivalence
Rendered output (layout, spacing, color, chart geometry, animation) is unchanged at 100% and 125% where
reachable (Requirement 2).
**Validates: Requirements 2.1, 2.2, 2.3, 2.4**

### Property 6: Line-budget
Every governed UI file is ≤ 300 physical lines, OR is recorded as an irreducible-block blocker
(Requirement 1.5).
**Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5**

## Error Handling

This is a refactor, not new logic, so runtime error handling is unchanged (e.g. `CopyText_Click`'s
existing `try/catch` is moved verbatim). Process-level error handling during implementation:

- **Build failure** after a batch → do not commit; diagnose; either fix the relocation or revert the
  batch; report which.
- **QA gate failure** → do not commit; report failure and whether the diff was preserved or reverted.
- **Gate-check failure on a frozen subtree** (cross-coupling / would require non-UI change) → leave the
  subtree in place; do not force; report as a blocker.
- **Irreducible block > 300 lines** → leave intact; report exact file + block + line count.

## Testing Strategy

No new automated tests are added (this is structural governance; adding tests is out of scope and the
QA scripts are frozen). Verification relies on the existing gates:

- **Automated:** `dotnet build` + the standard QA smoke suite (`run-qa-data-status`, `run-p1-smoke`,
  `run-p3-1`/`p3-2`/`p3-4`/`p3-5`/`p3-6`) run before every commit. These exercise data status,
  workbench, pipeline, workbench-logic, search, and navigation paths that traverse the governed UI.
- **Manual visual:** strongest-practical before/after comparison at 100%/125% for each modified visible
  surface, with explicit recording of any unreachable state/scale (Requirement 7).
- **Diff audit:** every commit's diff is inspected to confirm only intended UI files changed and bodies
  moved verbatim.

## Out of Scope (must not change)

ViewModels, Services, Repositories, `Orderly.Data`, `Orderly.Core`, `Orderly.Infrastructure`,
cloudfunctions, miniprogram, `tools/qa` scripts, documentation, schemas, migrations, gateway
contracts, payment/fulfillment/shipping-sync/cloud-sync behavior, and any visual or interaction
redesign. If a structural split appears to require any such change, STOP and report.
