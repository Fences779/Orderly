-- Orderly Cloud Team Server initial PostgreSQL schema
-- Created by AI implementation agent. Follows ORDERLY_TEAM_CLOUD_AI_IMPLEMENTATION_PLAN.md.

-- ---------------------------------------------------------------------------
-- Cloud account & workspace tables
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "CloudUsers" (
    "Id" UUID PRIMARY KEY,
    "Username" TEXT NOT NULL UNIQUE,
    "DisplayName" TEXT NOT NULL,
    "PasswordHash" TEXT NOT NULL,
    "IsEnabled" BOOLEAN NOT NULL DEFAULT TRUE,
    "TokenVersion" INTEGER NOT NULL DEFAULT 1,
    "PasswordChangedAt" TIMESTAMPTZ NULL,
    "FailedLoginCount" INTEGER NOT NULL DEFAULT 0,
    "LockedUntil" TIMESTAMPTZ NULL,
    "CreatedByUserId" UUID NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DisabledAt" TIMESTAMPTZ NULL,
    "DisabledByUserId" UUID NULL
);

CREATE TABLE IF NOT EXISTS "CloudWorkspaces" (
    "Id" UUID PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "DefaultCurrencyCode" TEXT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS "CloudWorkspaceMembers" (
    "WorkspaceId" UUID NOT NULL,
    "UserId" UUID NOT NULL,
    "CloudRole" TEXT NOT NULL,
    "BusinessLabel" TEXT NOT NULL,
    "RolePolicyVersion" INTEGER NOT NULL DEFAULT 1,
    "IsEnabled" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedByUserId" UUID NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedByUserId" UUID NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    PRIMARY KEY ("WorkspaceId", "UserId"),
    CONSTRAINT fk_cwm_workspace FOREIGN KEY ("WorkspaceId") REFERENCES "CloudWorkspaces"("Id"),
    CONSTRAINT fk_cwm_user FOREIGN KEY ("UserId") REFERENCES "CloudUsers"("Id")
);

CREATE TABLE IF NOT EXISTS "CloudRefreshTokens" (
    "Id" UUID PRIMARY KEY,
    "UserId" UUID NOT NULL REFERENCES "CloudUsers"("Id"),
    "TokenFamilyId" UUID NOT NULL,
    "TokenHash" TEXT NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "ExpiresAt" TIMESTAMPTZ NOT NULL,
    "RevokedAt" TIMESTAMPTZ NULL,
    "RevokedReason" TEXT NULL,
    "ReplacedByTokenId" UUID NULL
);

CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user ON "CloudRefreshTokens"("UserId");
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_family ON "CloudRefreshTokens"("TokenFamilyId");

-- ---------------------------------------------------------------------------
-- Audit, presence, price-change, inventory audit, export
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "CloudAuditLogs" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL REFERENCES "CloudWorkspaces"("Id"),
    "ActorUserId" UUID NULL REFERENCES "CloudUsers"("Id"),
    "ActorDisplayName" TEXT NOT NULL,
    "ActorRole" TEXT NOT NULL,
    "Action" TEXT NOT NULL,
    "EntityType" TEXT NOT NULL,
    "EntityId" UUID NULL,
    "BeforeJson" TEXT NULL,
    "AfterJson" TEXT NULL,
    "Reason" TEXT NULL,
    "ClientRequestId" TEXT NULL,
    "OccurredAt" TIMESTAMPTZ NOT NULL,
    "IpAddress" TEXT NULL,
    "UserAgent" TEXT NULL,
    "DeviceId" TEXT NULL,
    "Result" TEXT NOT NULL DEFAULT 'Succeeded',
    "CorrelationId" TEXT NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS ix_audit_workspace ON "CloudAuditLogs"("WorkspaceId", "OccurredAt" DESC);
CREATE INDEX IF NOT EXISTS ix_audit_entity ON "CloudAuditLogs"("EntityType", "EntityId");
CREATE INDEX IF NOT EXISTS ix_audit_correlation ON "CloudAuditLogs"("WorkspaceId", "CorrelationId");
CREATE INDEX IF NOT EXISTS ix_audit_result ON "CloudAuditLogs"("WorkspaceId", "Result", "OccurredAt" DESC);

CREATE TABLE IF NOT EXISTS "CloudEditPresences" (
    "WorkspaceId" UUID NOT NULL,
    "EntityType" TEXT NOT NULL,
    "EntityId" UUID NOT NULL,
    "UserId" UUID NOT NULL REFERENCES "CloudUsers"("Id"),
    "DisplayName" TEXT NOT NULL,
    "ConnectionId" TEXT NOT NULL,
    "StartedAt" TIMESTAMPTZ NOT NULL,
    "LastHeartbeatAt" TIMESTAMPTZ NOT NULL,
    "ExpiresAt" TIMESTAMPTZ NOT NULL,
    PRIMARY KEY ("WorkspaceId", "EntityType", "EntityId", "UserId")
);
CREATE INDEX IF NOT EXISTS ix_presence_expires ON "CloudEditPresences"("ExpiresAt");

CREATE TABLE IF NOT EXISTS "CloudPriceChangeRequests" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL REFERENCES "CloudWorkspaces"("Id"),
    "ProductId" UUID NOT NULL,
    "CurrentPrice" NUMERIC(18,2) NOT NULL,
    "ProposedPrice" NUMERIC(18,2) NOT NULL,
    "Reason" TEXT NULL,
    "Status" TEXT NOT NULL,
    "RequestedByUserId" UUID NOT NULL REFERENCES "CloudUsers"("Id"),
    "RequestedAt" TIMESTAMPTZ NOT NULL,
    "ReviewedByUserId" UUID NULL REFERENCES "CloudUsers"("Id"),
    "ReviewedAt" TIMESTAMPTZ NULL,
    "ReviewNote" TEXT NULL,
    "AppliedProductRevision" BIGINT NULL
);
CREATE INDEX IF NOT EXISTS ix_price_change_workspace ON "CloudPriceChangeRequests"("WorkspaceId", "Status", "RequestedAt" DESC);

