# Requirements Document

## Introduction

This feature transforms the existing Orderly project into a fully generic, local-first PC commerce and operations system for small business owners. The final positioning is:

> **Orderly = a local-first PC application combining deal/sales, orders, inventory, customers, cash flow, data analytics, and business advice for small business owners.**

The current implementation (WPF + .NET 8 + SQLite/SQLCipher) contains customer-specific and industry-specific logic, a remote business gateway, legacy naming, legacy state machines, and legacy business projections. This transformation removes all of that from the main line and replaces it with an industry-agnostic universal domain model, service layer, template/customization system, and WPF UI, while fully preserving the completed P0 security system with no regression.

The transformation is **not** about being compatible with any single legacy business. The goal is to make Orderly itself a universal commerce model. The WeChat miniprogram (`miniprogram/`) and cloud functions (`cloudfunctions/`) are **out of scope** for this transformation.

The highest principle of this transformation: **no customer-specific, industry-specific, or legacy-project-specific traces may remain in main-line source, UI, data model, docs, or scripts.**

## Glossary

- **Orderly**: The local-first PC commerce/operations application that is the subject of this transformation.
- **Main_Line**: The current working tree under `src/`, `tests/`, `tools/`, `README.md`, and `docs/` that is shipped and built; excludes git history and the out-of-scope `miniprogram/` and `cloudfunctions/` directories.
- **P0_Security_System**: The completed local security subsystem comprising SQLCipher full-database encryption, the local account system, the launcher database, the multi-account database structure, DPAPI key protection, field-level sensitive data encryption, backup/restore, security audit, and `LocalSessionContext`/`DataKey`.
- **Universal_Domain_Model**: The industry-agnostic set of value objects, enums, and entities defined in `Orderly.Core/Commerce`.
- **Commerce_Service_Layer**: The set of universal service interfaces and implementations that operate on the Universal_Domain_Model.
- **Business_Template**: A configurable definition that customizes custom fields, page configuration, and workflow configuration for a workspace.
- **Custom_Fields**: Per-entity user-defined fields persisted in a `CustomFieldsJson` column on each entity.
- **Forbidden_Term**: Any of the customer-specific, industry-specific, or legacy-project-specific tokens listed in the Forbidden Terms constraint that must not appear in the Main_Line.
- **External_Connector**: A pluggable, neutral abstraction for optional outbound integrations; disabled by default in V1.
- **OutboundEndpointPolicy**: The existing generic outbound HTTP security infrastructure that validates outbound endpoints; retained only under neutral naming.
- **Workspace_Service**: `IWorkspaceService`, which manages `BusinessWorkspace` records.
- **Import_Export_Service**: `IImportExportService`, which performs generic CSV/XLSX import and export.
- **Forbidden_Terms_Test**: The `ForbiddenTermsRegressionTests` automated test that scans the Main_Line for Forbidden_Terms.
- **Core_Flow**: The end-to-end runnable flow: create customer → create product → create inventory item → record inbound movement → create order → add order item → record payment → advance fulfillment → complete order → inventory deduction → cash flow generated → workbench metrics refresh → insights generated.
- **AvgDailyUsage30d**: The average daily inventory usage computed over a fixed 30-day window, used as the denominator when computing CoverageDays.
- **CoverageDays**: The estimated number of days the available inventory quantity will last, computed as available quantity divided by AvgDailyUsage30d; null (unavailable / not computable) when AvgDailyUsage30d is 0.
- **Core_Write_Transaction**: A single atomic transaction within which the Commerce_Service_Layer executes a core business write operation, such that the operation either fully commits or fully rolls back with all data left unchanged.
- **InventoryItemId**: The unique identifier of an `InventoryItem`; used to aggregate required and deducted inventory quantities across multiple OrderItems that reference the same `InventoryItem`.
- **QuantityAvailable**: The available on-hand quantity of an `InventoryItem` against which order-completion inventory availability checks are evaluated and from which deductions are applied.
- **Business_Key**: A stable, operation-identifying key derived from business data (for example, the owning order together with the record type) used to make a core write operation idempotent, so that re-running the operation links to or updates the existing `PaymentRecord`, `CashFlowEntry`, `InventoryMovement`, `BusinessInsight`, or `BusinessMetricSnapshot` record rather than creating a duplicate; contains no Forbidden_Term.
- **DefaultCommerce**: The internal stable key of the single built-in Business_Template; its user-visible Simplified Chinese display name is `默认经营模板`. Developer documentation and internal code MAY refer to the template by this key, but the user-facing UI displays only the Simplified Chinese display name.

## Constraints and Assumptions

The following are explicit constraints and assumptions recorded for this transformation. They are referenced by requirements below.

### C-1: AGENTS.md Closed-Area Override (User-Authorized)

The workspace `AGENTS.md` marks the Login Page, Settings Page, Order Fulfillment Page, Order Fulfillment backend fields, and Exception Handling Page as closed/completed areas that must not be modified. **The user has explicitly confirmed that this universal-transformation request OVERRIDES those `AGENTS.md` closed-area constraints.** For this transformation, the Login Page, Settings Page, Order Fulfillment Page, Order Fulfillment backend fields, and Exception Handling Page **are in scope** and may be modified, refactored, or replaced as needed to achieve the universal model.

### C-2: P0 Security Preservation Takes Precedence

Regardless of C-1, the P0_Security_System functionality MUST be preserved with no regression. Where a transformation step would conflict with P0 security behavior, P0 security behavior takes precedence.

### C-3: Out-of-Scope Areas

`miniprogram/` and `cloudfunctions/` are out of scope and MUST NOT be involved in this transformation.

### C-4: Forbidden Terms

