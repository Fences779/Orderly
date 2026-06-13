# Implementation Plan: Orderly Universal Commerce System

## Overview

This plan converts the universal-commerce design into incremental C# (.NET 8 / WPF) coding steps. It builds bottom-up: the industry-agnostic domain model first, then the SQLite/SQLCipher data layer, the Commerce Service Layer, the template/customization system, connectors, import/export, demo data, the WPF UI shell and per-page ViewModels, then cleanup (gateway, product identity, engineering), QA scripts, documentation, P0 security verification, and a final end-to-end Core_Flow acceptance pass.

Every core write executes inside a single `Core_Write_Transaction` and is idempotent by `Business_Key`. Property tests (CsCheck/FsCheck on the xUnit runner, minimum 100 iterations each) validate the 19 correctness properties from the design and are placed next to the code they cover. The P0_Security_System is preserved with zero regressions throughout (C-2, Requirement 15).

Implementation language: **C#** (matches the existing four-project solution: `Orderly.App`, `Orderly.Core`, `Orderly.Data`, `Orderly.Infrastructure`).

## Tasks

- [x] 1. Set up test project, PBT harness, and forbidden-terms guard
  - [x] 1.1 Create `tests/Orderly.Tests` project and wire it into the solution
    - Add the `tests/Orderly.Tests` xUnit project and register it in `Orderly.sln`
    - Create the required test directories `Commerce`, `Inventory`, `Cashflow`, `Customers`, `Templates`, `Analytics`, and `Regression`
    - Add a property-based testing library (CsCheck or FsCheck) layered on the xUnit runner, configured for a minimum of 100 iterations
    - _Requirements: 11.1, 11.2_

  - [x] 1.2 Implement the forbidden-terms regression test
    - Add `ForbiddenTermsRegressionTests` in the `Regression` directory that scans every file under `src/`, `tests/`, and `tools/`, plus `README.md` and `docs/`, excluding `.kiro/` spec files
    - Construct each forbidden term at runtime by concatenating two or more string fragments so the test source contains no literal term
    - Report a passed result when no term is found; on failure list each offending file path and matched term
    - **Timing**: It is OK to create `ForbiddenTermsRegressionTests` early (this wave). It is EXPECTED to FAIL until legacy cleanup (gateway cleanup task 17, product identity cleanup task 20, engineering cleanup task 21) and documentation cleanup (task 23) are complete. Early failure of `ForbiddenTermsRegressionTests` is NOT a blocker and MUST NOT stop progress prior to the final regression/acceptance wave.
    - The test becomes required / pass-gated only during the final regression/acceptance wave (tasks 24–25), after product identity cleanup, gateway cleanup, docs cleanup, and engineering cleanup are done. Final acceptance still requires zero forbidden-term matches across `src/`, `tests/`, `tools/`, `README.md`, and `docs/`.
    - _Requirements: 11.5, 11.6, 11.7, 11.8, 11.9, 16.7, 16.8_

- [x] 2. Implement universal value objects and enums (`Orderly.Core/Commerce`)
  - [x] 2.1 Create the 14 required value objects and enums
    - Implement `CommerceMoney` (decimal, range −999,999,999.99…999,999,999.99, scale exactly 2, out-of-range rejected), `DateRange`, `EntityLifecycleStatus`, `BusinessEntityType`, `CustomFieldDataType`, `OrderSalesStage`, `OrderPaymentStage`, `OrderFulfillmentStage`, `CashFlowDirection` (Income/Expense/Transfer), `CashFlowSettlementStatus`, `InventoryMovementType`, `ProductType`, `TaskStatus`, `InsightSeverity`
    - Keep all names industry-agnostic and free of any Forbidden_Term
    - _Requirements: 2.1, 2.3, 2.6_

  - [x] 2.2 Write property test for CommerceMoney range and scale
    - **Property 1: Money values stay in range with scale 2**
    - **Validates: Requirements 2.6**
    - Place in `Commerce` directory
    - _Requirements: 2.6_