CREATE TABLE IF NOT EXISTS "CloudInventoryMovementAudits" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL REFERENCES "CloudWorkspaces"("Id"),
    "InventoryItemId" UUID NOT NULL,
    "MovementId" UUID NOT NULL,
    "MovementType" TEXT NOT NULL,
    "QuantityBefore" NUMERIC(18,4) NOT NULL,
    "QuantityDelta" NUMERIC(18,4) NOT NULL,
    "QuantityAfter" NUMERIC(18,4) NOT NULL,
    "Reason" TEXT NULL,
    "IsStocktake" BOOLEAN NOT NULL DEFAULT FALSE,
    "ActorUserId" UUID NOT NULL REFERENCES "CloudUsers"("Id"),
    "OccurredAt" TIMESTAMPTZ NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_inv_audit_item ON "CloudInventoryMovementAudits"("WorkspaceId", "InventoryItemId", "OccurredAt" DESC);

CREATE TABLE IF NOT EXISTS "CloudExportJobs" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL REFERENCES "CloudWorkspaces"("Id"),
    "RequestedByUserId" UUID NOT NULL REFERENCES "CloudUsers"("Id"),
    "Scope" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "FileName" TEXT NULL,
    "FilePath" TEXT NULL,
    "ErrorMessage" TEXT NULL,
    "AttemptCount" INTEGER NOT NULL DEFAULT 0,
    "LastAttemptAt" TIMESTAMPTZ NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "CompletedAt" TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS ix_export_jobs_workspace ON "CloudExportJobs"("WorkspaceId", "CreatedAt" DESC);

-- ---------------------------------------------------------------------------
-- Emergency drafts
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "CloudEmergencyDrafts" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL REFERENCES "CloudWorkspaces"("Id"),
    "SubmittedByUserId" UUID NOT NULL REFERENCES "CloudUsers"("Id"),
    "EntityType" TEXT NOT NULL,
    "EntityId" UUID NULL,
    "OperationType" TEXT NOT NULL,
    "PayloadJson" TEXT NOT NULL,
    "BaseRevision" BIGINT NULL,
    "Status" TEXT NOT NULL,
    "LastSubmitError" TEXT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "SubmittedAt" TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS ix_emergency_drafts_workspace_status ON "CloudEmergencyDrafts"("WorkspaceId", "Status", "CreatedAt");

