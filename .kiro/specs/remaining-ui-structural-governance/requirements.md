# Requirements — Remaining UI Structural Governance

## Introduction

The Orderly repository has completed major non-UI/data-layer line-budget governance and the
`App.xaml.cs` UI-adjacent split (HEAD `1bb51a1` on `main`). The remaining work is to bring the
UI-layer source files (XAML, views, code-behind) into the mandated per-file line budget while
preserving the product's current visual appearance and behavior **exactly**.

This is **structural governance, not redesign**. No visible behavior, layout, styling, binding,
command, event, animation, or frozen functional behavior may change. The work is purely mechanical
decomposition: same-class partial files, merged `ResourceDictionary` splits, and `UserControl`
extractions that are byte-for-byte behavior-preserving.

### Verified Starting State (read-only, HEAD `1bb51a1`, branch `main`, clean tree)

| File | Physical lines | Budget | Status |
|------|---------------|--------|--------|
| `src/Orderly.App/Views/MainWindow.xaml` | 8302 | ≤300 | OVER |
| `src/Orderly.App/Views/Resources/MainWindowResources.xaml` | 1854 | ≤300 | OVER |
| `src/Orderly.App/Views/MainWindow.xaml.cs` | 913 | ≤300 | OVER |
| `src/Orderly.App/App.xaml` | 344 | ≤300 | OVER |
| `src/Orderly.App/Views/LoginView.xaml` | 1708 | ≤300 | OVER (frozen) |
| `src/Orderly.App/Views/LoginView.xaml.cs` | 1063 | ≤300 | OVER (frozen) |

### Hard Line-Budget Rules

- UI / XAML / View / code-behind source files: **≤ 300 physical lines** per file.
- Non-UI logic / Service / Repository / ViewModel / Model source files: ≤ 500 physical lines (out of scope here; must not be modified).
- Markdown/documentation is exempt from forced splitting.
- Physical line counts are measured consistently via `(Get-Content <path>).Count`.

### Workspace Rule Precedence

The workspace `AGENTS.md` rules take precedence. Relevant constraints carried into this spec:
- UI Modification Ban: only mechanical, behavior-preserving structural splits are authorized by this spec; no visual/interaction redesign.
- Minimal scope escalation: if a split would require touching ViewModels, Data, Core, cloudfunctions, miniprogram, tools/qa, or docs, STOP and report before expanding scope.
- Login is the strictest frozen boundary (auth/PIN/session/encryption).

---

## Requirements

### Requirement 1 — Line-Budget Compliance for Non-Login UI Files

**User Story:** As the repository maintainer, I want every non-Login UI/XAML/code-behind source file to be at or under 300 physical lines, so that the UI layer meets the same structural-governance standard as the rest of the codebase.

#### Acceptance Criteria

1. WHEN governance completes THEN `src/Orderly.App/Views/MainWindow.xaml.cs` and every new `MainWindow*.cs` code-behind partial SHALL be ≤ 300 physical lines.
2. WHEN governance completes THEN `src/Orderly.App/App.xaml` and every new app-level resource dictionary file SHALL be ≤ 300 physical lines.
3. WHEN governance completes THEN `src/Orderly.App/Views/Resources/MainWindowResources.xaml` and every new resource dictionary derived from it SHALL be ≤ 300 physical lines.
4. WHEN governance completes THEN `src/Orderly.App/Views/MainWindow.xaml` and every new extracted view/UserControl XAML and its code-behind SHALL be ≤ 300 physical lines.
5. IF a single indivisible block (one template/style/visual subtree) cannot be reduced below 300 lines without rewriting behavior-bearing content THEN the system SHALL leave that block intact and report it as an explicit irreducible blocker rather than rewriting it.

### Requirement 2 — Visual Appearance Preservation

**User Story:** As a product owner, I want the application to look pixel-identical before and after governance, so that users perceive no change.

#### Acceptance Criteria

