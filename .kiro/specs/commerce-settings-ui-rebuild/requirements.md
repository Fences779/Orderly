# Requirements Document

## Introduction

Orderly 当前 PC 端经营管理系统的 View 层（XAML、样式、资源）出现整体缺失，七大业务能力页面（成交销售、订单、库存、客户、现金流、数据分析、经营建议）与设置页的可视外观无法正常呈现。本次工作仅在 View 层进行重建，目标是在不修改任何 ViewModel、不修改后端、不修改外壳与登录我的页等已稳定外围模块的前提下，建立一套统一的视觉 token 体系，并基于该 token 体系重建上述八个页面的外观与基础交互。

视觉定调已与用户确认：风格方向为克制商务高效（信息密度高、装饰极少、强调可扫读）；主色为墨蓝；字体配对为思源黑体与 Inter；基础圆角为 4 像素；间距尺度为标准档；状态色为默认四态（成功绿、警告黄、错误红、信息蓝）；表格密度为舒适档；亮色与暗色双 token 一次建齐。

本文件不涉及具体技术实现方案，也不涉及任务拆解，仅描述需求与验收标准。

## Glossary

- **重建系统 / Rebuild_System**：本次需求覆盖的 View 层重建工作整体，作用范围限定为 src/Orderly.App/Views 下的指定 View 文件与新增样式资源。
- **视觉 token / Design_Token**：颜色、字体、字号阶、圆角、阴影、间距、控件状态色等基础视觉参数的统一命名定义。
- **Token 资源 / Token_Resource**：承载视觉 token 的 WPF ResourceDictionary 集合，供所有 View 引用。
- **业务页面 / Section_View**：成交销售、订单、库存、客户、现金流、数据分析、经营建议、设置八个主页面的 View 文件集合。
- **成交销售页 / Products_View**：对应 ProductsView.xaml，承载成交销售业务展示。
- **订单页 / Orders_View**：对应 OrdersView.xaml。
- **库存页 / Inventory_View**：对应 InventoryView.xaml。
- **客户页 / Customers_View**：对应 CustomersView.xaml。
- **现金流页 / Cashflow_View**：对应 CashflowView.xaml。
- **数据分析页 / Analytics_View**：对应 WorkbenchView.xaml。
- **经营建议页 / Advice_View**：对应 BusinessAdviceView.xaml。
- **设置页 / Settings_View**：对应 SettingsView.xaml 与其下属 SettingsTab*.xaml 子 Tab。
- **Binding 契约 / Binding_Contract**：当前 ViewModel 已对外暴露的属性名、命令名、集合名及其类型签名所构成的对外契约。
- **暗色模式 / Dark_Mode**：全局可切换至深色背景的视觉状态。
- **舒适表格密度 / Comfortable_Table_Density**：行高与内边距按"舒适"档配置的表格视觉参数。
- **状态色 / Status_Palette**：成功（绿）、警告（黄）、错误（红）、信息（蓝）四态颜色集合。
- **老板 / Owner**：使用者中负责经营决策的角色，关注汇总指标与异常预警。
- **操作员 / Operator**：使用者中负责日常录入与处理的角色，关注表格、表单、列表的录入效率。

## Requirements

### 需求 1：建立统一视觉 token 体系

**用户故事：** 作为老板与操作员，我希望整个应用的颜色、字号、间距、圆角、阴影、状态色都遵循同一套视觉 token，以便看到一致外观、降低阅读成本，并支持后续主题切换。

#### 验收标准

