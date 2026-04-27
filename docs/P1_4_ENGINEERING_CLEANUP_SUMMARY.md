# P1.4 Engineering Cleanup Summary

## 1. 本轮目标

P1.4 只做工程整理和安全重构，不增加新业务功能，不改视觉，不处理 125% 缩放，不做 Final Visual QA。

本轮重点：

- 降低 `MainViewModel` 单文件复杂度
- 降低 `MainWindow.xaml` 结构复杂度
- 保持 P1.3 主链路、绑定、命令名、AutomationId 和 QA 能力不变
- 为 P2 后续演进提供更清晰的代码落点

## 2. 做了哪些拆分

### MainViewModel

原始 `src/Orderly.App/ViewModels/MainViewModel.cs` 约 `1488` 行，现拆成：

- `src/Orderly.App/ViewModels/MainViewModel.cs`
- `src/Orderly.App/ViewModels/MainViewModel.StatusProperties.cs`
- `src/Orderly.App/ViewModels/MainViewModel.Selection.cs`
- `src/Orderly.App/ViewModels/MainViewModel.SearchFilters.cs`
- `src/Orderly.App/ViewModels/MainViewModel.Loading.cs`
- `src/Orderly.App/ViewModels/MainViewModel.CustomerCommands.cs`
- `src/Orderly.App/ViewModels/MainViewModel.OrderCommands.cs`
- `src/Orderly.App/ViewModels/MainViewModel.NoteCommands.cs`
- `src/Orderly.App/ViewModels/MainViewModel.FollowUpCommands.cs`
- `src/Orderly.App/ViewModels/MainViewModel.DealCommands.cs`
- `src/Orderly.App/ViewModels/MainViewModel.CommandInfrastructure.cs`

同时新增 2 个纯 helper：

- `src/Orderly.App/ViewModels/Helpers/StatusLabelHelper.cs`
- `src/Orderly.App/ViewModels/Helpers/FollowUpDateHelper.cs`

### MainWindow.xaml

原始 `src/Orderly.App/Views/MainWindow.xaml` 约 `1350` 行。

本轮将以下内容下沉到资源字典：

- Window 级 Converter / Brush / keyed Style
- 客户列表项模板
- 订单列表项模板

新增资源文件：

- `src/Orderly.App/Views/Resources/MainWindowResources.xaml`

整理后：

- `src/Orderly.App/Views/MainWindow.xaml` 约 `1004` 行
- 结构资源集中到 `MainWindowResources.xaml` 约 `372` 行

## 3. MainViewModel 如何变清晰

- 根文件只保留依赖、集合、`[ObservableProperty]` 状态和构造函数，稳定外壳更清楚。
- 选择同步、加载、筛选、命令、UI 反馈基础设施分到独立 partial，定位问题不再需要在一个超长文件里来回跳。
- `StatusLabelHelper` 收口客户优先级、Deal 阶段、FollowUp 状态等纯文案逻辑。
- `FollowUpDateHelper` 收口 Pending/Overdue/日期判断，避免筛选和命令可执行条件里重复散落。
- 没有改任何公开属性名、`RelayCommand` 生成命令名、现有绑定名。

## 4. MainWindow.xaml 如何变清晰

- 共享资源不再堆在 `Window.Resources` 顶部，主窗口文件更聚焦布局本身。
- 客户列表和订单列表的重型 `DataTemplate` 下沉到资源字典，主文件阅读路径更短。
- 仍保留现有布局、颜色、间距、圆角、按钮视觉、AutomationId 和 `RelativeSource AncestorType=Window` 命令绑定。
- 本轮没有冒险拆 `UserControl`，避免打断窗口级 DataContext、选中联动和 code-behind Click 桥接。

## 5. 没有做哪些事

- 没有新增业务功能
- 没有做 UI 视觉改版
- 没有处理 125% 缩放
- 没有改 `FloatingWindowViewModel`
- 没有大改 `DatabaseInitializer.cs`
- 没有删除 QA Mode / QA Data Maintenance
- 没有删除 ActivityLog 链路
- 没有用 `IOrderService` 替换 `IOrderRepository`
- 没有改 `MerchantOrder` / `OrderListItem`
- 没有把业务逻辑挪进 code-behind
- 没有把 Dialog / Service 再包装成新架构层