The following Forbidden_Terms MUST NOT appear in the Main_Line (`src/`, `tests/`, `tools/`, `README.md`, `docs/`, UI text, config names, class names, file names, environment variable names, and data-model names):
`StringNarration`, `串述`, `adminPcGateway`, `bracelet`, `bead`, `beads`, `wrist`, `wristSize`, `diameter`, `珠串`, `珠子`, `手围`, `直径`, `材质`, `订单设计`, `成品`, `平均每串`, `Orderly-SN`, `start-sn`.
Occurrences in git history are acceptable; only the current working tree must be clean.

The ForbiddenTermsRegressionTests SHALL scan `src/`, `tests/`, `tools/`, `README.md`, and `docs/`. Kiro spec files (`requirements.md`, `design.md`, `tasks.md` under `.kiro/`) are NOT production Main_Line and are NOT part of the forbidden-terms runtime scan. The forbidden-term definitions MUST NOT be copied into `docs/`, `README.md`, `src/`, `tests/`, `tools/`, or any other scanned location where they would trigger the production scan; within the scanned source the definitions exist only as runtime-constructed fragments per Requirement 11.

### C-5: Build/Test/Run Acceptance

Larger refactors and deletions are permitted, but the final result MUST build (`dotnet build Orderly.sln -c Debug`), pass tests (`dotnet test`), pass QA smoke and universal regression scripts, return nothing from the forbidden-terms scan, and run the Core_Flow.

### C-6: User Data Safety

Real user local data MUST NOT be deleted by any cleanup or migration step.

## Requirements

### Requirement 1: Product Identity Cleanup

**User Story:** As a small business owner, I want Orderly to present a neutral, generic commerce identity, so that the application is not tied to any specific customer or industry.

#### Acceptance Criteria

1. THE Orderly application SHALL use the exact product brand name string "Orderly" in the application window title bar, the About page, the `start-orderly.bat` startup script, the `README.md` file, and the documentation files under `docs/`.
2. THE Orderly application SHALL present user-facing UI labels, buttons, menus, empty states, and page titles as Simplified Chinese labels appropriate to each element, and SHALL NOT require any such user-facing UI element to display the literal string "Orderly".
3. THE Main_Line SHALL contain zero occurrences of any Forbidden_Term in any UI text element, in the window title bar, or in any documentation file.
4. THE Orderly application SHALL present positioning text that describes a local-first PC commerce/operations system and that explicitly enumerates all of the following seven capability areas: deal/sales, orders, inventory, customers, cash flow, data analytics, and business advice for small business owners.
5. THE Orderly application SHALL resolve its application root path to the directory `%LocalAppData%\Orderly` and SHALL read and write all local application data exclusively under that single directory.
6. THE Main_Line SHALL include exactly one startup script file named `start-orderly.bat`.
7. THE Main_Line SHALL contain zero files named `start-sn.bat`.
8. THE README.md SHALL describe only the generic universal commerce system and SHALL contain zero occurrences of any Forbidden_Term.
9. WHERE legacy local data from a previous installation directory exists, THE Orderly application SHALL refer to its handling using the neutral term "legacy local data migration" and SHALL use zero occurrences of any Forbidden_Term in that reference.

### Requirement 2: Universal Domain Model

**User Story:** As a developer, I want an industry-agnostic domain model in `Orderly.Core/Commerce`, so that Orderly can represent any small business without industry-specific fields.

#### Acceptance Criteria

1. THE Universal_Domain_Model SHALL define at minimum the following 14 required value objects and enums in `Orderly.Core/Commerce`: `CommerceMoney`, `DateRange`, `EntityLifecycleStatus`, `BusinessEntityType`, `CustomFieldDataType`, `OrderSalesStage`, `OrderPaymentStage`, `OrderFulfillmentStage`, `CashFlowDirection`, `CashFlowSettlementStatus`, `InventoryMovementType`, `ProductType`, `TaskStatus`, and `InsightSeverity`, each retaining its intended responsibility, AND MAY define additional neutral helper value objects, enums, DTOs, result objects, paging objects, validation objects, or transaction objects provided each additional type is industry-agnostic and contains no Forbidden_Term.
2. THE Universal_Domain_Model SHALL define at minimum the following 18 required entities in `Orderly.Core/Commerce`: `BusinessWorkspace`, `BusinessTemplate`, `CustomFieldDefinition`, `UnitDefinition`, `Product`, `ProductVariant`, `InventoryItem`, `InventoryMovement`, `Customer`, `CustomerContact`, `Order`, `OrderItem`, `PaymentRecord`, `CashFlowEntry`, `Supplier`, `BusinessTask`, `BusinessInsight`, and `BusinessMetricSnapshot`, each retaining its intended responsibility, AND MAY define additional neutral helper entities or internal infrastructure types provided each additional type is industry-agnostic and contains no Forbidden_Term.
3. THE top-level field names and types of every Universal_Domain_Model entity SHALL be industry-agnostic, such that no top-level field name or type identifier contains any Forbidden_Term as defined in the spec glossary.
4. WHERE an entity requires personalization beyond its industry-agnostic top-level fields, THE Universal_Domain_Model SHALL store that personalization exclusively in a single nullable string field named `CustomFieldsJson` on the entity, and SHALL NOT add industry-specific top-level fields for that personalization.
5. WHERE an entity carries a `CustomFieldsJson` value, THE Universal_Domain_Model SHALL store that value as provided and SHALL NOT itself reject the value at assignment time, deferring well-formedness validation to the Commerce_Service_Layer and repository save boundary as specified in Requirement 3.
6. THE Universal_Domain_Model SHALL represent every monetary value using the `decimal` type and SHALL constrain each monetary value to the inclusive range -999,999,999.99 to 999,999,999.99 with a scale of exactly 2 decimal places.
7. THE Universal_Domain_Model SHALL provide on every entity a non-null `CreatedAt` timestamp, a non-null `UpdatedAt` timestamp, and a nullable `DeletedAt` timestamp, each stored in UTC.
8. WHEN any persisted field of a Universal_Domain_Model entity is modified, THE Universal_Domain_Model SHALL set that entity's `UpdatedAt` to the current UTC time and SHALL leave its `CreatedAt` unchanged.
9. WHEN an entity is soft-deleted or archived, THE Universal_Domain_Model SHALL set the entity's `DeletedAt` audit field to the current UTC time and set its `EntityLifecycleStatus` to the corresponding archived or deleted value, while retaining the entity's stored data so it is excluded from active queries but remains recoverable.