1. THE Rebuild_System SHALL 在 src/Orderly.App/Views/Resources 目录下提供承载视觉 token 的资源字典文件，并在应用资源合并入口集中引用。
2. THE Token_Resource SHALL 定义主色为墨蓝，并以分级（默认、悬停、按下、禁用）形式提供 WPF Brush 资源。
3. THE Token_Resource SHALL 定义至少七级中性色，覆盖：底背景、表面背景、二级背景、分隔线、主文字、次文字、辅助文字。
4. THE Token_Resource SHALL 定义状态色为成功绿、警告黄、错误红、信息蓝四态，每态包含底色与可读前景色两个 Brush 资源。
5. THE Token_Resource SHALL 定义至少六级字号阶（标题大、标题、副标题、正文、正文小、辅助），并指明中文族（思源黑体）与英文/数字族（Inter）的字体回退顺序。
6. THE Token_Resource SHALL 定义基础圆角为 4 像素，并以命名 token 暴露三档圆角（小、基础、大）。
7. THE Token_Resource SHALL 定义间距阶为标准档，并以 4 像素为基础单位提供至少六级间距 token（4、8、12、16、24、32 像素）。
8. THE Token_Resource SHALL 定义至少两级阴影 token：弱阴影用于卡片、强阴影用于浮层。
9. THE Token_Resource SHALL 同时提供亮色与暗色两套同名 token，二者通过同一组键名一一对应。
10. WHEN 应用切换至暗色模式，THE Rebuild_System SHALL 通过资源字典替换的方式整体切换 token，而无需修改任何 View 文件。
11. THE Token_Resource SHALL 为按钮、输入框、下拉、复选、单选、Tab、表格、徽标、提示条等基础控件提供统一样式，并以隐式样式或具名 Style 资源的形式暴露。
12. IF 任意业务页面在重建中引用了硬编码颜色、字号、间距或圆角字面量，THEN THE Rebuild_System SHALL 视为不达标并拒绝交付。

### 需求 2：成交销售页（Products_View）视觉与交互重建

**用户故事：** 作为老板，我希望在成交销售页看到清晰的当期成交概况与近期成交记录，以便快速判断当前业务节奏；作为操作员，我希望页面布局让我能快速定位待处理项。

#### 验收标准

1. THE Products_View SHALL 使用需求 1 定义的 Token_Resource，不出现任何硬编码视觉字面量。
2. THE Products_View SHALL 仅消费 ViewModel 已存在的属性、命令、集合，不新增也不重命名 Binding 路径。
3. THE Products_View SHALL 在页面顶部呈现概况区，用于承载汇总指标与时间筛选控件。
4. THE Products_View SHALL 在页面主体呈现成交列表区，使用舒适表格密度展示成交销售数据。
5. WHEN 列表为空，THE Products_View SHALL 显示带文案与图示的空状态占位。
6. WHILE 数据加载中，THE Products_View SHALL 显示加载占位（骨架或加载条），并禁用与数据变更相关的命令按钮。
7. IF 数据加载失败，THEN THE Products_View SHALL 显示错误提示区域；当 ViewModel 已暴露重试命令时，THE Products_View SHALL 在错误区中绑定该重试命令；当 ViewModel 未暴露重试命令时，THE Products_View SHALL 仅显示错误文案而不新增命令通道。
8. WHEN 列表项被选中，THE Products_View SHALL 通过 Token_Resource 中定义的选中态视觉变化清晰指示当前选中项。

### 需求 3：订单页（Orders_View）视觉与交互重建

**用户故事：** 作为操作员，我希望在订单页快速过滤、定位、查看订单状态，以便高效处理日常订单流转；作为老板，我希望能一眼看出订单整体进度分布。

#### 验收标准

1. THE Orders_View SHALL 使用需求 1 定义的 Token_Resource，不出现任何硬编码视觉字面量。
2. THE Orders_View SHALL 仅消费 ViewModel 已存在的属性、命令、集合，不新增也不重命名 Binding 路径。
3. THE Orders_View SHALL 在页面顶部呈现筛选与搜索区，承载现有筛选项与搜索框绑定。
4. THE Orders_View SHALL 在页面主体呈现订单列表，使用舒适表格密度，并以状态徽标形式展示订单状态。
5. THE Orders_View SHALL 使用需求 1 定义的状态色对订单状态徽标着色：绿色表示已完成或正常，黄色表示需要关注，红色表示异常或逾期，蓝色表示信息或待处理。
6. WHEN 列表项被选中，THE Orders_View SHALL 通过 Token_Resource 选中态清晰指示当前选中项。
7. WHEN 列表为空，THE Orders_View SHALL 显示空状态占位。
8. WHILE 数据加载中，THE Orders_View SHALL 显示加载占位并禁用与数据变更相关的命令按钮。

### 需求 4：库存页（Inventory_View）视觉与交互重建

**用户故事：** 作为操作员，我希望在库存页快速查看库存数量与预警项；作为老板，我希望低库存与异常项一眼被识别出来。

#### 验收标准