1. WHEN any UI file is split THEN existing colors, typography, borders, corner radii, shadows, iconography, and resource keys SHALL remain unchanged.
2. WHEN any UI file is split THEN current visual layout, hierarchy, and spacing SHALL remain unchanged at both 100% and 125% display scaling.
3. WHEN chart-related code or resources are split THEN chart geometry, labels, clipping, tooltip behavior, and rendered output SHALL remain unchanged.
4. WHEN any UI file is split THEN existing animations and transitions SHALL remain unchanged.
5. WHEN resource dictionaries are split THEN no resource key SHALL be renamed, no resource value SHALL be changed, and merged-dictionary lookup precedence SHALL be preserved.

### Requirement 3 — Interaction & Binding Preservation

**User Story:** As a developer, I want all bindings, commands, and event handlers to behave identically after governance, so that no interactive functionality regresses.

#### Acceptance Criteria

1. WHEN view sections are extracted THEN existing XAML binding paths SHALL be preserved.
2. WHEN view sections are extracted THEN existing event handler names and routing SHALL be preserved.
3. WHEN view sections are extracted THEN existing commands and command parameters SHALL be preserved.
4. WHEN view sections are extracted THEN existing keyboard, pointer, double-click, selection, focus, popup, and navigation behavior SHALL be preserved.
5. WHEN view sections are extracted THEN DataContext semantics SHALL be preserved and no new ViewModel SHALL be introduced.
6. IF extracting a subtree would require rewriting behavior or changing cross-control coupling (e.g., named-element/event dependencies) THEN the system SHALL choose a smaller structural seam or report the blocker instead of rewriting.

### Requirement 4 — Frozen Functional Behavior Preservation

**User Story:** As the system owner, I want all behavior surfaced through the UI to remain unchanged, so that no business or integration behavior regresses.

#### Acceptance Criteria

1. WHEN any UI file is governed THEN settings save/validation/runtime-hook behavior SHALL remain unchanged.
2. WHEN any UI file is governed THEN order/fulfillment, exception-order, payment-callback verification, auto-transition-to-paid, WeChat shipping sync, payment-success-to-fulfillment loop, and cloud-sync behavior SHALL remain unchanged.
3. WHEN any UI file is governed THEN login/authentication/PIN/session-lock behavior SHALL remain unchanged.
4. WHEN governance requires changing any ViewModel, Service, Repository, Data, Core, cloudfunction, miniprogram, QA script, or documentation file THEN the system SHALL STOP at that boundary and report rather than making the change.

### Requirement 5 — Behavior-Preserving Mechanical Movement

**User Story:** As a reviewer, I want all moved code to be verbatim relocations, so that diffs are trivially auditable as behavior-preserving.

#### Acceptance Criteria

1. WHEN a method is relocated to a partial class file THEN its body SHALL be moved verbatim with no change to logic, constants, animation values, command calls, or event routing.
2. WHEN resources or XAML subtrees are relocated THEN their content SHALL be moved verbatim with only the minimal wrapping required for the new file (e.g., a `ResourceDictionary` root or `UserControl` shell).
3. WHEN files are added THEN they SHALL belong to the `Orderly.App` UI project only and SHALL be wired via partial classes, merged dictionaries, or control references — never by altering business logic.

### Requirement 6 — Validation After Every Batch

**User Story:** As the maintainer, I want each coherent batch validated before commit, so that regressions are caught immediately.

#### Acceptance Criteria

1. WHEN a coherent batch is implemented THEN `dotnet build Orderly.sln -c Debug` SHALL succeed before commit.
2. WHEN a coherent batch is implemented THEN all safely-runnable listed QA smoke scripts SHALL be executed and pass before commit.
3. IF a listed QA script is absent or cannot be run safely locally THEN the system SHALL state this clearly and run all remaining safe gates.
4. WHEN a batch fails validation THEN the system SHALL NOT commit it and SHALL report the failure and whether the diff was preserved or reverted.

### Requirement 7 — Visual Validation Evidence (amended)

**User Story:** As a product owner, I want the strongest practical before/after visual evidence for any visible change, while not blocking purely structural work when a display state cannot be reliably reached.

#### Acceptance Criteria

