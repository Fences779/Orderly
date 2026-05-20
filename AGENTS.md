# Agent Instructions & Constraints

## CRITICAL RULES - DO NOT MODIFY

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

## CURRENT WORK IN PROGRESS

### 1. Home Page / MainWindow Refactoring (首页重构)
- **Status**: Pending refactoring. This is the next target.
- **Related Files**:
  - `src/Orderly.App/Views/MainWindow.xaml`
  - `src/Orderly.App/Views/MainWindow.xaml.cs`
  - `src/Orderly.App/ViewModels/MainViewModel.cs`