1. THE Inventory_View SHALL 使用需求 1 定义的 Token_Resource，不出现任何硬编码视觉字面量。
2. THE Inventory_View SHALL 仅消费 ViewModel 已存在的属性、命令、集合，不新增也不重命名 Binding 路径。
3. THE Inventory_View SHALL 在页面主体呈现库存列表，使用舒适表格密度。
4. WHEN 库存项命中预警条件（依据 ViewModel 已暴露的预警字段或集合），THE Inventory_View SHALL 使用状态色中的警告色或错误色对该行进行高亮标注。
5. WHEN 列表项被选中，THE Inventory_View SHALL 通过 Token_Resource 选中态清晰指示当前选中项。
6. WHEN 列表为空，THE Inventory_View SHALL 显示空状态占位。
7. WHILE 数据加载中，THE Inventory_View SHALL 显示加载占位并禁用与数据变更相关的命令按钮。

### 需求 5：客户页（Customers_View）视觉与交互重建

**用户故事：** 作为操作员，我希望在客户页快速搜索客户、查看客户基础信息与跟进状态；作为老板，我希望能快速识别重要客户与待跟进客户。

#### 验收标准

1. THE Customers_View SHALL 使用需求 1 定义的 Token_Resource，不出现任何硬编码视觉字面量。
2. THE Customers_View SHALL 仅消费 ViewModel 已存在的属性、命令、集合，不新增也不重命名 Binding 路径。
3. THE Customers_View SHALL 在页面顶部呈现搜索与筛选区，绑定 ViewModel 既有搜索与筛选属性。
4. THE Customers_View SHALL 在页面主体呈现客户列表，使用舒适表格密度。
5. WHEN 客户存在跟进或异常状态（依据 ViewModel 已暴露字段），THE Customers_View SHALL 使用状态色徽标进行标注。
6. WHEN 列表项被选中，THE Customers_View SHALL 通过 Token_Resource 选中态清晰指示当前选中项。
7. WHEN 列表为空，THE Customers_View SHALL 显示空状态占位。
8. WHILE 数据加载中，THE Customers_View SHALL 显示加载占位并禁用与数据变更相关的命令按钮。

### 需求 6：现金流页（Cashflow_View）视觉与交互重建

**用户故事：** 作为老板，我希望在现金流页看清当期收支总览与现金流走势，以便判断经营资金的健康程度；作为操作员，我希望能快速录入与查看明细。

#### 验收标准

1. THE Cashflow_View SHALL 使用需求 1 定义的 Token_Resource，不出现任何硬编码视觉字面量。
2. THE Cashflow_View SHALL 仅消费 ViewModel 已存在的属性、命令、集合，不新增也不重命名 Binding 路径。
3. THE Cashflow_View SHALL 在页面顶部呈现收入、支出、净额三类核心数字，并使用 Token_Resource 中数字字号阶呈现。
4. THE Cashflow_View SHALL 使用 Token_Resource 状态色将正值（净流入）显示为成功绿、负值（净流出）显示为错误红。
5. THE Cashflow_View SHALL 在页面主体呈现现金流明细列表，使用舒适表格密度。
6. WHEN 列表为空，THE Cashflow_View SHALL 显示空状态占位。
7. WHILE 数据加载中，THE Cashflow_View SHALL 显示加载占位。
8. WHERE ViewModel 已暴露图表相关属性或集合，THE Cashflow_View SHALL 在概览区下方预留图表占位区并完成绑定；当 ViewModel 未暴露图表数据，THE Cashflow_View SHALL 不引入新的图表数据来源。

### 需求 7：数据分析页（Analytics_View，对应 WorkbenchView）视觉与交互重建

**用户故事：** 作为老板，我希望在数据分析页看到关键经营指标与汇总，以便对业务整体形成判断；作为操作员，我希望页面结构清晰，能快速看到自己关心的指标卡。

#### 验收标准

1. THE Analytics_View SHALL 使用需求 1 定义的 Token_Resource，不出现任何硬编码视觉字面量。
2. THE Analytics_View SHALL 仅消费 ViewModel 已存在的属性、命令、集合，不新增也不重命名 Binding 路径。
3. THE Analytics_View SHALL 以指标卡阵列布局呈现 ViewModel 已暴露的关键指标。
4. THE Analytics_View SHALL 使用 Token_Resource 中的数字字号阶突出关键数值。
5. WHEN 某项指标在 ViewModel 中标注为异常或预警态，THE Analytics_View SHALL 使用 Token_Resource 状态色对该指标卡进行视觉标注。
6. WHEN 指标集合为空，THE Analytics_View SHALL 显示空状态占位。
7. WHILE 数据加载中，THE Analytics_View SHALL 显示加载占位。