- [x] 3. Implement the entity base classes and 18 universal entities
  - [x] 3.1 Create the shared entity base hierarchy
    - Implement `CommerceEntity` (Id, UTC `CreatedAt` fixed, UTC `UpdatedAt` advanced on every persisted-field change, nullable UTC `DeletedAt`, `EntityLifecycleStatus`, single nullable `CustomFieldsJson`), `WorkspaceScopedEntity` (non-null `WorkspaceId`), and `SystemEntity`
    - Implement soft-delete/archive that sets `DeletedAt` + lifecycle status while retaining recoverable data; `CustomFieldsJson` is stored as provided without assignment-time validation
    - _Requirements: 2.4, 2.5, 2.7, 2.8, 2.9_

  - [x] 3.2 Implement the 18 universal entities
    - Workspace-scoped: `Product`, `ProductVariant`, `InventoryItem`, `InventoryMovement`, `Customer`, `CustomerContact`, `Order` (three independent stage fields + monetary fields), `OrderItem` (optional `InventoryItem` link), `PaymentRecord`, `CashFlowEntry`, `Supplier`, `BusinessTask`, `BusinessInsight`, `BusinessMetricSnapshot`
    - System/config: `BusinessWorkspace`, `BusinessTemplate` (built-in or workspace-scoped via nullable `WorkspaceId`), `CustomFieldDefinition` (template-scoped, one entity type), `UnitDefinition`
    - Ensure no top-level field name or type identifier contains any Forbidden_Term
    - _Requirements: 2.2, 2.3, 2.4_

  - [x] 3.3 Write property test for audit timestamps
    - **Property 3: Mutation preserves CreatedAt and advances UpdatedAt**
    - **Validates: Requirements 2.8**
    - Place in `Commerce` directory
    - _Requirements: 2.8_

  - [x] 3.4 Write property test for soft-delete recoverability
    - **Property 4: Soft-delete is recoverable and excluded from active queries**
    - **Validates: Requirements 2.9**
    - Place in `Commerce` directory
    - _Requirements: 2.9_

- [x] 4. Implement the data layer: schema init, repositories, and custom-field validation
  - [x] 4.1 Implement idempotent Commerce schema initialization
    - Create one SQLCipher table per entity under `%LocalAppData%\Orderly`; running init repeatedly leaves an identical schema without error
    - Preserve the P0 launcher DB and multi-account structure unchanged
    - _Requirements: 3.2, 3.3, 1.5_

  - [x] 4.2 Implement one repository per entity with CRUD
    - Provide create/read/update/delete for every Universal_Domain_Model entity; active queries exclude soft-deleted records
    - _Requirements: 3.2_

  - [x] 4.3 Enforce CustomFieldsJson validation at the save boundary
    - On save, parse non-null `CustomFieldsJson`; if malformed, reject with `InvalidCustomFields` and leave existing persisted data unchanged
    - _Requirements: 3.11, 3.12_

  - [x] 4.4 Write property test for schema-init idempotence
    - **Property 5: Schema initialization is idempotent**
    - **Validates: Requirements 3.3**
    - Place in `Commerce` directory
    - _Requirements: 3.3_

  - [x] 4.5 Write property test for malformed custom fields
    - **Property 7: Malformed custom fields are rejected without side effects**
    - **Validates: Requirements 3.12**
    - Place in `Commerce` directory
    - _Requirements: 3.12_

- [x] 5. Checkpoint - domain and data layer
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement non-destructive legacy CRM migration
  - [x] 6.1 Implement the migration routine with backup-first and mapping rules
    - Back up the source DB before any change; abort with `BackupFailedMigrationAborted` if the backup cannot be created, leaving source unmodified and recording the reason
    - Map `Customer→Customer`, `Order→Order`, `Deal→Order|BusinessTask|note` (documented rules), `FollowUp→BusinessTask`, `CustomerNote→note`; retain `ActivityLog` unchanged; do not read/modify legacy industry-specific remote data
    - Make migration non-destructive and idempotent; log outcome and migrated record count
    - _Requirements: 3.4, 3.5, 3.6, 3.7, 3.8, 3.9_

  - [x] 6.2 Write property test for migration idempotence and non-destructiveness
    - **Property 6: Migration is idempotent and non-destructive**
    - **Validates: Requirements 3.6, 3.7**
    - Place in `Commerce` directory
    - _Requirements: 3.6, 3.7_

  - [x] 6.3 Write integration tests for legacy mappings
    - Verify the criterion-4 mappings and non-destructive/idempotent behavior end-to-end
    - _Requirements: 3.4, 3.10_

