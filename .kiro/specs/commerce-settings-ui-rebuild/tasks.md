# Implementation Plan: 经营管理系统设置与业务页 View 层视觉重建

## Overview

> 概述

本实施计划将设计文档拆解为一系列可由代码生成代理增量完成的编码步骤，遵循设计中"令牌 → 控件样式 → 页面"的单向三层依赖：先建立颜色/非颜色令牌与资源合并入口，再建立基础控件统一样式与状态呈现模板，最后在统一状态机骨架下逐页重建八个页面与设置子 Tab，并以全局静态扫描与构建验证收口。

约束遵循（贯穿全部任务）：

- 仅改动 `src/Orderly.App/Views` 下的指定 View 文件与 `src/Orderly.App/Views/Resources` 下的令牌/样式资源；不改 ViewModel、Core/Data/Infrastructure、cloudfunctions、外壳、登录页、我的页、各对话框。
- 被重建页面零硬编码视觉字面量（颜色/字号/间距/圆角），全部引用 token。
- 仅消费 ViewModel 既有的属性/命令/集合，不新增、不重命名 Binding 路径；代码后置文件保持不动。
- 颜色语义画刷复用既有主题键名（主色由绿改墨蓝）；非颜色令牌使用全新键名（基础圆角 4px），不与既有 `DesignTokens.xaml` 键冲突。

> 关于测试：设计文档明确本特性属 WPF View / XAML 视觉重建与设计令牌（配置资源）类工作，**不适用基于属性的测试（PBT）**，未设 Correctness Properties 章节。因此本计划不含属性测试任务，改用"令牌齐备性 / 亮暗一致性 / 对比度 / 硬编码扫描 / Binding 契约核验"等枚举型与静态型自动化测试（落在 `tests/Orderly.Tests`），均作为可选子任务（`*`）。性能与可达性中需人工核验的部分不纳入编码任务。

## Tasks

- [x] 1. 扩展主题颜色令牌（墨蓝主色与状态色）
  - [x] 1.1 在 `ThemeLight.xaml` 与 `ThemeDark.xaml` 原位扩展颜色语义画刷
    - 将既有主色键（`PrimaryBrush` 等）由绿色改为墨蓝，并补齐默认/悬停/按下/禁用四级
    - 补齐中性七级：底背景、表面背景、二级背景、分隔线、主文字、次文字、辅助文字
    - 补齐状态四态（成功绿/警告黄/错误红/信息蓝）的"底色 + 可读前景色"成对画刷
    - 新增焦点边框画刷 `UiFocusBrush`
    - 保持两套主题键名一一对应，不改 `ThemeHelper` 的整体替换契约
    - _Requirements: 1.2, 1.3, 1.4, 1.9, 12.1, 12.2, 12.3_
  - [x] 1.2 编写颜色令牌枚举校验测试
    - 校验所要求的主色分级/中性七级/状态四态/焦点色键全部存在
    - 校验 `ThemeLight` 与 `ThemeDark` 键集合完全一致（一一对应）
    - 对正文/背景、状态徽标前景/底色逐对计算对比度 ≥ 4.5:1
    - _Requirements: 1.2, 1.3, 1.4, 1.9, 12.1, 12.2_

- [x] 2. 建立非颜色令牌与资源合并入口
  - [x] 2.1 创建 `Views/Resources/Tokens/Typography.xaml`
    - 定义 ≥6 级字号阶（标题大/标题/副标题/正文/正文小/辅助）与字重
    - 定义中文族（思源黑体→系统中文回退）与英文数字族（Inter→系统回退）字体族 token
    - 正文字号 ≥13px、辅助说明 ≥12px
    - 使用全新键名前缀（`UiFont*` / `UiFontFamily*`），不与既有 `DesignTokens.xaml` 冲突
    - _Requirements: 1.5, 12.5_
  - [x] 2.2 创建 `Views/Resources/Tokens/Shape.xaml`
    - 定义三档圆角（小/基础 4px/大），基础圆角为 4 像素
    - 定义 ≥6 级间距阶（4/8/12/16/24/32），以 4px 为基础单位
    - 定义 ≥2 级阴影（卡片弱阴影、浮层强阴影）
    - 使用全新键名前缀（`UiRadius*` / `UiSpace*` / `UiElevation*`），不与既有键冲突
    - _Requirements: 1.6, 1.7, 1.8_
  - [x] 2.3 在 `App.xaml` 的 MergedDictionaries 集中引用新令牌字典
    - 合并 `Tokens/Typography.xaml` 与 `Tokens/Shape.xaml`
    - 确认颜色画刷经主题字典以 `DynamicResource` 全应用通行，暗色切换无需改 View
    - _Requirements: 1.1, 1.10_
  - [x] 2.4 编写非颜色令牌枚举校验测试
    - 校验所要求的字号/圆角/间距/阴影/字体族令牌键全部存在
    - 校验正文字号 ≥13px、辅助说明 ≥12px
    - 校验新令牌键名与既有 `DesignTokens.xaml` 键名不发生碰撞
    - _Requirements: 1.5, 1.6, 1.7, 1.8, 12.5_