### Requirement 3: SQLite/SQLCipher Data Layer

**User Story:** As a small business owner, I want my universal commerce data stored securely on my PC, so that all my business records are encrypted and durable.

#### Acceptance Criteria

1. THE Orderly data layer SHALL preserve the P0_Security_System with no regression, per constraint C-2, such that all P0_Security_System automated tests pass with zero failures after data-layer changes.
2. THE Orderly data layer SHALL provide one SQLite/SQLCipher table and one repository for each entity defined in the Universal_Domain_Model, where every repository exposes create, read, update, and delete operations for its entity.
3. WHEN the application initializes a workspace database, THE Orderly data layer SHALL create or update the universal Commerce schema using a schema-initialization routine that, when run two or more times against the same database, leaves the schema in an identical final state without raising an error.
4. WHEN legacy generic CRM data is present at migration start, THE Orderly data layer SHALL map `Customer` to `Customer`, `Order` to `Order`, `Deal` to an `Order`, `BusinessTask`, or note as determined by the documented mapping rules, `FollowUp` to `BusinessTask`, and `CustomerNote` to a note, and SHALL retain all `ActivityLog` records unchanged.
5. THE Orderly data layer SHALL NOT migrate the legacy customer-specific or industry-specific remote data model, and SHALL leave such records unread and unmodified.
6. WHEN a data migration is executed two or more times against the same source data, THE Orderly data layer SHALL produce an identical target record set with no duplicated migrated records.
7. WHEN a data migration runs, THE Orderly data layer SHALL retain every existing source record without deletion or overwrite, and SHALL create a complete backup of the source database before applying any change.
8. IF the backup required before a migration cannot be created, THEN THE Orderly data layer SHALL abort the migration before applying any change, leave the source data unmodified, and record an indication that the migration did not run because the backup failed.
9. WHEN a data migration completes or fails, THE Orderly data layer SHALL write a log entry recording the outcome, including whether the migration succeeded or failed and the count of records migrated.
10. THE Orderly data layer migration SHALL be covered by automated tests that verify non-destructive behavior, idempotent repeatability, and the legacy entity mappings defined in criterion 4.
11. WHEN a Commerce_Service_Layer service or repository saves an entity, THE Orderly data layer SHALL validate the entity's `CustomFieldsJson` value before persisting it.
12. IF an entity's `CustomFieldsJson` value is non-null and is not well-formed JSON WHEN a save is attempted, THEN THE Orderly data layer SHALL reject the save with an error indicating invalid custom-field content and SHALL leave existing persisted data unchanged.

### Requirement 4: Universal Service Layer

**User Story:** As a developer, I want a universal service layer over the domain model, so that the UI and tests interact with business logic through stable, industry-agnostic interfaces.

#### Acceptance Criteria