## 6. 保留的关键链路

- 订单主链路保持不变：`IOrderRepository -> MerchantOrder -> OrderListItem`
- `MainWindow.xaml` 现有 Binding 名保持不变
- `RelayCommand` 现有命令名保持不变
- 关键 AutomationId 保持不变：
  - `Tab_Dashboard`
  - `Tab_CustomerOrder`
  - `Tab_Scripts`
  - `Tab_Settings`
  - `Btn_AddOrder`
  - `Btn_AddNote`
  - `Input_SearchKeyword`
  - `Btn_ClearFilters`
  - `Chip_TodayFollowUp`
  - `Chip_OverdueFollowUp`
  - `Chip_TomorrowFollowUp`
  - `Chip_PendingOrders`
  - `Chip_WonOrders`
  - `Btn_ChangeCustomerStatus`
  - `Btn_ChangeOrderStatus`
  - `Btn_ChangeDealStage`
- QA 预置动态项依然通过 `RemoteId` 暴露给 UIA

## 7. 回归验证结果

### Build

- `dotnet build Orderly.sln -c Debug`
- 结果：`0 Warning / 0 Error`

### QA 数据命令

已实际执行：

- `Orderly.App.exe --qa-data-status`
- `Orderly.App.exe --reset-qa-data`
- `Orderly.App.exe --clear-qa-data`
- `Orderly.App.exe --reset-qa-data`

观察结果：

- `status` 可正常输出各表 QA 计数
- `reset` 不会无限增长，重置后稳定回到：
  - Customers `3`
  - Orders `3`
  - Deals `3`
  - FollowUps `4`
  - Notes `3`
  - PriceAdjustments `2`
  - ActivityLogs `10`
- `clear` 会保留仍被非 QA 记录引用的 QA 父记录，这是当前安全设计，不是失败

### UIA / 实际运行

已实际运行 `--qa-mode` 并做 UIA smoke：

- 直接进入主窗口成功
- 关键导航 / 搜索 /筛选 AutomationId 存在
- `AddCustomerDialog` / `AddOrderDialog` / `AddNoteDialog` 可正常打开和关闭
- `Chip_TodayFollowUp` / `Chip_OverdueFollowUp` / `Chip_TomorrowFollowUp` / `Chip_PendingOrders` / `Chip_WonOrders` 运行时可见
- `Input_SearchKeyword`、`Btn_ClearFilters`、QA 预置客户/订单项可在运行态定位

已实际做一轮带 QA 标记的临时写入验证：

- 通过 UIA 实际保存临时客户
- 通过 UIA 实际保存临时订单
- 通过 UIA 实际保存临时备注
- 随后 `--qa-data-status` 计数增至：
  - Customers `4`
  - Orders `4`
  - Deals `3`
  - FollowUps `4`
  - Notes `4`
  - PriceAdjustments `2`
  - ActivityLogs `13`
- 最后再次执行 `clear + reset`，库恢复到稳定 QA 基线

### 本轮验证边界

本轮没有做以下“实际状态变更型”端到端 UIA 点击：

- FollowUp 完成 / 延期 / 取消
- Deal 阶段推进
- 客户状态切换
- 订单状态切换

这些项本轮做了：

- 命令、Binding、AutomationId 保留验证
- 相关按钮运行时存在验证
- 相关服务与命令代码路径未改语义的代码审查

## 8. 后续建议

- P2 前如果继续整理，可优先收口 `MainWindow.xaml` 里重复的空态显隐样式和 `ListBox` 显隐样式。
- 如果后续再拆 UI，先消除 `MainWindow.xaml.cs` 的 Click 桥接和 `AncestorType=Window` 依赖，再考虑 `UserControl`。
- `MainViewModel` 下一步可以继续把命令 guard 和筛选选项构建提炼成更小的纯函数，但不建议现在引入新的 service 层。