- [x] 3. 建立基础控件统一样式与状态呈现模板
  - [x] 3.1 创建 `Views/Resources/Controls/ControlStyles.xaml`
    - 为按钮（主/次/文本）、输入框、下拉、复选、单选、Tab、表格（DataGrid/ListView）、徽标、提示条提供统一样式（隐式样式或具名 Style）
    - 表格采用舒适档行高/内边距，选中态用主色弱底 + 左侧主色条；启用 UI 虚拟化（VirtualizingStackPanel 或等价机制）
    - 所有可获焦控件具备取自 `UiFocusBrush` 的明确焦点边框
    - 全部引用令牌，零硬编码视觉字面量
    - _Requirements: 1.11, 11.4, 12.3_
  - [x] 3.2 创建 `Views/Resources/Controls/StatePresenters.xaml`
    - 提供空状态、加载占位（骨架/加载条）、错误提示、行级校验错误、状态徽标等可复用呈现模板
    - 状态徽标为胶囊形，使用状态四态"底+前景"，同时承载文字，不以颜色为唯一信息载体
    - _Requirements: 1.11, 12.4_
  - [x] 3.3 在 `App.xaml` 合并控件样式与状态模板字典
    - 合并 `Controls/ControlStyles.xaml` 与 `Controls/StatePresenters.xaml`
    - 确认合并顺序在令牌与主题字典之后，样式可正确解析令牌键
    - _Requirements: 1.1, 1.11_

- [x] 4. Checkpoint - 令牌与样式层就绪
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. 重建成交销售页（Products_View）
  - [x] 5.1 重建 `Sections/ProductsView.xaml`
    - 套用统一状态机骨架（加载/空/错误/内容），错误区绑定既有 `RefreshCommand` 重试
    - 顶部概况区承载汇总指标与时间筛选；主体以舒适表格密度展示 `Products` 集合
    - 选中态使用令牌选中视觉；加载中禁用变更命令按钮
    - 仅消费既有绑定，全部引用 token，零硬编码字面量；代码后置文件不动
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8_
  - [x] 5.2 ProductsView 硬编码扫描与 Binding 契约核验
    - 断言 XAML 不含颜色/字号/间距/圆角字面量
    - 比对重建前后引用的属性/命令/集合无新增、无重命名；代码后置无新增逻辑
    - _Requirements: 2.1, 2.2, 10.4, 10.5, 10.6_

- [x] 6. 重建订单页（Orders_View）
  - [x] 6.1 重建 `Sections/OrdersView.xaml`
    - 顶部筛选/搜索区绑定既有筛选项与搜索框；主体舒适表格展示 `Orders`
    - 订单状态以状态色徽标着色（绿=完成/正常、黄=需关注、红=异常/逾期、蓝=信息/待处理）
    - 套用状态机骨架与选中态；加载中禁用变更命令
    - 仅消费既有绑定，全部引用 token，零硬编码字面量
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8_
  - [x] 6.2 OrdersView 硬编码扫描与 Binding 契约核验
    - 断言无硬编码字面量；属性/命令/集合无新增、无重命名；代码后置无新增逻辑
    - _Requirements: 3.1, 3.2, 10.4, 10.5, 10.6_

- [x] 7. 重建库存页（Inventory_View）
  - [x] 7.1 重建 `Sections/InventoryView.xaml`
    - 主体舒适表格展示 `Items`；命中预警（`IsLowStock`/`ShouldReorder`）的行用警告/错误色高亮
    - 套用状态机骨架与选中态；加载中禁用变更命令
    - 仅消费既有绑定，全部引用 token，零硬编码字面量
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7_
  - [x] 7.2 InventoryView 硬编码扫描与 Binding 契约核验
    - 断言无硬编码字面量；属性/命令/集合无新增、无重命名；代码后置无新增逻辑
    - _Requirements: 4.1, 4.2, 10.4, 10.5, 10.6_