- [x] 7. Implement IOrderService (recalculation, three-dimensional stages, completion)
  - [x] 7.1 Implement order recalculation
    - On create/update recompute subtotal, total, cost, gross profit, paid amount, receivable (each money at scale 2) and gross margin as a percentage in [0,100] rounded to 2dp
    - _Requirements: 4.2_

  - [x] 7.2 Write property test for order recalculation
    - **Property 2: Order recalculation produces 2dp money and bounded gross margin**
    - **Validates: Requirements 4.2**
    - Place in `Commerce` directory
    - _Requirements: 4.2_

  - [x] 7.3 Implement independent three-dimensional stage transitions
    - Support independent updates to `OrderSalesStage`, `OrderPaymentStage`, `OrderFulfillmentStage`; apply only the dimension(s) a permitted transition names; reject non-permitted transitions with `TransitionNotPermitted` and no partial update
    - _Requirements: 4.3, 4.4, 4.5_

  - [x] 7.4 Write property test for independent stage dimensions
    - **Property 8: Order stage dimensions are independent**
    - **Validates: Requirements 4.3**
    - Place in `Commerce` directory
    - _Requirements: 4.3_

  - [x] 7.5 Implement order completion with aggregated deduction in a Core_Write_Transaction
    - Aggregate required quantity per `InventoryItemId` across inventory-linked OrderItems; if every aggregate ≤ `QuantityAvailable`, apply exactly one deduction per `InventoryItemId`, record InventoryMovements, and update customer statistics atomically; non-linked items neither block nor deduct
    - On insufficient inventory reject with `InsufficientInventory` and roll back the entire transaction; reuse any existing PaymentRecord/CashFlowEntry for the order
    - _Requirements: 4.6, 4.7, 4.16, 4.17, 4.19, 18.1, 18.2, 18.3, 18.4, 18.5_

  - [x] 7.6 Write property test for completion aggregation and rollback
    - **Property 10: Order completion aggregates per InventoryItemId and is all-or-nothing**
    - **Validates: Requirements 4.6, 4.7, 4.16, 4.17, 18.4**
    - Place in `Inventory` directory
    - _Requirements: 4.6, 4.7, 4.16, 4.17, 18.4_

  - [x] 7.7 Write property test for core-write atomicity
    - **Property 19: Core write operations are atomic**
    - **Validates: Requirements 18.1, 18.3**
    - Place in `Commerce` directory
    - _Requirements: 18.1, 18.3_

- [x] 8. Implement IInventoryService (movements and metrics)
  - [x] 8.1 Implement inventory movements and metrics
    - Update item quantity per `InventoryMovementType`; compute low-stock (available ≤ reorder threshold), 7-day and 30-day average daily usage, `CoverageDays = QuantityAvailable / AvgDailyUsage30d` reported as null when `AvgDailyUsage30d == 0`, plus reorder suggestion and inventory insights
    - _Requirements: 4.8, 4.9, 4.10_

  - [x] 8.2 Write property test for CoverageDays null rule
    - **Property 11: CoverageDays is null exactly when 30-day usage is zero**
    - **Validates: Requirements 4.10**
    - Place in `Inventory` directory
    - _Requirements: 4.10_

- [x] 9. Implement ICustomerService (RFM and repurchase reminders)
  - [x] 9.1 Implement customer metrics
    - Compute recency (days since last completed order), frequency (count of completed orders), monetary (summed total of completed orders); produce repurchase reminders
    - _Requirements: 4.11_

  - [x] 9.2 Write unit tests for customer metrics
    - Cover edge cases: no completed orders, single order, ties in recency
    - Place in `Customers` directory
    - _Requirements: 4.11_

