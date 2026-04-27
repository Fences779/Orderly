# P1.4.1 Engineering Cleanup Summary

## 1. 本轮目标

P1.4.1 只做工程小清理和 UIA 回归补强：

- 收口 `MainWindow.xaml.cs` 冗余 click 桥接
- 提炼 `MainWindow.xaml` 中重复的空态 / 显隐样式
- 补强客户状态 / 订单状态及 P1.3 关键链路回归

本轮没有新增业务功能，没有做视觉改版，没有处理 125% 缩放，没有做 Final Visual QA。

## 2. MainWindow.xaml.cs 做了什么收口

涉及文件：

- `src/Orderly.App/Views/MainWindow.xaml.cs`
- `src/Orderly.App/Views/MainWindow.xaml`

本轮调整：

- `AddCustomerButton` 改回直接 `Command="{Binding AddCustomerCommand}"`
- `AddOrderButton` 改回直接 `Command="{Binding AddOrderCommand}"`
- 删除对应的 `AddCustomerButton_Click` / `AddOrderButton_Click`
- 删除不再需要的通用 `ExecuteViewModelCommand`
- 将 6 个 `QuickFilter_*_Click` 收口为 1 个 `QuickFilter_Click`
- 通过 `Button.Tag -> QuickFilterKind` 映射统一转发到 `SetQuickFilter`

结果：

- code-behind 只保留 UI 层筛选转发
- 没有把任何保存、状态更新、仓储调用或业务逻辑写进 code-behind
- 没有改动任何 ViewModel 业务命令名

## 3. MainWindow.xaml / Resources 做了什么提炼

涉及文件：

- `src/Orderly.App/Views/MainWindow.xaml`
- `src/Orderly.App/Views/Resources/MainWindowResources.xaml`

本轮新增资源：

- `HasItemsEmptyStateTextStyle`
- `HasItemsListBoxStyle`
- `StatusMessageTitleTextStyle`

本轮提炼内容：

- 客户列表 / 订单列表的 EmptyState `TextBlock` 显隐样式抽到资源
- 客户列表 / 订单列表的 `ListBox` 显隐样式抽到资源
- 两处重复的状态消息标题 `TextBlock.Style` 抽到资源

约束保持不变：

- 不改颜色
- 不改布局
- 不改间距
- 不改圆角
- 不改字体大小
- 不改视觉层级

## 4. 保留的 code-behind 及原因

当前保留：

- `QuickFilter_Click`
- `SetQuickFilter`

保留原因：

- 当前 ViewModel 只有 `SelectedQuickFilter` 和 `QuickFilterOptions`，没有现成的 QuickFilter 命令入口
- 这里仍然只是 UI Chip 到 `SelectedQuickFilter` 的选择映射，不承载业务逻辑
- 继续保留 UI 层桥接，比引入新的命令面或并行状态源风险更低

## 5. 是否保留全部 Binding / Command / AutomationId

是。

本轮保持不变：

- 现有 Binding 名
- 现有 Command 名
- 现有 `AutomationProperties.AutomationId`
- 现有 `x:Name`

重点确认仍在：

- `Input_CustomerStatus`
- `Btn_ChangeCustomerStatus`
- `Input_OrderStatus`
- `Btn_ChangeOrderStatus`
- `Btn_AddCustomer`
- `Btn_AddOrder`
- `Btn_AddNote`
- `Input_SearchKeyword`
- `Btn_ClearFilters`
- `Chip_TodayFollowUp`
- `Chip_OverdueFollowUp`
- `Chip_TomorrowFollowUp`
- `Chip_PendingOrders`
- `Chip_WonOrders`

## 6. 状态切换回归结果

实际验证方式：

- UIA 运行态验证
- SQLite 持久化核对
- `ActivityLog` 最新记录核对

说明：

- 数据库核对是服务级 / 持久化验证，不等于 UI 视觉验证

实际结果：

- `Input_CustomerStatus` / `Btn_ChangeCustomerStatus` 可被 UIA 找到
- `Input_OrderStatus` / `Btn_ChangeOrderStatus` 可被 UIA 找到
- 实际选中 `[P1.3_QA] 客户-A` 后执行客户状态切换：
  - `Active(0) -> Dormant(1)`