1. THE Commerce_Service_Layer SHALL define the interfaces `IWorkspaceService`, `IBusinessTemplateService`, `ICustomFieldService`, `IUnitService`, `IProductService`, `IInventoryService`, `ICustomerService`, `IOrderService`, `IPaymentService`, `ICashFlowService`, `ISupplierService`, `IBusinessTaskService`, `IDashboardService`, `IBusinessInsightService`, and `IImportExportService`.
2. WHEN an order is created or updated, THE `IOrderService` SHALL recalculate the order subtotal, total, cost, gross profit, paid amount, and receivable amount as monetary values rounded to 2 decimal places, and SHALL recalculate gross margin as a percentage between 0 and 100 rounded to 2 decimal places.
3. THE `IOrderService` SHALL support independent updates to the `OrderSalesStage`, `OrderPaymentStage`, and `OrderFulfillmentStage` stage dimensions, such that a payment action SHALL NOT require changing `OrderSalesStage` or `OrderFulfillmentStage`, and a fulfillment action SHALL NOT require changing `OrderSalesStage` or `OrderPaymentStage`.
4. WHEN an order stage transition is requested AND the transition is permitted by the active workflow configuration, THE `IOrderService` SHALL apply the transition to only the stage dimension or dimensions named by that transition, WHERE the active workflow configuration MAY define a composite transition that updates one, two, or all three of the `OrderSalesStage`, `OrderPaymentStage`, and `OrderFulfillmentStage` dimensions.
5. IF an order stage transition is requested AND the transition is not permitted by the active workflow configuration, THEN THE `IOrderService` SHALL reject the transition, SHALL leave all three of the `OrderSalesStage`, `OrderPaymentStage`, and `OrderFulfillmentStage` dimensions unchanged with no partial update, and SHALL return an error result indicating the transition is not permitted.
6. WHEN an order is completed AND, for every InventoryItemId referenced by one or more inventory-linked OrderItems in the order, the aggregated required quantity summed across all OrderItems referencing that InventoryItemId is less than or equal to that InventoryItem's QuantityAvailable, THE `IOrderService` SHALL update the associated customer statistics and SHALL, within the Core_Write_Transaction, apply exactly one deduction per InventoryItemId equal to the aggregated required quantity for that InventoryItemId, WHERE an OrderItem that is not linked to an InventoryItem (such as a service, a custom item, or a product not linked to inventory) SHALL NOT participate in the inventory availability check or the deduction and SHALL NOT block order completion due to missing inventory.
7. IF an order is completed AND the aggregated required quantity for any InventoryItemId, summed across all inventory-linked OrderItems referencing that InventoryItemId, exceeds that InventoryItem's QuantityAvailable, THEN THE `IOrderService` SHALL reject the completion, SHALL roll back the entire completion transaction so that all inventory quantities and customer statistics remain unchanged with no partial update, and SHALL return an error result indicating insufficient inventory.
8. WHEN an inventory movement is recorded, THE `IInventoryService` SHALL update the inventory item quantity according to the `InventoryMovementType`.
9. WHEN inventory metrics are requested, THE `IInventoryService` SHALL compute low-stock status as true when the available quantity is less than or equal to the item reorder threshold, SHALL compute average daily usage over fixed 7-day and 30-day windows, SHALL compute `CoverageDays` as the available quantity divided by `AvgDailyUsage30d`, and SHALL produce a reorder suggestion and inventory insights.
10. IF `AvgDailyUsage30d` is 0 WHEN inventory metrics are requested, THEN THE `IInventoryService` SHALL report `CoverageDays` as null (unavailable / not computable) and SHALL NOT report `CoverageDays` as 0, because a value of 0 would incorrectly indicate no remaining coverage.
11. WHEN customer metrics are requested, THE `ICustomerService` SHALL compute recency as days since the last completed order, frequency as the count of completed orders, and monetary as the summed total of completed orders, and SHALL produce repurchase reminders.
12. THE `ICashFlowService` SHALL record income, expense, receivable, and payable entries, SHALL support settlement of receivable and payable entries, SHALL produce period summaries, and SHALL compute a cash-flow health score expressed as an integer between 0 and 100.
13. WHEN a dashboard snapshot is requested, THE `IDashboardService` SHALL return a unified `DashboardSnapshot` containing aggregate metrics and 7-day trend series.
14. THE `IBusinessInsightService` SHALL generate insights using deterministic local rules only and SHALL NOT call any large language model.
15. THE `IBusinessInsightService` SHALL expose a reserved pluggable `IBusinessInsightProvider` extension point.
16. WHEN an order is completed, THE `IOrderService` SHALL aggregate the required inventory quantities by InventoryItemId across all inventory-linked OrderItems in the order and SHALL evaluate inventory availability using those aggregated per-InventoryItemId quantities rather than evaluating each OrderItem independently.
17. WHEN an order is completed AND the aggregated inventory availability check passes, THE `IOrderService` SHALL, within the Core_Write_Transaction, apply exactly one inventory deduction per InventoryItemId equal to the aggregated required quantity for that InventoryItemId, so that no InventoryItemId is deducted more than once and no required quantity is partially deducted.
18. WHEN the `IPaymentService` creates or records a PaymentRecord, THE `IPaymentService` SHALL generate or link to at most one corresponding CashFlowEntry for that PaymentRecord.
19. WHEN an order is completed AND a PaymentRecord or CashFlowEntry already exists for that order, THE `IOrderService` SHALL reuse the existing records and SHALL create no additional PaymentRecord or CashFlowEntry for that order.
20. WHEN the Commerce_Service_Layer performs a core write operation that generates `PaymentRecord`, `CashFlowEntry`, `InventoryMovement`, `BusinessInsight`, or `BusinessMetricSnapshot` records, THE Commerce_Service_Layer SHALL make that operation idempotent by Business_Key where a Business_Key is defined for the record type, such that re-running the same completion or payment operation SHALL produce no duplicate financial, inventory, or insight records.

### Requirement 5: Universal Template and Customization System

**User Story:** As a small business owner, I want to customize fields, pages, and workflows through a template, so that Orderly fits my business without code changes.

#### Acceptance Criteria

1. WHEN a user invokes a create, edit, activate, clone, import, or export operation on a Business_Template, THE `IBusinessTemplateService` SHALL complete that operation using JSON as the import and export serialization format.
2. IF a JSON import payload fails schema validation or references an undefined Universal_Domain_Model entity type, THEN THE `IBusinessTemplateService` SHALL reject the import, return an error indicating the specific validation failure, and leave all existing Business_Templates unchanged.
3. THE Orderly application SHALL provide exactly one built-in Business_Template whose internal stable key is `DefaultCommerce` and whose user-visible Simplified Chinese display name is `默认经营模板`, and SHALL NOT provide any industry-specific built-in template.
4. WHEN a user configures a `CustomFieldDefinition` entry, THE `ICustomFieldService` SHALL associate that entry with exactly one Universal_Domain_Model entity type and SHALL support between 0 and 100 `CustomFieldDefinition` entries per entity type.
5. THE Business_Template SHALL support page configuration consisting of metric-card show/hide state, table-column show/hide state, default sort, default unit, default currency, and default order flow, where each show/hide state resolves to exactly one of the two values shown or hidden.
6. THE Business_Template SHALL support workflow configuration that defines a default workflow over the independent `OrderSalesStage`, `OrderPaymentStage`, and `OrderFulfillmentStage` dimensions, WHERE the workflow MAY define composite transitions that each update one, two, or all three of those stage dimensions, and SHALL assign each of the three dimensions an initial stage value.
7. WHEN a workspace has no explicitly activated template, THE Orderly application SHALL activate the built-in template identified by the internal stable key `DefaultCommerce` as that workspace's active template.
8. WHEN the built-in `DefaultCommerce` template is presented in the user-facing UI, THE Orderly application SHALL display its Simplified Chinese display name `默认经营模板` and SHALL NOT display the string "Default Commerce", WHERE developer documentation and internal code MAY refer to the template by its internal stable key `DefaultCommerce`.