- [x] 10. Implement IPaymentService and ICashFlowService with idempotency
  - [x] 10.1 Implement IPaymentService
    - Create/record PaymentRecord generating or linking to at most one CashFlowEntry per PaymentRecord
    - _Requirements: 4.18_

  - [x] 10.2 Write property test for payment-to-cashflow cardinality
    - **Property 13: Each PaymentRecord links to at most one CashFlowEntry**
    - **Validates: Requirements 4.18**
    - Place in `Cashflow` directory
    - _Requirements: 4.18_

  - [x] 10.3 Implement ICashFlowService
    - Record income/expense/receivable/payable, settle receivable/payable via `CashFlowSettlementStatus`, produce period summaries, and compute an integer cash-flow health score in [0,100]
    - _Requirements: 4.12_

  - [x] 10.4 Write property test for cash-flow health bounds
    - **Property 12: Cash-flow health score is a bounded integer**
    - **Validates: Requirements 4.12**
    - Place in `Cashflow` directory
    - _Requirements: 4.12_

  - [x] 10.5 Implement Business_Key idempotency for generated records
    - Before generating `PaymentRecord`, `CashFlowEntry`, `InventoryMovement`, `BusinessInsight`, or `BusinessMetricSnapshot`, look up by Business_Key and link/update rather than insert, so re-running completion/payment produces no duplicates
    - _Requirements: 4.20, 18.6_

  - [x] 10.6 Write property test for idempotency by Business_Key
    - **Property 14: Core writes are idempotent by Business_Key**
    - **Validates: Requirements 4.19, 4.20, 18.5, 18.6**
    - Place in `Commerce` directory
    - _Requirements: 4.19, 4.20, 18.5, 18.6_

- [x] 11. Checkpoint - core services
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. Implement Dashboard, Insight, and remaining CRUD services
  - [x] 12.1 Implement IDashboardService
    - Return a unified `DashboardSnapshot` with aggregate metrics and 7-day trend series
    - _Requirements: 4.13_

  - [x] 12.2 Implement IBusinessInsightService with reserved provider hook
    - Generate insights from deterministic local rules only (no LLM); expose the reserved `IBusinessInsightProvider` extension point
    - _Requirements: 4.14, 4.15_

  - [x] 12.3 Write unit tests for deterministic insight rules
    - Verify representative insight rules and confirm no LLM dependency
    - Place in `Analytics` directory
    - _Requirements: 4.14_

  - [x] 12.4 Implement IWorkspaceService, IUnitService, ISupplierService, IBusinessTaskService
    - Provide CRUD over `BusinessWorkspace`, `UnitDefinition`, `Supplier`, and `BusinessTask` (with status)
    - _Requirements: 4.1_

- [x] 13. Implement the template and customization system
  - [x] 13.1 Implement IBusinessTemplateService with JSON import/export and the built-in template
    - Support create/edit/activate/clone/import/export via JSON; provide exactly one built-in template (key `DefaultCommerce`, display name `默认经营模板`); activate `DefaultCommerce` when no template is explicitly active
    - Reject invalid/undefined-entity imports with `TemplateImportInvalid` leaving existing templates unchanged
    - _Requirements: 5.1, 5.2, 5.3, 5.7, 5.8_

  - [x] 13.2 Write property test for template JSON round-trip
    - **Property 15: Business template JSON round-trip preserves the template**
    - **Validates: Requirements 5.1**
    - Place in `Templates` directory
    - _Requirements: 5.1_

  - [x] 13.3 Write property test for invalid template import
    - **Property 16: Invalid template imports are rejected without side effects**
    - **Validates: Requirements 5.2**
    - Place in `Templates` directory
    - _Requirements: 5.2_

  - [x] 13.4 Implement ICustomFieldService and template page/workflow configuration
    - Associate each `CustomFieldDefinition` with exactly one entity type, 0–100 per type (101st rejected with `CustomFieldLimitExceeded`)
    - Implement page configuration (metric-card/table-column show-hide, default sort/unit/currency/order flow) and workflow configuration over the three independent stage dimensions with an initial stage per dimension and composite transitions
    - _Requirements: 5.4, 5.5, 5.6_

  - [x] 13.5 Write property test for custom-field bounds
    - **Property 17: Custom-field definitions are bounded and singly-typed**
    - **Validates: Requirements 5.4**
    - Place in `Templates` directory
    - _Requirements: 5.4_

  - [x] 13.6 Write property test for workflow-validated transitions
    - **Property 9: Stage transitions honor the active workflow with no partial update**
    - **Validates: Requirements 4.4, 4.5, 5.6**
    - Place in `Templates` directory
    - _Requirements: 4.4, 4.5, 5.6_