- [x] 8. 重建客户页（Customers_View）
  - [x] 8.1 重建 `Sections/CustomersView.xaml`
    - 顶部搜索/筛选区绑定既有属性；主体舒适表格展示 `Customers`
    - 跟进/异常状态以状态色徽标标注；套用状态机骨架与选中态；加载中禁用变更命令
    - 仅消费既有绑定，全部引用 token，零硬编码字面量
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8_
  - [x] 8.2 CustomersView 硬编码扫描与 Binding 契约核验
    - 断言无硬编码字面量；属性/命令/集合无新增、无重命名；代码后置无新增逻辑
    - _Requirements: 5.1, 5.2, 10.4, 10.5, 10.6_

- [x] 9. 重建现金流页（Cashflow_View）
  - [x] 9.1 重建 `Sections/CashflowView.xaml`
    - 顶部呈现收入/支出/净额三类核心数字，使用数字字号阶；净流入用成功绿、净流出用错误红
    - 主体舒适表格展示 `Entries`；套用空/加载状态骨架
    - 仅当 VM 已暴露图表数据时在概览区下方预留图表占位并绑定；当前未暴露则不引入新图表数据源
    - 仅消费既有绑定，全部引用 token，零硬编码字面量
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8_
  - [x] 9.2 CashflowView 硬编码扫描与 Binding 契约核验
    - 断言无硬编码字面量；属性/命令/集合无新增、无重命名；代码后置无新增逻辑
    - _Requirements: 6.1, 6.2, 10.4, 10.5, 10.6_

- [x] 10. 重建数据分析页（Analytics_View / WorkbenchView）
  - [x] 10.1 重建 `Sections/WorkbenchView.xaml`
    - 以指标卡阵列呈现既有关键指标，数字使用数字字号阶突出
    - 标注为异常/预警态的指标卡用状态色标注；套用空/加载状态
    - 仅消费既有绑定，全部引用 token，零硬编码字面量
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7_
  - [x] 10.2 WorkbenchView 硬编码扫描与 Binding 契约核验
    - 断言无硬编码字面量；属性/命令/集合无新增、无重命名；代码后置无新增逻辑
    - _Requirements: 7.1, 7.2, 10.4, 10.5, 10.6_

- [x] 11. 重建经营建议页（Advice_View）
  - [x] 11.1 重建 `Sections/BusinessAdviceView.xaml`
    - 以建议卡片列表呈现 `Insights`；按 `Severity` 用状态色分级标注
    - 将既有"采纳/忽略/查看详情"等命令绑定到卡片按钮；套用空/加载状态
    - 仅消费既有绑定，全部引用 token，零硬编码字面量
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7_
  - [x] 11.2 BusinessAdviceView 硬编码扫描与 Binding 契约核验
    - 断言无硬编码字面量；属性/命令/集合无新增、无重命名；代码后置无新增逻辑
    - _Requirements: 8.1, 8.2, 10.4, 10.5, 10.6_

- [x] 12. Checkpoint - 六个业务列表页就绪
  - Ensure all tests pass, ask the user if questions arise.

- [x] 13. 重建设置页主框架（Settings_View）
  - [x] 13.1 重建 `Sections/SettingsView.xaml`
    - 保留现有 Tab 结构，以 `SettingsTab*` 子 View 作为内容区承载
    - 当前 Tab 视觉高亮；若 VM 已暴露滚动位置字段则保留切换前滚动位置
    - 全部引用 token，零硬编码字面量；仅消费既有绑定
    - _Requirements: 9.1, 9.2, 9.3, 9.5_
  - [x] 13.2 SettingsView 硬编码扫描与 Binding 契约核验
    - 断言无硬编码字面量；属性/命令/集合无新增、无重命名；代码后置无新增逻辑
    - _Requirements: 9.1, 9.2, 10.4, 10.5, 10.6_