### Requirement 6: WPF UI Restructure

**User Story:** As a small business owner, I want a clear navigation across all commerce areas, so that I can run my business from one local application.

#### Acceptance Criteria

1. THE Orderly WPF UI SHALL persistently display top-level navigation entries whose displayed labels are exactly 工作台 (Workbench), 订单 (Orders), 商品 (Products), 库存 (Inventory), 客户 (Customers), 现金流 (Cash Flow), 经营建议 (Business Advice), 设置 (Settings), and 我的 (Me/Account), presented in that order, with all nine entries visible without scrolling at the application's default window size.
2. WHEN the Workbench page is displayed, THE Orderly WPF UI SHALL show dashboard metrics and trends sourced from `IDashboardService` within 2 seconds under normal local operating conditions.
3. WHEN the Orders page is displayed, THE Orderly WPF UI SHALL show order data sourced from `IOrderService` within 2 seconds under normal local operating conditions.
4. WHEN the Products page is displayed, THE Orderly WPF UI SHALL show product data sourced from `IProductService` within 2 seconds under normal local operating conditions.
5. WHEN the Inventory page is displayed, THE Orderly WPF UI SHALL show inventory data sourced from `IInventoryService` within 2 seconds under normal local operating conditions.
6. WHEN the Customers page is displayed, THE Orderly WPF UI SHALL show customer data sourced from `ICustomerService` within 2 seconds under normal local operating conditions.
7. WHEN the Cash Flow page is displayed, THE Orderly WPF UI SHALL show cash-flow data sourced from `ICashFlowService` within 2 seconds under normal local operating conditions.
8. WHEN the Business Advice page is displayed, THE Orderly WPF UI SHALL show insights sourced from `IBusinessInsightService` within 2 seconds under normal local operating conditions.
9. WHEN the Settings page is displayed, THE Orderly WPF UI SHALL present settings organized into labeled groups, with each setting assigned to exactly one group and no group displayed without a label.
10. THE Orderly WPF UI SHALL source all displayed business data from the Commerce_Service_Layer and SHALL NOT reference any legacy state machine, legacy projection, or Forbidden_Term.
11. THE Login Page, Settings Page, Order Fulfillment Page, and Exception Handling Page SHALL be treated as in scope for this restructure per constraint C-1.
12. WHEN a user selects a top-level navigation entry, THE Orderly WPF UI SHALL display the corresponding page within 1 second and visually mark the selected entry as active.
13. IF a Commerce_Service_Layer service is unavailable or returns an error when its page is displayed, THEN THE Orderly WPF UI SHALL display an error indication on that page, retain the current navigation state, and SHALL NOT terminate the application.
14. WHEN a Commerce_Service_Layer service returns an empty data set for a displayed page, THE Orderly WPF UI SHALL display an empty-state indication instead of a blank content region.

### Requirement 7: ViewModel Refactor

**User Story:** As a developer, I want per-page ViewModels backed by the universal service layer, so that the UI logic is maintainable and free of legacy aggregation.

#### Acceptance Criteria

1. THE Orderly application SHALL provide exactly one dedicated ViewModel (or one clearly delimited region within a single partial) for each UI page, such that no single ViewModel or partial file aggregates logic for more than one page.
2. WHEN the ViewModel refactor is complete, THE Orderly application SHALL ensure that no individual `MainViewModel` partial file exceeds 500 lines of code.
3. THE Orderly ViewModels SHALL obtain business data only through the Commerce_Service_Layer.
4. THE Orderly ViewModels SHALL NOT issue any direct call to a legacy remote service, where a direct call is any invocation that bypasses the Commerce_Service_Layer.
5. IF a ViewModel requests business data while the Commerce_Service_Layer is unavailable or returns a failure result, THEN THE Orderly application SHALL surface an error indication to the user, retain the last known valid UI state without partial updates, and SHALL NOT fall back to any legacy remote service.
6. THE Main_Line SHALL NOT contain any legacy business, gateway, order-fulfillment, or exception ViewModel partial that references one or more Forbidden_Terms.
7. WHEN a refactored page is displayed, THE Orderly UI bindings SHALL bind every data-bound control exclusively to properties exposed by that page's dedicated ViewModel, with no binding targeting a legacy aggregation property.

### Requirement 8: External Connector and Gateway Cleanup

**User Story:** As a small business owner, I want Orderly to run fully local by default with no legacy remote integrations, so that my data stays on my PC unless I opt in to a future connector.

#### Acceptance Criteria

1. THE Main_Line SHALL NOT contain any of the following legacy artifacts: the customer-specific gateway client, gateway order service, gateway business service, inventory gateway adapter, gateway options, gateway environment variables, gateway action constants, or outbound configuration UI, such that a full-text search of the Main_Line for each named artifact and for every Forbidden_Term returns zero matches.
2. THE Orderly application SHALL preserve all neutral, non-business-specific security infrastructure required by the P0_Security_System and by future connector safety (for example `OutboundEndpointPolicy` and similar neutral components), SHALL remove or rename all legacy, customer-specific, or industry-specific gateway code, and SHALL keep the name and public members of every preserved component free of all Forbidden_Terms.
3. THE Orderly application SHALL define at minimum the five required reserved neutral interfaces and types `IExternalConnector`, `IExternalOrderConnector`, `IExternalInventoryConnector`, `ConnectorOptions`, and `ConnectorHealthStatus`, each declared but not wired to any active runtime implementation in V1, AND MAY define additional neutral helper types, DTOs, result objects, or internal infrastructure types provided each additional type is industry-agnostic and contains no Forbidden_Term.
4. WHILE the Orderly application is running in V1, THE Orderly application SHALL keep every External_Connector in the disabled state by default, where disabled means the connector performs no outbound network requests and exposes no enabled configuration entry point to the user.
5. IF program logic attempts to invoke a disabled External_Connector in V1, THEN THE Orderly application SHALL reject the invocation, perform no outbound network request, preserve all local data unchanged, and return a result indicating the connector is disabled.
6. WHEN the Orderly application starts in V1, THE Orderly application SHALL report the health status of every defined External_Connector as disabled.