- [x] 14. Implement reserved external connectors (disabled in V1)
  - [x] 14.1 Declare reserved neutral connector interfaces and types
    - Declare `IExternalConnector`, `IExternalOrderConnector`, `IExternalInventoryConnector`, `ConnectorOptions`, `ConnectorHealthStatus` with no active runtime wiring; report `disabled` health at startup; invoking a disabled connector performs no outbound request, preserves local data, and returns a `ConnectorDisabled` result
    - _Requirements: 8.3, 8.4, 8.5, 8.6_

  - [x] 14.2 Write unit tests for connector-disabled behavior
    - Verify disabled health at startup and that invocation returns `ConnectorDisabled` with no outbound request
    - _Requirements: 8.4, 8.5, 8.6_

- [x] 15. Implement import/export (IImportExportService)
  - [x] 15.1 Implement CSV/XLSX import/export with deterministic matching
    - Export all records of a selected type to CSV/XLSX; for import validate → preview (Add/Update/Error/Conflict counts) → commit only Add/Update rows → report per-row failures → roll back on commit-level error with `CommitFailedRolledBack`
    - Apply deterministic match keys (Product: Code→Name; InventoryItem: Sku→Name; Customer: Phone→WeChat→Name; Order: OrderNo; CashFlowEntry: ImportBatchId+SourceRowKey); classify ambiguous fallback matches as Conflict (`ImportRowConflict`) and never silently update; keep all logic inside the service
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8_

  - [x] 15.2 Write property test for import matching, preview, and idempotent commit
    - **Property 18: Import matching is deterministic, preview counts are accurate, and commit is idempotent**
    - **Validates: Requirements 9.4, 9.5, 9.6, 9.7**
    - Place in `Commerce` directory
    - _Requirements: 9.4, 9.5, 9.6, 9.7_

- [x] 16. Checkpoint - templates, connectors, import/export
  - Ensure all tests pass, ask the user if questions arise.

- [x] 17. Remove legacy gateway and remote integration code
  - [x] 17.1 Remove legacy gateway/remote artifacts and preserve neutral security infrastructure
    - Delete the customer-specific gateway client, gateway order/business services, inventory gateway adapter, gateway options/env vars/action constants, and outbound configuration UI
    - Retain neutral security infrastructure (e.g., `OutboundEndpointPolicy`) under names free of any Forbidden_Term
    - _Requirements: 8.1, 8.2_

- [x] 18. Implement neutral demo and QA data
  - [x] 18.1 Implement the neutral demo dataset
    - Seed `客户 A/B`, `商品 A/B`, `库存项 A/B`, `供应商 A`, `订单 001`, `收入分类 A`, `支出分类 A`; at least one record per category (orders, order items, customers, inventory items, inventory movements, income, expense, receivable, payable), at least one low-stock item, and at least one generated insight; no English-style or industry-specific values
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_

  - [x] 18.2 Write unit tests for demo-data composition
    - Verify category coverage, the low-stock item, the generated insight, and absence of forbidden/English-style values
    - Place in `Analytics` directory
    - _Requirements: 10.3, 10.4, 10.5_