-- ---------------------------------------------------------------------------
-- Sync, idempotency, import
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "CloudWorkspaceSyncState" (
    "WorkspaceId" UUID PRIMARY KEY REFERENCES "CloudWorkspaces"("Id"),
    "LastSequence" BIGINT NOT NULL DEFAULT 0,
    "UpdatedAt" TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS "CloudChangeLog" (
    "WorkspaceId" UUID NOT NULL REFERENCES "CloudWorkspaces"("Id"),
    "Sequence" BIGINT NOT NULL,
    "EntityType" TEXT NOT NULL,
    "EntityId" UUID NULL,
    "Action" TEXT NOT NULL,
    "Revision" BIGINT NULL,
    "ActorUserId" UUID NULL REFERENCES "CloudUsers"("Id"),
    "OccurredAt" TIMESTAMPTZ NOT NULL,
    "PayloadHintJson" TEXT NULL,
    PRIMARY KEY ("WorkspaceId", "Sequence")
);
CREATE INDEX IF NOT EXISTS ix_changelog_occurred ON "CloudChangeLog"("WorkspaceId", "OccurredAt" DESC);

CREATE TABLE IF NOT EXISTS "CloudIdempotencyKeys" (
    "WorkspaceId" UUID NOT NULL REFERENCES "CloudWorkspaces"("Id"),
    "UserId" UUID NOT NULL REFERENCES "CloudUsers"("Id"),
    "Action" TEXT NOT NULL,
    "ClientRequestId" TEXT NOT NULL,
    "RequestHash" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "ResponseStatusCode" INTEGER NULL,
    "ResponseBodyJson" TEXT NULL,
    "ResourceType" TEXT NULL,
    "ResourceId" UUID NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "CompletedAt" TIMESTAMPTZ NULL,
    PRIMARY KEY ("WorkspaceId", "UserId", "Action", "ClientRequestId")
);

CREATE TABLE IF NOT EXISTS "CloudImportBatches" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL REFERENCES "CloudWorkspaces"("Id"),
    "SourceInstanceId" UUID NOT NULL,
    "SourceFingerprint" TEXT NOT NULL,
    "SourceReportJson" TEXT NOT NULL,
    "ResultJson" TEXT NULL,
    "Status" TEXT NOT NULL,
    "RequestedByUserId" UUID NOT NULL REFERENCES "CloudUsers"("Id"),
    "DryRunAt" TIMESTAMPTZ NOT NULL,
    "CommittedAt" TIMESTAMPTZ NULL,
    "RolledBackAt" TIMESTAMPTZ NULL,
    "ErrorMessage" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "CloudImportEntityMap" (
    "WorkspaceId" UUID NOT NULL,
    "SourceInstanceId" UUID NOT NULL,
    "EntityType" TEXT NOT NULL,
    "SourceLocalEntityId" TEXT NOT NULL,
    "TargetEntityId" UUID NOT NULL,
    "FirstImportBatchId" UUID NOT NULL REFERENCES "CloudImportBatches"("Id"),
    "LastImportBatchId" UUID NOT NULL REFERENCES "CloudImportBatches"("Id"),
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    PRIMARY KEY ("WorkspaceId", "SourceInstanceId", "EntityType", "SourceLocalEntityId")
);

-- ---------------------------------------------------------------------------
-- Commerce tables (cloud mirror of local SQLite Commerce tables)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "CommerceBusinessWorkspaces" (
    "Id" UUID PRIMARY KEY,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "Name" TEXT NOT NULL,
    "ActiveTemplateId" UUID NULL,
    "DefaultCurrencyCode" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "CommerceBusinessTemplates" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "TemplateKey" TEXT NOT NULL,
    "IsBuiltIn" BOOLEAN NOT NULL DEFAULT FALSE,
    "DisplayName" TEXT NOT NULL,
    "ConfigJson" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "CommerceCustomFieldDefinitions" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "TemplateId" UUID NULL,
    "TargetEntityType" TEXT NOT NULL,
    "DataType" INTEGER NOT NULL,
    "FieldKey" TEXT NOT NULL,
    "DisplayName" TEXT NOT NULL,
    "IsRequired" BOOLEAN NOT NULL DEFAULT FALSE,
    "SortOrder" INTEGER NOT NULL DEFAULT 0,
    "OptionsJson" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "CommerceUnitDefinitions" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "TemplateId" UUID NULL,
    "Code" TEXT NOT NULL,
    "IsBuiltIn" BOOLEAN NOT NULL DEFAULT FALSE,
    "DisplayName" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "CommerceProducts" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "Name" TEXT NOT NULL,
    "Code" TEXT NOT NULL,
    "ProductType" INTEGER NOT NULL,
    "Description" TEXT NULL,
    "DefaultUnitId" UUID NULL,
    "SupplierId" UUID NULL,
    "DefaultPrice" NUMERIC(18,2) NOT NULL,
    "DefaultCost" NUMERIC(18,2) NULL
);
CREATE INDEX IF NOT EXISTS ix_products_workspace ON "CommerceProducts"("WorkspaceId", "Lifecycle", "Name");

CREATE TABLE IF NOT EXISTS "CommerceProductVariants" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "ProductId" UUID NOT NULL,
    "Name" TEXT NOT NULL,
    "Sku" TEXT NULL,
    "PriceAdjustment" NUMERIC(18,2) NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_product_variants_product ON "CommerceProductVariants"("WorkspaceId", "ProductId");

CREATE TABLE IF NOT EXISTS "CommerceInventoryItems" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "Name" TEXT NOT NULL,
    "Sku" TEXT NULL,
    "ProductId" UUID NULL,
    "ProductVariantId" UUID NULL,
    "UnitId" UUID NULL,
    "QuantityAvailable" NUMERIC(18,4) NOT NULL DEFAULT 0,
    "ReorderThreshold" NUMERIC(18,4) NOT NULL DEFAULT 0,
    "UnitCost" NUMERIC(18,2) NULL
);
CREATE INDEX IF NOT EXISTS ix_inventory_workspace ON "CommerceInventoryItems"("WorkspaceId", "Lifecycle", "Name");