### Requirement 9: Import and Export

**User Story:** As a small business owner, I want to import and export my data as spreadsheets, so that I can move data in and out of Orderly safely.

#### Acceptance Criteria

1. WHEN the user requests an export of products, inventory, customers, orders, or cash-flow data and selects CSV or XLSX format, THE `IImportExportService` SHALL produce a file in the selected format containing all records of the selected data type.
2. WHEN the user provides a CSV or XLSX file to import products, inventory, customers, orders, or cash-flow data, THE `IImportExportService` SHALL accept the file and begin processing it.
3. IF a provided import file is not a valid CSV or XLSX file, or its columns do not match the expected schema for the selected data type, THEN THE `IImportExportService` SHALL reject the file without committing any data and SHALL return an error indication identifying the reason for rejection.
4. WHEN a valid import file is provided, THE `IImportExportService` SHALL produce a preview that lists the count of rows to be added, the count of rows to be updated, and the count of rows containing errors, before any data is committed.
5. WHEN the user commits an import, THE `IImportExportService` SHALL apply only the valid rows and SHALL leave all pre-existing records that are not referenced by the import unchanged.
6. IF one or more rows in a committed import cannot be imported, THEN THE `IImportExportService` SHALL complete the import of all valid rows and SHALL report each failed row together with an error indication identifying the reason for failure.
7. IF an error occurs during commit that prevents the import from completing, THEN THE `IImportExportService` SHALL restore the data to its state prior to the commit and SHALL return an error indication.
8. THE Orderly application SHALL implement all import and export logic within the Import_Export_Service.

### Requirement 10: Demo and QA Data

**User Story:** As a QA engineer, I want neutral demo data, so that I can exercise Orderly's full feature set without any industry-specific samples.

#### Acceptance Criteria

1. THE Orderly demo data SHALL use neutral, Simplified Chinese, user-visible values, specifically `客户 A` and `客户 B` for customers, `商品 A` and `商品 B` for products, `库存项 A` and `库存项 B` for inventory items, `供应商 A` for the supplier, `订单 001` for the order, `收入分类 A` for the income category, and `支出分类 A` for the expense category, and SHALL NOT use English-style values such as "Customer A" or "Product A" as user-visible demo data.
2. THE Orderly demo data SHALL NOT contain any industry-specific sample value or any Forbidden_Term, where Forbidden_Term is defined in the spec glossary.
3. THE Orderly demo data SHALL include at least one record in each of the following categories: orders, order items, customers, inventory items, inventory movements, income entries, expense entries, receivable entries, and payable entries.
4. THE Orderly demo data SHALL include at least one inventory item whose available quantity is at or below its defined low-stock threshold.
5. THE Orderly demo data SHALL include at least one generated insight.

### Requirement 11: Testing

**User Story:** As a developer, I want comprehensive automated tests including a forbidden-terms regression test, so that the universal model stays correct and clean.

#### Acceptance Criteria

1. THE `Orderly.sln` SHALL include the `tests/Orderly.Tests` project.
2. THE `tests/Orderly.Tests` project SHALL contain test directories named `Commerce`, `Inventory`, `Cashflow`, `Customers`, `Templates`, `Analytics`, and `Regression`, with no required directory missing.
3. THE `tests/Orderly.Tests` project SHALL contain at least one automated test in each of the `Commerce`, `Inventory`, `Cashflow`, `Customers`, `Templates`, and `Analytics` directories.
4. WHEN the test suite is executed, THE `tests/Orderly.Tests` project SHALL execute every test in the `Commerce`, `Inventory`, `Cashflow`, `Customers`, `Templates`, and `Analytics` directories and report each as passed or failed.
5. THE `tests/Orderly.Tests` project SHALL include a `ForbiddenTermsRegressionTests` test that scans every file under the `src/`, `tests/`, and `tools/` directories and the `README.md` file and the `docs/` directory for each Forbidden_Term.
6. WHEN the `ForbiddenTermsRegressionTests` scan completes and no Forbidden_Term is present in the scanned Main_Line locations, THE `ForbiddenTermsRegressionTests` SHALL report a passed result.
7. IF one or more Forbidden_Terms are present in the scanned Main_Line locations, THEN THE `ForbiddenTermsRegressionTests` SHALL report a failed result and SHALL list, for each occurrence, the offending file path and the matched Forbidden_Term.
8. THE `ForbiddenTermsRegressionTests` source file SHALL construct each Forbidden_Term at runtime by concatenating two or more string fragments, so that the `ForbiddenTermsRegressionTests` source file itself contains no literal Forbidden_Term and is not reported as an offending location by its own scan.
9. THE `ForbiddenTermsRegressionTests` scan SHALL be limited to `src/`, `tests/`, `tools/`, `README.md`, and `docs/`, and SHALL exclude Kiro spec files (`requirements.md`, `design.md`, `tasks.md` under `.kiro/`), which are not production Main_Line, per constraint C-4.

### Requirement 12: QA Scripts

**User Story:** As a QA engineer, I want commerce smoke and universal regression scripts, so that I can validate the universal system before release while keeping P0 security checks.