- [x] 19. Restructure the WPF UI shell and per-page ViewModels
  - [x] 19.1 Implement the nine-entry navigation shell with Chinese-only labels
    - Render `工作台`, `订单`, `商品`, `库存`, `客户`, `现金流`, `经营建议`, `设置`, `我的` in order, all visible without scrolling; selecting an entry shows its page within 1s and marks it active; no rendered English label
    - Treat Login, Settings, Order Fulfillment, and Exception Handling pages as in scope per C-1 while preserving P0 security behavior (C-2)
    - _Requirements: 6.1, 6.11, 6.12, 17.1, 17.3, 17.4_

  - [x] 19.2 Implement per-page ViewModels backed by the Commerce Service Layer
    - One dedicated ViewModel (or one delimited region per partial) per page; no `MainViewModel` partial exceeds 500 LOC; obtain data only through Commerce services with no legacy remote calls or legacy aggregation bindings
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.6, 7.7, 6.10_

  - [x] 19.3 Wire each page to its Commerce service with error and empty states
    - Bind Workbench→`IDashboardService`, Orders→`IOrderService`, Products→`IProductService`, Inventory→`IInventoryService`, Customers→`ICustomerService`, Cash Flow→`ICashFlowService`, Business Advice→`IBusinessInsightService`; Settings grouped with labels
    - On service failure show a page-level error, retain navigation/last-known state, never terminate or fall back to legacy; show explicit empty-state for empty result sets
    - _Requirements: 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8, 6.9, 6.13, 6.14, 7.5_

  - [x] 19.4 Write UI example/smoke tests for navigation and data-source wiring
    - Verify each page sources data from its Commerce service, navigation marks the active entry, and error/empty states render without termination
    - _Requirements: 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8, 6.12, 6.13, 6.14_

- [x] 20. Product identity cleanup
  - [x] 20.1 Apply neutral Orderly product identity 
    - Use the brand string "Orderly" in the window title bar, About page, `start-orderly.bat`, `README.md`, and `docs/`; present Chinese user-facing labels; include the seven-capability positioning text; resolve the app root to `%LocalAppData%\Orderly`
    - Ensure exactly one `start-orderly.bat` and zero `start-sn.bat`; refer to prior-install data as "legacy local data migration"; zero Forbidden_Terms in UI/title/docs
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9_

- [x] 21. Engineering cleanup
  - [x] 21.1 Remove legacy artifacts and update .gitignore (preserving user data)
    - Remove the unreferenced `src/scratch` directory, regenerated build artifacts, `bin_verify` directories, `*_wpftmp` residue, and legacy scripts referencing Forbidden_Terms; update `.gitignore` to exclude those patterns; do not touch real user local data (C-6)
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5, 14.6_

- [x] 22. QA scripts
  - [x] 22.1 Implement commerce smoke and universal regression scripts
    - Add `tools/qa/run-commerce-smoke.ps1` running the ordered commerce steps, exiting 0 on full success and non-zero at the first failed step with identification
    - Add `tools/qa/run-universal-regression.ps1` running build → test → forbidden-terms scan → security smoke → backup smoke → commerce smoke with the same pass/fail semantics; retain the existing P0 security smoke script unchanged; ensure no QA script references a Forbidden_Term
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 12.8_

- [x] 23. Documentation
  - [x] 23.1 Write generic README and docs
    - Rewrite root `README.md` to describe only the generic universal commerce system; create `docs/ORDERLY_UNIVERSAL_MODEL.md`, `docs/ORDERLY_DATA_MODEL.md`, `docs/ORDERLY_TEMPLATE_SYSTEM.md`, and `docs/ORDERLY_RELEASE_CHECKLIST.md`; ensure zero Forbidden_Terms and no reproduction of forbidden-term definitions
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7_

- [x] 24. P0 security preservation verification
  - [x] 24.1 Re-run the full P0 security suite and confirm zero regressions
    - Run the pre-existing P0_Security_System suite (SQLCipher encryption, local account system, launcher DB, multi-account structure, DPAPI key protection, field-level encryption, backup/restore, security audit, `LocalSessionContext`/`DataKey`); accept only with zero failures and zero new skips versus baseline
    - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.5, 15.6, 15.7, 15.8_

