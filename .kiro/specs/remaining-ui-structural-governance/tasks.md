# Implementation Plan — Remaining UI Structural Governance

> **STATUS NOTE (rebaselined at HEAD `cafd64d`, branch `main`).**
> The original plan in this file was authored against stale baseline HEAD `1bb51a1`
> (`MainWindow.xaml` = 8302 lines). Since then two commits (`拆文件`, `4.8接着拆`) landed and
> completed most of the original Batches 1–6: code-behind partials, App.xaml / MainWindowResources
> merged-dictionary splits, inline-style extraction, and Workbench / Inventory / Cashflow / Settings /
> Fulfillment (toolbar + statistics + **left** order-list workspace) UserControl extraction are all
> already committed. The task list below reflects the **actual remaining** governance work measured
> from the real current HEAD, not the obsolete line ranges.

## Verified Current Baseline (HEAD `cafd64d`, clean tree, build green)

Tracked source files still over budget (UI ≤300 / non-UI ≤500):

| File | Lines | Budget | Type | Disposition |
|------|-------|--------|------|-------------|
| `src/Orderly.App/Views/MainWindow.xaml` | 2780 | 300 | UI | Govern (priority) |
| `src/Orderly.App/ViewModels/MainViewModel.StringNarrationOrders.cs` | 1809 | 500 | non-UI | Assess safe split |
| `src/Orderly.App/Views/LoginView.xaml` | 1708 | 300 | UI | Govern (structure-first) |
| `src/Orderly.App/ViewModels/MainViewModel.SettingsP0.cs` | 1358 | 500 | non-UI | Assess safe split |
| `src/Orderly.App/Views/LoginView.xaml.cs` | 1063 | 300 | UI code-behind | Govern (structure-first) |
| `src/Orderly.App/ViewModels/LoginViewModel.cs` | 923 | 500 | non-UI / auth | Assess safe split |
| `src/Orderly.App/ViewModels/MainViewModel.ExceptionOrders.cs` | 750 | 500 | non-UI | Assess safe split |

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

## Tasks (rebaselined)

- [ ] A. Rebaseline stale spec docs to actual HEAD `cafd64d` (this file + design/requirements notes)
- [ ] B. Extract Fulfillment right detail region into self-contained UserControls (≤300 each)
- [ ] C. Extract Exception page into self-contained UserControls (≤300 each)
- [ ] D. Extract Me/Profile page into a UserControl; bring `MainWindow.xaml` ≤300 (or record blocker)
- [ ] E. Structurally decompose `LoginView.xaml` / `LoginView.xaml.cs` (behavior-preserving)
- [ ] F. Assess & safely split oversized non-UI ViewModels (≤500), preserving all contracts
- [ ] G. Read-only Login/Auth/PIN/Session-Lock security audit → evidence-based hardening (separate commits)
- [ ] H. Final report (Chinese): inventory, commits, exceptions, manual-verification checklist

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