#### Acceptance Criteria

1. THE Main_Line SHALL include a script at `tools/qa/run-commerce-smoke.ps1` that executes the commerce smoke steps in the following order: build, init QA database, create demo workspace, create customer, create product, create inventory, create order, record payment, deduct inventory, create cash flow, generate dashboard, generate insight, and output pass result.
2. WHEN `tools/qa/run-commerce-smoke.ps1` completes with every commerce smoke step succeeding, THE Main_Line SHALL cause the script to exit with code 0 and emit a pass result.
3. IF any commerce smoke step fails, THEN THE Main_Line SHALL cause `tools/qa/run-commerce-smoke.ps1` to stop at the failed step, exit with a non-zero code, and emit a failure result identifying the failed step.
4. THE Main_Line SHALL include a script at `tools/qa/run-universal-regression.ps1` that executes the universal regression steps in the following order: dotnet build, dotnet test, forbidden-terms scan, security smoke, backup smoke, and commerce smoke.
5. WHEN `tools/qa/run-universal-regression.ps1` completes with every universal regression step succeeding, THE Main_Line SHALL cause the script to exit with code 0 and emit a pass result.
6. IF any universal regression step fails, THEN THE Main_Line SHALL cause `tools/qa/run-universal-regression.ps1` to stop at the failed step, exit with a non-zero code, and emit a failure result identifying the failed step.
7. THE Main_Line SHALL retain the existing P0 security smoke script with its file name and P0 security checks unchanged.
8. WHEN the forbidden-terms scan runs, IF a QA script's name or content references a Forbidden_Term, THEN THE Main_Line SHALL ensure that QA script is updated to remove every Forbidden_Term reference or is removed entirely, such that zero QA scripts contain a Forbidden_Term reference after the operation.

### Requirement 13: Documentation

**User Story:** As a developer or owner, I want accurate generic documentation, so that the universal model, data model, template system, and release process are clearly described.

#### Acceptance Criteria

1. THE Main_Line SHALL contain a `README.md` file at the repository root that describes the generic universal commerce system.
2. WHEN the `README.md` is reviewed, THE Main_Line SHALL ensure its content references only the generic universal commerce system and contains no Forbidden_Term.
3. THE Main_Line SHALL contain all four documentation files at the specified paths: `docs/ORDERLY_UNIVERSAL_MODEL.md`, `docs/ORDERLY_DATA_MODEL.md`, `docs/ORDERLY_TEMPLATE_SYSTEM.md`, and `docs/ORDERLY_RELEASE_CHECKLIST.md`.
4. IF any of the four required documentation files is absent from its specified path, THEN THE Main_Line SHALL be considered non-compliant with this requirement.
5. WHEN any Main_Line documentation file (`README.md` or any file under `docs/`) is scanned, THE Main_Line SHALL contain zero occurrences of any Forbidden_Term.
6. IF a Main_Line documentation file contains one or more occurrences of any Forbidden_Term, THEN THE Main_Line SHALL be considered non-compliant with this requirement, identifying each file and term occurrence detected.
7. THE Main_Line documentation under `docs/` and the `README.md` file SHALL NOT reproduce the forbidden-term definitions, so that scanned documentation never triggers the production forbidden-terms scan, per constraint C-4.

### Requirement 14: Engineering Cleanup

**User Story:** As a developer, I want the repository cleaned of legacy artifacts, so that the working tree is tidy and free of legacy traces while preserving real user data.

#### Acceptance Criteria

1. WHERE the `src/scratch` directory is not referenced by any committed build configuration or source file, THE Main_Line SHALL remove the entire `src/scratch` directory from the working tree.
2. THE Main_Line SHALL remove all regenerated build artifacts (compiled binaries and build output directories), all `bin_verify` directories, and all `*_wpftmp` residue files from the working tree.
3. THE Main_Line SHALL remove all legacy scripts that reference Forbidden_Terms from the working tree.
4. THE Main_Line SHALL update `.gitignore` so that regenerated build artifacts and temporary residue matching the patterns removed in criteria 2 and 3 are excluded from version control.
5. THE engineering cleanup SHALL NOT delete, modify, or relocate real user local data, per constraint C-6.
6. WHEN the engineering cleanup completes, THE Main_Line SHALL leave a working tree that contains no `src/scratch` directory, no `bin_verify` directories, no `*_wpftmp` residue files, and no legacy scripts referencing Forbidden_Terms, while all real user local data protected by constraint C-6 remains present and unchanged.

### Requirement 15: P0 Security Preservation

**User Story:** As a small business owner, I want my existing security guarantees kept intact, so that the transformation does not weaken data protection.

#### Acceptance Criteria

1. THE Orderly application SHALL preserve SQLCipher full-database encryption such that every pre-existing P0 security automated test covering full-database encryption continues to pass with zero failures and zero new skips.
2. THE Orderly application SHALL preserve the local account system, the launcher database, and the multi-account database structure such that every pre-existing P0 security automated test covering these components continues to pass with zero failures and zero new skips.
3. THE Orderly application SHALL preserve DPAPI key protection and field-level sensitive data encryption such that every pre-existing P0 security automated test covering key protection and field-level encryption continues to pass with zero failures and zero new skips.
4. THE Orderly application SHALL preserve backup/restore and security audit such that every pre-existing P0 security automated test covering backup/restore and security audit continues to pass with zero failures and zero new skips.
5. THE Orderly application SHALL preserve `LocalSessionContext` and `DataKey` behavior such that every pre-existing P0 security automated test covering `LocalSessionContext` and `DataKey` continues to pass with zero failures and zero new skips.
6. WHERE a transformation step conflicts with P0_Security_System behavior, THE Orderly application SHALL preserve the P0_Security_System behavior, per constraint C-2.
7. WHEN a transformation step is completed, THE Orderly application SHALL re-run the full pre-existing P0 security automated test suite, and the transformation step SHALL be accepted only if the suite reports zero failures and zero new skips relative to the pre-transformation baseline.
8. IF any pre-existing P0 security automated test fails or is newly skipped after a transformation step, THEN THE Orderly application SHALL treat the transformation step as a regression and SHALL retain the pre-transformation P0_Security_System behavior, per constraint C-2.