- [x] 25. Final integration: Core_Flow and acceptance gates
  - [x] 25.1 Wire the end-to-end Core_Flow integration test
    - Implement an automated test running the 13 ordered steps (create customer → create product → create inventory item → record inbound movement → create order → add order item → record payment → advance fulfillment → complete order → deduct inventory → generate cash flow → refresh workbench metrics → generate insights) with halt-and-preserve on failure
    - _Requirements: 16.9, 16.10_

  - [x] 25.2 Verify build/test acceptance gates
    - Confirm `dotnet clean` + `dotnet build Orderly.sln -c Debug` completes with zero errors and no new unresolved warnings, `dotnet test` reports zero failures and no new unjustified skips, QA scripts return terminal success, and the forbidden-terms scan returns zero matches
    - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5, 16.6, 16.7, 16.8_

- [x] 26. Final checkpoint - full system
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are TEST tasks, not optional tasks. They MAY be executed after their corresponding implementation task rather than immediately alongside it, but they are REQUIRED for final acceptance and MUST NOT be skipped. Core implementation sub-tasks (those without `*`) are never optional either.
- The following tests in particular MUST NOT be skipped: core correctness tests, all property tests (Properties 1–19), the forbidden-terms regression (1.2), import idempotency tests (15.2), transaction/atomicity tests (7.6, 7.7, 10.6), P0 security tests (24.1), and Core_Flow tests (25.1).
- Each task references specific granular requirements for traceability.
- Property tests (Properties 1–19) validate universal correctness over generated inputs (minimum 100 iterations each) and live next to the code they cover, in the design's property-to-directory mapping.
- Non-PBT criteria — the forbidden-terms scan, P0 security preservation, UI timing/wiring, and the end-to-end Core_Flow — are covered by regression, integration, and smoke tests rather than properties.
- Checkpoints provide incremental validation; the P0_Security_System is preserved with zero regressions throughout (C-2).

## Execution Strategy

- Do NOT use "Run All Tasks" to execute the full plan in one pass. Execute by waves/checkpoints so cleanup and acceptance gates land in the correct order.
- Recommended execution sequence:
  - **Wave 0–4**: domain model, schema, repositories, transaction foundation.
  - **Wave 5–8**: core services; order/inventory/cashflow/customer logic; property tests.
  - **Wave 9–10**: template system, import/export, connector-disabled behavior, demo data, legacy cleanup.
  - **Wave 11–13**: WPF shell, per-page ViewModels, product identity, docs, QA scripts.
  - **Wave 14–15**: P0 security verification, Core_Flow, final acceptance gates.
- `ForbiddenTermsRegressionTests` may exist and fail from an early wave; it is only pass-gated in the final acceptance wave (Wave 14–15), after gateway, product identity, docs, and engineering cleanup are complete.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "2.1"] },
    { "id": 1, "tasks": ["1.2", "2.2", "3.1"] },
    { "id": 2, "tasks": ["3.2", "3.3", "3.4"] },
    { "id": 3, "tasks": ["4.1", "4.2", "4.3"] },
    { "id": 4, "tasks": ["4.4", "4.5", "6.1", "14.1"] },
    { "id": 5, "tasks": ["6.2", "6.3", "7.1", "8.1", "9.1", "14.2"] },
    { "id": 6, "tasks": ["7.2", "7.3", "8.2", "9.2", "10.1", "10.3", "12.1", "12.2", "12.4"] },
    { "id": 7, "tasks": ["7.4", "7.5", "10.2", "10.4", "10.5", "12.3", "13.1"] },
    { "id": 8, "tasks": ["7.6", "7.7", "10.6", "13.2", "13.3", "13.4"] },
    { "id": 9, "tasks": ["13.5", "13.6", "15.1", "17.1", "18.1"] },
    { "id": 10, "tasks": ["15.2", "18.2", "19.1"] },
    { "id": 11, "tasks": ["19.2"] },
    { "id": 12, "tasks": ["19.3", "20.1"] },
    { "id": 13, "tasks": ["19.4", "21.1", "22.1", "23.1"] },
    { "id": 14, "tasks": ["24.1", "25.1"] },
    { "id": 15, "tasks": ["25.2"] }
  ]
}
```
