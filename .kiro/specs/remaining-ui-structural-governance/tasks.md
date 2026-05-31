# Implementation Plan — Remaining UI Structural Governance

> **FINAL-STATE NOTE (governance complete; corrected on `main` after `ccb453a`).**
> All governance tasks below are now complete. The earlier history of this file tracked the work from
> stale baseline HEAD `1bb51a1` (`MainWindow.xaml` = 8302 lines) through the rebaseline at `cafd64d`.
> Since the rebaseline, the full UI extraction series landed: code-behind partials, App.xaml /
> MainWindowResources merged-dictionary splits, inline-style extraction, Workbench / Inventory /
> Cashflow / Settings / Fulfillment (toolbar + statistics + left order-list) plus the Fulfillment
> right detail region, the Exception page, the Me/Profile page, and the Login sign-in / recovery /
> account-management / owner-create / create-member surface extraction are all committed. An
> independent final review then identified two release blockers (cumulative trailing whitespace and a
> Login recent-account popup surface-guard regression), both resolved by dedicated correction commits
> (`327cb82`, `ccb453a`). At that corrected state every governed tracked source file is within budget
> (UI ≤300 / non-UI ≤500) and the oversized count is 0.
>
> **Push remains prohibited** until a post-correction independent review and the user's manual runtime
> verification both succeed.

## Verified Final Baseline (post-correction on `main` after `ccb453a`, clean tree, build green)

Tracked source files over budget (UI ≤300 / non-UI ≤500): **none — oversized count is 0.**

All files that were previously over budget have been governed:

| File | Then (cafd64d) | Now | Budget | Status |
|------|----------------|-----|--------|--------|
| `src/Orderly.App/Views/MainWindow.xaml` | 2780 | ≤300 | 300 | PASS (detail/exception/me-profile extracted) |
| `src/Orderly.App/ViewModels/MainViewModel.StringNarrationOrders.cs` | 1809 | ≤500 | 500 | PASS (safely split) |
| `src/Orderly.App/Views/LoginView.xaml` | 1708 | ≤300 | 300 | PASS (surface UserControls extracted) |
| `src/Orderly.App/ViewModels/MainViewModel.SettingsP0.cs` | 1358 | ≤500 | 500 | PASS (safely split) |
| `src/Orderly.App/Views/LoginView.xaml.cs` | 1063 | ≤300 | 300 | PASS (surface partials extracted) |
| `src/Orderly.App/ViewModels/LoginViewModel.cs` | 923 | ≤500 | 500 | PASS (safely split) |
| `src/Orderly.App/ViewModels/MainViewModel.ExceptionOrders.cs` | 750 | ≤500 | 500 | PASS (safely split) |

The current largest governed files are `Views/Sections/MeProfileView.xaml` (299, UI budget 300) and
`Orderly.Data/Services/StringNarrationGatewayOrderService.cs` (499, non-UI budget 500) — both within
budget.

`scratch/` and `src/scratch/` are gitignored local scratch — NOT governance targets.

## Remaining MainWindow.xaml inline regions (corrected map)

- **Fulfillment right detail region** (~lines 242–1305): `DetailPanel`, 6-node Status Stepper,
  order overview, product snapshot, production order, shipping/logistics inputs, bottom action
  buttons, detail loading overlay + unified work-area overlay + initialization overlay.
  Coupled handlers (all self-contained to the region): `CloseDetails_Click`,
  `StepperNode_MouseLeftButtonDown`, `FulfillmentInput_KeyDown`, `QuickFulfillmentUpdate_Click`;
  named elements `DetailPanel`, `Input_FulfillmentCarrier`.
- **Exception page** (~lines 1307–2452): filter bar, exception card template, collapsed/expanded
  lists, paging, `ExceptionDetailPanel`, operation log, action buttons. Coupled handlers:
  `ExceptionOrderCard_MouseDoubleClick`, `CloseExceptionDetails_Click`, `JumpToOrderFulfillment_Click`,
  `ContactCustomer_Click`, `CopyText_Click`.
- **Me/Profile "我的" page** (~lines 2457–2763): profile card, staff list, password/PIN security card.
- `Popup_CopyToast` / `Text_CopyToast`: window-level shared, stay in `MainWindow`.

## Tasks (rebaselined) — all complete

- [x] A. Rebaseline stale spec docs to actual HEAD `cafd64d` (this file + design/requirements notes)
- [x] B. Extract Fulfillment right detail region into self-contained UserControls (≤300 each)
- [x] C. Extract Exception page into self-contained UserControls (≤300 each)
- [x] D. Extract Me/Profile page into a UserControl; bring `MainWindow.xaml` ≤300 (or record blocker)
- [x] E. Structurally decompose `LoginView.xaml` / `LoginView.xaml.cs` (behavior-preserving)
- [x] F. Assess & safely split oversized non-UI ViewModels (≤500), preserving all contracts
- [x] G. Read-only Login/Auth/PIN/Session-Lock security audit → evidence-based hardening (separate commits)
- [x] H. Final report (Chinese): inventory, commits, exceptions, manual-verification checklist

### Post-review correction round (release-gate fixes)

- [x] I. Remove cumulative trailing whitespace flagged by `git diff --check origin/main..HEAD`
      (commit `327cb82`).
- [x] J. Restore the sign-in recent-account popup active-surface guard lost during the Login surface
      extraction, and remove the two proven-dead parent `BtnOpen*` handlers (commit `ccb453a`).
- [x] K. Align this spec's final-state narration with the actual corrected repository state (this
      commit).

## Standard per-stage validation gate

```
git status --short --branch
dotnet build .\Orderly.sln -c Debug
git diff --check
```

Plus any safe, non-destructive, directly-relevant existing probe under `tools/qa/` that does not
mutate business data or QA baselines.

## Hard rules (carried from requirements & AGENTS.md, reconciled with latest user authorization)

- Every moved member/resource/subtree is relocated verbatim; no logic, value, binding, command,
  event, or animation change.
- New files belong to `Orderly.App` UI project only; wiring via partial classes, merged dictionaries,
  or `UserControl` references; each extracted `UserControl` merges `MainWindowResources.xaml` +
  `MainWindowInlineStyles.xaml` in its own `UserControl.Resources` (established convention).
- Login/Auth/PIN/Session-Lock: structure-first (behavior-preserving) is authorized; security changes
  are separate, evidence-based, low-regression commits AFTER structural work. No incompatible auth
  migration, no credential reset/deletion, no lockout risk.
- Absolutely frozen (read-only): payment callback signature verification, auto-transition-to-paid,
  WeChat shipping sync, payment-success-to-fulfillment-reporting loop, and related
  gateway/cloudfunction/data-contract/state-transition behavior.
- Do not push to any remote. Commit only intended files per stage after diff review.
- Any file that cannot reach budget without rewriting a single indivisible block: leave intact and
  report as an irreducible blocker.