### Requirement 16: Build, Test, and Core Flow Acceptance

**User Story:** As a release manager, I want the transformed system to build, test, and run end to end, so that V1 is shippable.

#### Acceptance Criteria

1. WHEN `dotnet clean` followed by `dotnet build Orderly.sln -c Debug` is executed, THE Orderly solution SHALL complete the build with zero compiler errors within 300 seconds.
2. WHEN `dotnet build Orderly.sln -c Debug` is executed, THE Orderly solution SHALL NOT increase the count of existing compiler warnings relative to the pre-transformation baseline, and SHALL ensure that every new compiler warning introduced by this transformation is fixed or explicitly reported.
3. IF the build completes with one or more compiler errors, OR introduces one or more new compiler warnings that are neither fixed nor explicitly reported, THEN THE Orderly solution SHALL be treated as failing acceptance and SHALL report the count and location of each compiler error and each unresolved new compiler warning.
4. WHEN `dotnet test` is executed, THE Orderly test suite SHALL report zero failed tests, SHALL NOT increase the count of skipped tests relative to the pre-transformation baseline, SHALL NOT introduce any new skipped test unless that skip is explicitly justified, and SHALL complete within 600 seconds.
5. WHEN the `dotnet test` run includes the P0 security tests, THE Orderly test suite SHALL report zero new P0 security test failures and zero new P0 security test skips relative to the pre-transformation baseline, consistent with Requirement 15.
6. WHEN the QA commerce smoke and universal regression scripts are executed, EACH script SHALL complete with zero failed assertions and SHALL report a terminal success result.
7. WHEN the forbidden-terms scan is executed against the Main_Line, THE scan SHALL return zero matches.
8. IF the forbidden-terms scan returns one or more matches against the Main_Line, THEN THE scan SHALL be treated as failing acceptance and SHALL report the matched term and its file location for each match.
9. WHEN a user performs the Core_Flow, THE Orderly application SHALL complete all 13 steps in the following order without unhandled error: create customer, create product, create inventory item, record inbound movement, create order, add order item, record payment, advance fulfillment, complete order, deduct inventory, generate cash flow, refresh workbench metrics, and generate insights.
10. IF any step of the Core_Flow fails to complete, THEN THE Orderly application SHALL halt the Core_Flow at the failing step, SHALL indicate which step failed, and SHALL preserve the data state established by the steps completed before the failure.

### Requirement 17: UI Language and Localization

**User Story:** As a small business owner in a Simplified Chinese locale, I want all user-visible text presented in Simplified Chinese, so that I can operate Orderly in my own language.

#### Acceptance Criteria

1. THE Orderly application SHALL default all user-visible UI text to Simplified Chinese.
2. WHERE an identifier is internal code (such as a class name, interface name, property name, or symbol), THE Orderly application MAY express that identifier in English.
3. THE Orderly WPF UI SHALL display the main navigation labels as exactly 工作台 (Workbench), 订单 (Orders), 商品 (Products), 库存 (Inventory), 客户 (Customers), 现金流 (Cash Flow), 经营建议 (Business Advice), 设置 (Settings), and 我的 (Me/Account), each mapped respectively to those capability areas.
4. WHERE English labels are used, THE Orderly application SHALL confine them to code, tests, or developer documentation and SHALL NOT display them in the primary user-facing UI.

### Requirement 18: Transactional Integrity of Core Writes

**User Story:** As a small business owner, I want core business write operations to be all-or-nothing, so that my data is never left in a partially updated state.

#### Acceptance Criteria

1. THE Commerce_Service_Layer SHALL execute every core business write operation as a single Core_Write_Transaction.
2. WHEN an order is completed, THE Commerce_Service_Layer SHALL atomically update, within one Core_Write_Transaction, the order stages, the inventory deduction for each OrderItem linked to an InventoryItem, the inventory movements for those inventory-linked OrderItems, the customer statistics, the payment and cash-flow records, the dashboard-impacting data, and the generated insights where applicable, WHERE an OrderItem that is not linked to an InventoryItem incurs no inventory deduction and no inventory movement.
3. IF any part of a Core_Write_Transaction fails, THEN THE Commerce_Service_Layer SHALL roll back the entire transaction and SHALL leave all data unchanged.
4. WHEN an order is completed within a Core_Write_Transaction, THE Commerce_Service_Layer SHALL apply inventory deductions as exactly one deduction per InventoryItemId equal to the required quantity aggregated across all inventory-linked OrderItems referencing that InventoryItemId, and SHALL record the corresponding InventoryMovement records within the same Core_Write_Transaction.
5. WHEN an order is completed AND a PaymentRecord or CashFlowEntry already exists for that order, THE Commerce_Service_Layer SHALL reuse the existing records within the Core_Write_Transaction and SHALL create no additional PaymentRecord or CashFlowEntry.
6. WHEN a Core_Write_Transaction generates `PaymentRecord`, `CashFlowEntry`, `InventoryMovement`, `BusinessInsight`, or `BusinessMetricSnapshot` records, THE Commerce_Service_Layer SHALL make the generation idempotent by Business_Key where a Business_Key is defined for the record type, such that re-running the same Core_Write_Transaction SHALL produce no duplicate records.