### 需求 8：经营建议页（Advice_View）视觉与交互重建

**用户故事：** 作为老板，我希望经营建议页把系统给出的建议分级、可读、可操作地呈现，以便快速决定是否采纳。

#### 验收标准

1. THE Advice_View SHALL 使用需求 1 定义的 Token_Resource，不出现任何硬编码视觉字面量。
2. THE Advice_View SHALL 仅消费 ViewModel 已存在的属性、命令、集合，不新增也不重命名 Binding 路径。
3. THE Advice_View SHALL 以建议卡片列表布局呈现 ViewModel 已暴露的建议集合。
4. WHEN 单条建议带有优先级或紧急度字段（依据 ViewModel 已暴露字段），THE Advice_View SHALL 使用 Token_Resource 状态色进行视觉分级标注。
5. WHEN 单条建议绑定有 ViewModel 既有的"采纳/忽略/查看详情"等命令，THE Advice_View SHALL 在建议卡片上显示对应操作按钮并完成命令绑定。
6. WHEN 建议集合为空，THE Advice_View SHALL 显示空状态占位。
7. WHILE 数据加载中，THE Advice_View SHALL 显示加载占位。

### 需求 9：设置页（Settings_View）视觉与交互重建

**用户故事：** 作为老板与操作员，我希望设置页保持清晰的分组与可读的层级，能快速找到外观、数据、AI、热键、通知、安全等子项并完成配置。

#### 验收标准

1. THE Settings_View SHALL 使用需求 1 定义的 Token_Resource，不出现任何硬编码视觉字面量。
2. THE Settings_View SHALL 仅消费 SettingsViewModel 与 MainViewModel 设置相关分部已存在的属性、命令、集合，不新增也不重命名 Binding 路径。
3. THE Settings_View SHALL 保留现有 Tab 结构，以 SettingsTab* 各子 View 作为内容区承载。
4. THE Settings_View SHALL 使用统一的设置项行样式，包含标签、控件、说明文字三段式结构。
5. WHEN 子 Tab 切换，THE Settings_View SHALL 在视觉上明确高亮当前 Tab；WHERE ViewModel 已暴露子 Tab 滚动位置字段，THE Settings_View SHALL 保留切换前的滚动位置。
6. WHEN 设置项处于禁用或受限态（依据 ViewModel 已暴露字段），THE Settings_View SHALL 使用 Token_Resource 中禁用态视觉清晰呈现。
7. IF 设置项校验失败，THEN THE Settings_View SHALL 使用 Token_Resource 错误状态色显示行级错误提示，并仅基于 ViewModel 已暴露的错误字段进行展示，不新增校验逻辑。

### 需求 10：不破坏 Binding 契约与外围模块约束

**用户故事：** 作为老板，我希望本次 View 层重建不会引入新的故障面，确保已经稳定的 ViewModel、后端、外壳、登录页与我的页继续按既有方式工作。

#### 验收标准

1. THE Rebuild_System SHALL 不修改 src/Orderly.App/ViewModels 目录下的任何文件。
2. THE Rebuild_System SHALL 不修改 src/Orderly.Core、src/Orderly.Data、src/Orderly.Infrastructure 与 cloudfunctions 目录下的任何文件。
3. THE Rebuild_System SHALL 不修改 MainWindow.xaml 与其代码后置文件、NavigationSidebar.xaml 与其代码后置文件、所有 LoginView 相关文件、MeProfileView.xaml 与其代码后置文件、所有 Add* 对话框文件、LoginToastOverlay.xaml、FloatingWindow.xaml、PinUnlockView.xaml、SnoozeFollowUpDialog.xaml、EmergencyPinDialog.xaml。
4. THE Rebuild_System SHALL 在每个被重建的 View 中仅消费 ViewModel 已对外暴露的属性、命令、集合，不引入新的 ViewModel 字段调用路径。
5. THE Rebuild_System SHALL 不在被重建 View 的代码后置文件中新增"事件转命令"或"状态计算"逻辑；既有代码后置文件保持不动。
6. IF 任何重建后的 View 触发既有命令的方式与原 Binding 契约不一致，THEN THE Rebuild_System SHALL 视为不达标并拒绝交付。

### 需求 11：性能基线