CREATE TABLE IF NOT EXISTS "CommerceInventoryMovements" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "InventoryItemId" UUID NOT NULL,
    "MovementType" INTEGER NOT NULL,
    "Quantity" NUMERIC(18,4) NOT NULL,
    "SupplierId" UUID NULL,
    "OrderId" UUID NULL,
    "OccurredAt" TIMESTAMPTZ NOT NULL,
    "BusinessKey" TEXT NULL,
    "Note" TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_inventory_movements_item ON "CommerceInventoryMovements"("WorkspaceId", "InventoryItemId", "OccurredAt" DESC);

CREATE TABLE IF NOT EXISTS "CommerceCustomers" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "Name" TEXT NOT NULL,
    "Phone" TEXT NULL,
    "WeChat" TEXT NULL,
    "Email" TEXT NULL,
    "LastOrderAt" TIMESTAMPTZ NULL,
    "CompletedOrderCount" INTEGER NOT NULL DEFAULT 0,
    "TotalSpend" NUMERIC(18,2) NOT NULL DEFAULT 0,
    "AssignedToUserId" UUID NULL
);
CREATE INDEX IF NOT EXISTS ix_customers_workspace ON "CommerceCustomers"("WorkspaceId", "Lifecycle", "Name");

CREATE TABLE IF NOT EXISTS "CommerceCustomerContacts" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "CustomerId" UUID NOT NULL,
    "Name" TEXT NOT NULL,
    "Phone" TEXT NULL,
    "Email" TEXT NULL,
    "Role" TEXT NULL,
    "IsPrimary" BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS "CommerceOrders" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "OrderNo" TEXT NOT NULL,
    "CustomerId" UUID NULL,
    "SalesStage" INTEGER NOT NULL,
    "PaymentStage" INTEGER NOT NULL,
    "FulfillmentStage" INTEGER NOT NULL,
    "Subtotal" NUMERIC(18,2) NOT NULL DEFAULT 0,
    "Total" NUMERIC(18,2) NOT NULL DEFAULT 0,
    "Cost" NUMERIC(18,2) NULL,
    "GrossProfit" NUMERIC(18,2) NULL,
    "GrossMargin" NUMERIC(18,4) NULL,
    "PaidAmount" NUMERIC(18,2) NOT NULL DEFAULT 0,
    "ReceivableAmount" NUMERIC(18,2) NOT NULL DEFAULT 0,
    "OrderedAt" TIMESTAMPTZ NOT NULL,
    "Note" TEXT NULL,
    "AssignedToUserId" UUID NULL
);
CREATE INDEX IF NOT EXISTS ix_orders_workspace ON "CommerceOrders"("WorkspaceId", "Lifecycle", "OrderedAt" DESC);
CREATE UNIQUE INDEX IF NOT EXISTS ux_orders_workspace_order_no ON "CommerceOrders"("WorkspaceId", "OrderNo") WHERE "DeletedAt" IS NULL;

