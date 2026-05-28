# Agent Instructions & Constraints

## CRITICAL RULES - DO NOT MODIFY

### -1. Explicit Implementation Keyword Gate
- **Status**: Active and strict.
- **Constraint**: Unless the user explicitly gives a standalone implementation trigger such as `施工`, `修改`, `改代码`, `implement`, `fix`, or `apply changes`, Codex must stay in discussion mode only.
- **Required Behavior**: Without an explicit implementation trigger, **DO NOT** modify files, edit code, write patches, run change-causing commands, or perform implementation by implication.
- **Default Interpretation**: Requests such as `看看`, `检查`, `分析`, `排查`, `评估`, `方案`, `怎么改`, or similar wording must be treated as discussion-only, even if the likely technical solution is already clear.
- **Escalation Rule**: When the user's intent is ambiguous, resolve it as non-implementation by default and discuss scope, goal, and approach first.

### -1.1 Discussion-First Workflow
- **Status**: Active and strict.
- **Default Mode**: Codex must default to discussion mode for every new user request unless the user explicitly provides an implementation trigger.
- **Required Sequence**:
  1. The user raises a requirement or question.
  2. Codex responds with analysis, answers, and/or a proposed change plan only.
  3. Codex confirms the user's actual intent, scope, and boundaries.
  4. Codex explicitly asks whether it may begin modification.
  5. Codex may start implementation only after the user gives an explicit implementation trigger such as `施工`, `修改`, `改代码`, `implement`, `fix`, or `apply changes`.
- **Constraint**: Codex must not infer permission to modify code from context, momentum, prior similar tasks, or solution clarity.

### -1.2 Post-Change Validation Workflow
- **Status**: Active by default after every approved code change.
- **Required Behavior After Modification**:
  - Automatically check for compile or build errors using the most relevant local project command.
  - Automatically launch the most relevant local preview target when a preview entry exists and can be started safely.
  - Return a brief acceptance checklist telling the user what to verify.
- **Failure Handling**: If build or preview cannot run, Codex must state the real blocker and provide the exact command the user should run locally.

### -1.3 Minimal Scope Escalation Rule
- **Status**: Active and strict.
- **Constraint**: Changes must stay within the smallest necessary scope to solve the approved task.
- **Required Behavior**:
  - If the fix appears to require touching additional files, modules, UI surfaces, shared state, services, or adjacent subsystems beyond the initially expected scope, stop and ask the user before expanding.
  - Do not proactively widen the refactor surface just because a broader cleanup may be technically beneficial.
- **Escalation Note**: The user may explicitly approve broader scope or higher reasoning depth before Codex continues.

### 0. UI Modification Ban
- **Status**: Strict default restriction.
- **Constraint**: **DO NOT** modify any UI-related content by default. Codex is strictly forbidden from changing layout, styles, bindings, visuals, interaction flow, UI-adjacent logic, or UI interfaces/APIs unless the user explicitly and unambiguously requests that exact UI change.
- **Scope**: This applies to all UI surfaces, UI code, and UI interfaces in the repo, not just the pages listed below.
- **Priority**: This is a top-level rule. It overrides any general implementation, cleanup, refactor, or optimization request whenever the change would touch UI code.

### 1. Login Page (登录页)
- **Status**: Completed by the USER.
- **Constraint**: **DO NOT** modify, refactor, or touch any code, layout, styles, or logic related to the Login Page (登录页).
- **Related Files**:
  - `src/Orderly.App/Views/LoginView.xaml`
  - `src/Orderly.App/Views/LoginView.xaml.cs`
  - `src/Orderly.App/ViewModels/LoginViewModel.cs`
  - Any other files or views directly impacting the Login Page UI/UX, unless explicitly requested by the user.

### 2. Settings Page (设置页)
- **Status**: Closed / accepted by the USER.
- **Constraint**: **DO NOT** modify, refactor, polish, restructure, or touch any settings page code, layout, styles, bindings, commands, persistence logic, or related runtime behavior unless the user explicitly asks for a settings-page change.
- **Related Files**:
  - `src/Orderly.App/Views/MainWindow.xaml` settings section only
  - `src/Orderly.App/Views/MainWindow.xaml.cs`
  - `src/Orderly.App/ViewModels/MainViewModel.SettingsP0.cs`
  - `src/Orderly.App/ViewModels/MainViewModel.SettingsP1.cs`
  - `src/Orderly.Core/Models/AppPreferences.cs`
  - `src/Orderly.Core/Models/AppSettingKeys.cs`
  - `src/Orderly.Core/Repositories/IAppSettingRepository.cs`
  - `src/Orderly.Data/Repositories/AppSettingRepository.cs`
  - `miniprogram/pages/settings/settings.js`
  - `miniprogram/pages/settings/settings.wxml`
  - `miniprogram/pages/settings/settings.wxss`
  - `miniprogram/pages/settings/settings.json`
  - Any other files or views directly impacting Settings Page UI/UX, settings persistence, settings commands, or settings runtime behavior, unless explicitly requested by the user.

### 3. Order Fulfillment Page (订单履约页)
- **Status**: Closed / accepted by the USER.
- **Constraint**: **DO NOT** modify, refactor, polish, restructure, or touch any order fulfillment page code, layout, styles, bindings, commands, interaction flow, page state, or related runtime behavior unless the user explicitly asks for an order-fulfillment-page change.
- **Related Files**:
  - `src/Orderly.App/Views/MainWindow.xaml` order fulfillment section only
  - `src/Orderly.App/Views/MainWindow.xaml.cs` fulfillment-page handlers only
  - `src/Orderly.App/ViewModels/MainViewModel.StringNarrationOrders.cs`
  - `src/Orderly.App/ViewModels/MainViewModel.StringNarrationOrders.Bindings.cs`
  - `src/Orderly.App/Converters/FulfillmentStatusLabelConverter.cs`
  - `src/Orderly.App/Views/Resources/MainWindowResources.xaml` fulfillment-related resources only
  - Any other files or views directly impacting Order Fulfillment Page UI/UX, bindings, commands, page state, or fulfillment-page runtime behavior, unless explicitly requested by the user.

### 4. Order Fulfillment Backend Fields (订单履约后端字段)
- **Status**: Completed by the USER.
- **Constraint**: **DO NOT** modify, refactor, rename, remove, or otherwise touch any completed backend fields for order fulfillment unless the user explicitly asks for that area.
- **Related Files**:
  - Any backend models, DTOs, entities, mappings, services, repositories, migrations, or API contracts directly related to order fulfillment backend fields.

### 5. Exception Handling Page (异常处理页 / 标记异常处理页)
- **Status**: Temporarily complete.
- **Constraint**: **DO NOT** modify, refactor, polish, restructure, or touch any exception-handling-page code, layout, styles, bindings, commands, interaction flow, page state, or related runtime behavior unless the user explicitly asks for that area.
- **Note**: This page is temporarily considered closed for normal implementation work.

## CURRENT WORK IN PROGRESS

### 0. Current Delivery Scope
- **Status**: All major areas except the Workbench are considered complete for now.
- **Constraint**: Unless the user explicitly says otherwise, default active scope is the Workbench only.

### 1. Home Page / MainWindow Refactoring (首页重构)
- **Status**: Pending refactoring. This is the next target.
- **Related Files**:
  - `src/Orderly.App/Views/MainWindow.xaml`
  - `src/Orderly.App/Views/MainWindow.xaml.cs`
  - `src/Orderly.App/ViewModels/MainViewModel.cs`