**用户故事：** 作为老板与操作员，我希望页面切换、滚动、加载在视觉重建后体验不下降。

#### 验收标准

1. WHEN 在搭载 4 核 CPU 与 8 GB 内存的 Windows 10 或 Windows 11 设备上首次冷启动应用，THE Rebuild_System SHALL 保证从主窗口可见到首个业务页面可交互的耗时不超过 3 秒。
2. WHEN 在主窗口已就绪状态下点击侧边栏切换业务页面，THE Rebuild_System SHALL 保证页面首屏可见耗时不超过 300 毫秒。
3. WHILE 列表数据条数不超过 1000 条，THE Rebuild_System SHALL 保证表格滚动帧率在上述基准设备与 60 Hz 显示器上不低于 50 FPS。
4. THE Rebuild_System SHALL 在所有列表型业务页面启用 UI 虚拟化（VirtualizingStackPanel 或等价 WPF 虚拟化机制）。
5. IF 任意被重建页面在切换时引发可观察的卡顿（连续掉帧超过 200 毫秒），THEN THE Rebuild_System SHALL 视为不达标。

### 需求 12：可达性基线

**用户故事：** 作为老板与操作员，我希望颜色对比、键盘焦点、字体可读性达到可日常使用的水平。

#### 验收标准

1. THE Token_Resource SHALL 保证亮色与暗色两套主题下，正文文字与其背景的对比度不低于 4.5 比 1。
2. THE Token_Resource SHALL 保证状态徽标的前景色与底色对比度不低于 4.5 比 1。
3. WHEN 用户使用 Tab 键在页面内移动焦点，THE Rebuild_System SHALL 在每一个可获得焦点的控件上显示明确的焦点边框，焦点边框颜色取自 Token_Resource。
4. THE Rebuild_System SHALL 不依赖颜色作为唯一信息载体；状态信息须同时具备文字、图标、徽标三种形态中的至少一种额外提示。
5. THE Rebuild_System SHALL 保证所有正文字号不小于 13 像素，辅助说明字号不小于 12 像素。

### 需求 13：交付边界与非目标

**用户故事：** 作为老板与操作员，我希望本次重建只做明确范围内的事，避免影响已经稳定的能力，也避免承诺未经确认的工作。

#### 验收标准

1. THE Rebuild_System SHALL 仅重建以下 View 文件及其样式资源：ProductsView.xaml、OrdersView.xaml、InventoryView.xaml、CustomersView.xaml、CashflowView.xaml、WorkbenchView.xaml、BusinessAdviceView.xaml、SettingsView.xaml、SettingsTabAi.xaml、SettingsTabAiDiagnostics.xaml、SettingsTabAppearance.xaml、SettingsTabData.xaml、SettingsTabDataAudit.xaml、SettingsTabDataSecurity.xaml、SettingsTabHotkeys.xaml、SettingsTabNotify.xaml，以及 src/Orderly.App/Views/Resources 下的 token 资源文件。
2. THE Rebuild_System SHALL 不重建、不修改 MainWindow.xaml、NavigationSidebar.xaml、MeProfileView.xaml、LoginView.xaml 与其全部相关分部、LoginBrandPanel.xaml、LoginSignInPanel.xaml、LoginCreateAccountPanel.xaml、LoginOwnerCreatePanel.xaml、LoginAccountManagementPanel.xaml、LoginPasswordRecoveryPanel.xaml、LoginToastOverlay.xaml、AddCustomerDialog.xaml、AddFollowUpDialog.xaml、AddNoteDialog.xaml、AddOrderDialog.xaml、AddPriceAdjustmentDialog.xaml、SnoozeFollowUpDialog.xaml、EmergencyPinDialog.xaml、PinUnlockView.xaml、FloatingWindow.xaml。
3. THE Rebuild_System SHALL 不新增任何 ViewModel 文件、不新增任何后端字段、不新增任何 cloudfunction。
4. THE Rebuild_System SHALL 不在本次范围内引入新的图表库、UI 组件库或动画库；如确需依赖，须由用户单独确认后再以独立需求纳入。
5. THE Rebuild_System SHALL 不在本次范围内引入国际化多语言切换；界面文案以简体中文为唯一目标语言。
6. THE Rebuild_System SHALL 不在本次范围内修改业务逻辑、数据库 Schema、SQLCipher 加密策略、数据备份与恢复策略。