CREATE TABLE IF NOT EXISTS "CommerceOrderItems" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "OrderId" UUID NOT NULL,
    "ProductId" UUID NULL,
    "ProductVariantId" UUID NULL,
    "InventoryItemId" UUID NULL,
    "UnitId" UUID NULL,
    "Description" TEXT NOT NULL,
    "Quantity" NUMERIC(18,4) NOT NULL,
    "UnitPrice" NUMERIC(18,2) NOT NULL,
    "UnitCost" NUMERIC(18,2) NULL,
    "LineTotal" NUMERIC(18,2) NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_order_items_order ON "CommerceOrderItems"("WorkspaceId", "OrderId");

CREATE TABLE IF NOT EXISTS "CommercePaymentRecords" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "OrderId" UUID NULL,
    "CashFlowEntryId" UUID NULL,
    "Amount" NUMERIC(18,2) NOT NULL,
    "PaidAt" TIMESTAMPTZ NOT NULL,
    "Method" INTEGER NOT NULL DEFAULT 0,
    "BusinessKey" TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_records_workspace_business_key ON "CommercePaymentRecords"("WorkspaceId", "BusinessKey") WHERE "BusinessKey" IS NOT NULL AND "DeletedAt" IS NULL;

CREATE TABLE IF NOT EXISTS "CommerceCashFlowEntries" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "Direction" INTEGER NOT NULL,
    "Amount" NUMERIC(18,2) NOT NULL,
    "SettledAmount" NUMERIC(18,2) NOT NULL DEFAULT 0,
    "SettlementStatus" INTEGER NOT NULL,
    "OccurredAt" TIMESTAMPTZ NOT NULL,
    "DueDate" TIMESTAMPTZ NULL,
    "CategoryName" TEXT NOT NULL,
    "OrderId" UUID NULL,
    "PaymentRecordId" UUID NULL,
    "ImportBatchId" UUID NULL,
    "SourceRowKey" TEXT NULL,
    "BusinessKey" TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_cashflow_workspace_business_key ON "CommerceCashFlowEntries"("WorkspaceId", "BusinessKey") WHERE "BusinessKey" IS NOT NULL AND "DeletedAt" IS NULL;

CREATE TABLE IF NOT EXISTS "CommerceSuppliers" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "Name" TEXT NOT NULL,
    "ContactName" TEXT NULL,
    "Phone" TEXT NULL,
    "Email" TEXT NULL,
    "Address" TEXT NULL,
    "Note" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "CommerceBusinessTasks" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "Title" TEXT NOT NULL,
    "Description" TEXT NULL,
    "Status" INTEGER NOT NULL,
    "DueDate" TIMESTAMPTZ NULL,
    "CompletedAt" TIMESTAMPTZ NULL,
    "CustomerId" UUID NULL,
    "OrderId" UUID NULL,
    "AssignedToUserId" UUID NULL
);

CREATE TABLE IF NOT EXISTS "CommerceBusinessInsights" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "Severity" INTEGER NOT NULL,
    "Title" TEXT NOT NULL,
    "Message" TEXT NOT NULL,
    "Category" TEXT NOT NULL,
    "IsAcknowledged" BOOLEAN NOT NULL DEFAULT FALSE,
    "GeneratedAt" TIMESTAMPTZ NOT NULL,
    "BusinessKey" TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_insights_workspace_business_key ON "CommerceBusinessInsights"("WorkspaceId", "BusinessKey") WHERE "BusinessKey" IS NOT NULL AND "DeletedAt" IS NULL;

CREATE TABLE IF NOT EXISTS "CommerceBusinessMetricSnapshots" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    "DeletedAt" TIMESTAMPTZ NULL,
    "Lifecycle" INTEGER NOT NULL DEFAULT 0,
    "CustomFieldsJson" TEXT NULL,
    "Revision" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL,
    "UpdatedByUserId" UUID NULL,
    "ArchivedByUserId" UUID NULL,
    "ArchiveReason" TEXT NULL,
    "LastChangeSequence" BIGINT NOT NULL DEFAULT 0,
    "MetricKey" TEXT NOT NULL,
    "CapturedAt" TIMESTAMPTZ NOT NULL,
    "NumericValue" NUMERIC(18,4) NULL,
    "MoneyValue" NUMERIC(18,2) NULL,
    "BusinessKey" TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_metric_snapshots_workspace_business_key ON "CommerceBusinessMetricSnapshots"("WorkspaceId", "BusinessKey") WHERE "BusinessKey" IS NOT NULL AND "DeletedAt" IS NULL;