- 实际选中 `[P1.3_QA] 订单-待处理` 后执行订单状态切换：
  - `PendingFollowUp(3) -> PendingQuote(1)`
- `ActivityLog` 真实新增记录：
  - `客户状态变更`：`活跃 -> 沉默`
  - `订单状态变更`：`待跟进 -> 待报价`
- 关键聚合口径未破坏：
  - `PendingCount` 对应 DB 聚合前后保持不变
  - `WonCount` 对应 DB 聚合前后保持不变
  - `TotalAmount` 对应 DB 聚合前后保持不变

## 7. QA 数据命令回归结果

实际执行：

- `Orderly.App.exe --qa-data-status`
- `Orderly.App.exe --reset-qa-data`
- `Orderly.App.exe --qa-data-status`

结果：

- `status` 正常输出
- `reset` 正常执行
- 未出现无限增长
- 最终又执行了一轮 `reset + status` 恢复稳定基线

最终基线：

- Customers `3`
- Orders `3`
- Deals `3`
- FollowUps `4`
- Notes `3`
- PriceAdjustments `2`
- ActivityLogs `10`

## 8. P1.3 核心回归结果

### Build

- `dotnet build Orderly.sln -c Debug`
- 结果：`0 Warning / 0 Error`

### --qa-mode

- 实际运行 `Orderly.App.exe --qa-mode`
- 能进入工作台

### 搜索 / 筛选 / QuickFilter

实际 UIA 验证通过：

- 搜索框可输入 `[P1.3_QA]`
- 可定位 `[P1.3_QA] 客户-A`
- 可定位 `[P1.3_QA] 订单-待处理`
- `Btn_ClearFilters` 可点击
- `Chip_TodayFollowUp` / `Chip_OverdueFollowUp` / `Chip_TomorrowFollowUp` / `Chip_PendingOrders` / `Chip_WonOrders` 均可点击
- 每个 QuickFilter 都能定位到对应预期 QA 订单项

### AddOrder / AddNote / 持久化

实际 UIA 验证通过：

- `Btn_AddOrder` 可打开 `AddOrderDialog`
- 实际创建订单 `[P1.4.1_QA] UIA AddOrder Verify`
- 重启应用后，该订单仍可在 UI 搜到
- `Btn_AddNote` 可打开 `AddNoteDialog`
- 实际插入模板并保存备注 `[P1.4.1_QA] UIA AddNote Verify`
- SQLite 已存在对应订单 / 备注记录

### FollowUp / Deal / ActivityLog

实际 UIA + SQLite 即时核对通过：

- 跟进完成：
  - `p13qa-followup-001` -> `Completed(2)`
- 跟进延期：
  - `p13qa-followup-002` 新时间 -> `2026-04-29 09:30`
- 跟进取消：
  - `p13qa-followup-003` -> `Cancelled(4)`
- Deal 阶段推进：
  - `p13qa-deal-001` `Negotiating(3) -> Won(4)`
- `ActivityLog` 真实新增：
  - `完成跟进`
  - `延期跟进`
  - `取消跟进`
  - `更新成交阶段`

### 验证边界

本轮做了真实运行态验证，但没有做 Final Visual QA，也没有做 125% 缩放验证。

## 9. 没有做哪些事

- 没有新增业务功能
- 没有做 UI 视觉改版
- 没有处理 125% 缩放
- 没有改 `FloatingWindowViewModel`
- 没有大改 `DatabaseInitializer.cs`
- 没有删除 QA Mode / QA Data Maintenance
- 没有删除 `ActivityLog` 链路
- 没有改 `MerchantOrder` / `OrderListItem`
- 没有用 `IOrderService` 替换 `IOrderRepository`
- 没有把状态切换或保存逻辑下沉到 code-behind

## 10. 后续建议

- 后续若继续跑这类 QA 自动化，建议为 QA 自动化使用隔离数据库，或让运行态产生的 QA ActivityLog 也带 QA 标记，降低 reset 后的保守保留干扰。
- `artifacts` 里部分旧 UIA 脚本存在编码损坏，后续如果还要长期复用，建议单独清理，但本轮未提交任何 `artifacts` 改动。
- 如果继续减负 `MainWindow.xaml`，下一步可继续抽低风险的 `ListBox` 基础样式或紧凑 pill 壳子，但不建议为了“完全泛化”引入新的 converter / attached property。