- [x] 14. 重建设置子 Tab
  - [x] 14.1 重建外观/通知/热键子 Tab
    - 重建 `SettingsTabAppearance.xaml`、`SettingsTabNotify.xaml`、`SettingsTabHotkeys.xaml`
    - 统一"标签-控件-说明"三段式行样式；禁用态用令牌禁用态视觉；行级校验失败用错误状态色提示（仅基于既有错误字段）
    - 仅消费既有绑定，全部引用 token，零硬编码字面量
    - _Requirements: 9.1, 9.2, 9.4, 9.6, 9.7_
  - [x] 14.2 重建数据/数据审计/数据安全子 Tab
    - 重建 `SettingsTabData.xaml`、`SettingsTabDataAudit.xaml`、`SettingsTabDataSecurity.xaml`
    - 套用三段式行样式、禁用态与行级错误呈现；仅消费既有绑定，全部引用 token
    - _Requirements: 9.1, 9.2, 9.4, 9.6, 9.7_
  - [x] 14.3 重建 AI/AI 诊断子 Tab
    - 重建 `SettingsTabAi.xaml`、`SettingsTabAiDiagnostics.xaml`
    - 套用三段式行样式、禁用态与行级错误呈现；仅消费既有绑定，全部引用 token
    - _Requirements: 9.1, 9.2, 9.4, 9.6, 9.7_
  - [x] 14.4 设置页全子 Tab 硬编码扫描与 Binding 契约核验
    - 对八个 `SettingsTab*` 断言无硬编码字面量；属性/命令/集合无新增、无重命名；代码后置无新增逻辑
    - _Requirements: 9.1, 9.2, 10.4, 10.5, 10.6_

- [x] 15. 全局交付门禁与范围隔离核验
  - [x] 15.1 八页 + 设置子 Tab 硬编码扫描汇总测试
    - 扫描全部被重建 XAML，断言均不含颜色/字号/间距/圆角字面量，全部引用 token
    - _Requirements: 1.12_
  - [x] 15.2 范围隔离核验测试
    - 静态确认 `ViewModels`/`Orderly.Core`/`Orderly.Data`/`Orderly.Infrastructure`/`cloudfunctions` 及外围 View（外壳、登录、我的页、各对话框等）未被改动
    - 确认未新增 ViewModel 文件、后端字段、cloudfunction，未引入图表/UI 组件/动画库与国际化
    - _Requirements: 10.1, 10.2, 10.3, 13.1, 13.2, 13.3, 13.4, 13.5, 13.6_

- [x] 16. 构建验证 Checkpoint
  - [x] 16.1 运行 `dotnet build` 全解决方案并修复编译错误
    - 确认八页与设置子 Tab 可正常加载渲染，列表页 UI 虚拟化生效
    - Ensure all tests pass, ask the user if questions arise.
    - _Requirements: 11.4_

## Notes

- 标记 `*` 的子任务为可选测试任务，可为更快交付 MVP 而跳过；核心实现任务不标记可选。
- 本特性按设计文档结论**不适用 PBT**，故无属性测试任务；测试以令牌齐备性、亮暗一致性、对比度计算、硬编码扫描、Binding 契约核验等枚举型/静态型自动化检查为主，落在 `tests/Orderly.Tests`。
- 性能基线（冷启动 ≤3s、切页 ≤300ms、1000 行 ≥50FPS）与键盘焦点、暗色切换、四态外观等需在基准设备人工核验，属非编码验收项，未纳入任务清单；其中可由代码保证的部分（UI 虚拟化、对比度、字号下限、焦点边框、颜色非唯一载体）已分别落在任务 3.1、1.2、2.4、3.2。
- 每个任务均引用具体需求条款以便追溯；Checkpoint 用于增量校验。
- 颜色复用既有主题键名（主色改墨蓝），非颜色令牌使用全新键名（基础圆角 4px），与我的页仍依赖的 `DesignTokens.xaml` 解耦共存。

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "2.1", "2.2"] },
    { "id": 1, "tasks": ["2.3", "3.1", "3.2"] },
    { "id": 2, "tasks": ["3.3", "1.2", "2.4"] },
    { "id": 3, "tasks": ["5.1", "6.1", "7.1", "8.1", "9.1", "10.1", "11.1", "13.1", "14.1", "14.2", "14.3"] },
    { "id": 4, "tasks": ["5.2", "6.2", "7.2", "8.2", "9.2", "10.2", "11.2", "13.2", "14.4"] },
    { "id": 5, "tasks": ["15.1", "15.2"] },
    { "id": 6, "tasks": ["16.1"] }
  ]
}
```