1. WHERE the environment can perform it reliably, baseline and post-change visual comparison SHALL be performed at both 100% and 125% display scaling for every modified visible surface.
2. IF a display scale or UI state cannot be safely or reliably reached THEN the system SHALL record the limitation explicitly rather than claiming validation was performed.
3. WHEN a change is purely structural and non-visible (e.g., code-behind partial split, or a resource-dictionary split that preserves identical merge order and keys) THEN the inability to reach a visual state SHALL NOT automatically block the work, PROVIDED the build and applicable QA gates pass.
4. WHEN a modified surface is visible THEN the strongest practical before/after evidence available in the environment SHALL be captured before that batch is committed.

### Requirement 8 — Login Family Audit & Approval Gate (amended)

**User Story:** As the security owner, I want the Login UI treated as the strictest boundary, so that authentication is never modified without explicit, separately-approved review.

#### Acceptance Criteria

1. WITHIN this mission the system MAY fully audit `LoginView.xaml` and `LoginView.xaml.cs` (read-only) to produce a decomposition plan, validation-coverage assessment, and risk assessment.
2. The system SHALL NOT implement any Login-family modification automatically within the same execution run.
3. WHEN non-Login UI governance is complete or blocked THEN the system SHALL report the Login decomposition plan, available validation coverage, and risk assessment, and SHALL then WAIT for explicit user approval before modifying any Login file.
4. The system SHALL NOT force Login compliance at the cost of authentication risk.

### Requirement 9 — Git Discipline (Local Only)

**User Story:** As the maintainer, I want clean, auditable local commits and no remote pushes, so that I retain full control over what lands.

#### Acceptance Criteria

1. WHEN governance begins THEN the working tree SHALL be confirmed clean.
2. WHEN a batch is committed THEN only the intended UI files SHALL be included in that commit.
3. WHEN a batch is committed THEN the commit message SHALL clearly describe the implemented UI scope (e.g., `refactor(ui): split main window chart code-behind`).
4. The system SHALL NOT push to any remote.
5. WHEN a diff is inspected before commit THEN the system SHALL verify only intended UI files changed.

### Requirement 10 — Final Reporting

**User Story:** As the maintainer, I want a complete implementation report, so that I can audit exactly what changed and what remains.

#### Acceptance Criteria

1. WHEN the mission concludes THEN the system SHALL report: starting state; chosen decomposition sequence; each commit (hash, message, file list, affected surface, validation, visual verification); final UI line-budget inventory (path, final line count, PASS / REMAINS OVER BUDGET + blocker); automated and manual validation results at 100%/125% (or recorded limitations); protected-boundary confirmation; and remaining work with exact blockers and required approvals (including the Login decision awaiting approval).


---

## Rebaseline Note (HEAD `cafd64d`)

The "Verified Starting State" table above was captured at the obsolete baseline HEAD `1bb51a1`.
Two later commits (`拆文件`, `4.8接着拆`) completed most of the original plan. The actual current
state at HEAD `cafd64d` (clean tree, build green) is:

| File | Then (1bb51a1) | Now (cafd64d) | Budget | Status |
|------|----------------|---------------|--------|--------|
| `Views/MainWindow.xaml` | 8302 | 2780 | ≤300 | still OVER (Fulfillment detail / Exception / Me-Profile inline) |
| `Views/Resources/MainWindowResources.xaml` | 1854 | 17 | ≤300 | PASS (now a merged-dictionary shell) |
| `Views/MainWindow.xaml.cs` | 913 | 93 | ≤300 | PASS (split into partials) |
| `App.xaml` | 344 | 14 | ≤300 | PASS (merged-dictionary shell) |
| `Views/LoginView.xaml` | 1708 | 1708 | ≤300 | still OVER (structure-first authorized) |
| `Views/LoginView.xaml.cs` | 1063 | 1063 | ≤300 | still OVER (structure-first authorized) |

Per the latest user authorization, Login/Auth/PIN/Session-Lock is no longer absolutely frozen:
behavior-preserving structural decomposition is authorized first, followed by a separate read-only
security audit and evidence-based hardening in independent commits. Requirement 8's "audit-only,
await approval" gate is therefore superseded for this run.
