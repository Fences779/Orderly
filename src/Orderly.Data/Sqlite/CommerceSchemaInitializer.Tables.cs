namespace Orderly.Data.Sqlite;

/// <summary>
/// The declarative Commerce schema: one <see cref="CommerceTableDefinition"/> per Universal_Domain_Model
/// entity. Column shapes are derived directly from the entity definitions in
/// <c>Orderly.Core/Commerce</c>. Shared base columns (from <c>CommerceEntity</c> and
/// <c>WorkspaceScopedEntity</c>) are factored into helpers so every table carries the same audit,
/// lifecycle, and personalization surface (Requirements 2.4, 2.5, 2.7, 2.8, 2.9).
/// </summary>
public sealed partial class CommerceSchemaInitializer
{
    // Shared base column fragments (see Orderly.Core/Commerce/CommerceEntity.cs).
    private const string IdColumn = "TEXT NOT NULL PRIMARY KEY";          // Guid stored as TEXT
    private const string CreatedAtColumn = "TEXT NOT NULL";              // UTC, 'O' round-trip
    private const string UpdatedAtColumn = "TEXT NOT NULL";              // UTC, 'O' round-trip
    private const string DeletedAtColumn = "TEXT NULL";                  // nullable UTC
    private const string LifecycleColumn = "INTEGER NOT NULL DEFAULT 0"; // EntityLifecycleStatus
    private const string CustomFieldsJsonColumn = "TEXT NULL";           // single personalization field

    // Money / exact-decimal columns are TEXT to preserve scale-2 decimal exactness (Req 2.6).
    private const string Money = "TEXT NOT NULL DEFAULT '0.00'";
    private const string MoneyNullable = "TEXT NULL";
    private const string DecimalQuantity = "TEXT NOT NULL DEFAULT '0'";

    private const string GuidNotNull = "TEXT NOT NULL";
    private const string GuidNullable = "TEXT NULL";
    private const string Enum = "INTEGER NOT NULL DEFAULT 0";
    private const string Bool = "INTEGER NOT NULL DEFAULT 0";
    private const string TextNotNull = "TEXT NOT NULL DEFAULT ''";
    private const string TextNullable = "TEXT NULL";
    private const string TimestampNotNull = "TEXT NOT NULL";
    private const string TimestampNullable = "TEXT NULL";
    private const string IntNotNull = "INTEGER NOT NULL DEFAULT 0";

    private static IReadOnlyList<CommerceColumn> BaseColumns() => new List<CommerceColumn>
    {
        new("Id", IdColumn),
        new("CreatedAt", CreatedAtColumn),
        new("UpdatedAt", UpdatedAtColumn),
        new("DeletedAt", DeletedAtColumn),
        new("Lifecycle", LifecycleColumn),
        new("CustomFieldsJson", CustomFieldsJsonColumn),
    };

    /// <summary>Builds a column list starting with the shared base columns plus, optionally, WorkspaceId.</summary>
    private static List<CommerceColumn> Columns(bool workspaceScoped, params CommerceColumn[] entityColumns)
    {
        var columns = new List<CommerceColumn>(BaseColumns());
        if (workspaceScoped)
        {
            columns.Add(new CommerceColumn("WorkspaceId", GuidNotNull));
        }

        columns.AddRange(entityColumns);
        return columns;
    }

    /// <summary>
    /// The complete set of Commerce tables (18 entities). Table names use the <c>Commerce</c>
    /// prefix so they never collide with the legacy <c>Customers</c>/<c>Orders</c> tables.
    /// </summary>
    private static readonly IReadOnlyList<CommerceTableDefinition> Tables = new List<CommerceTableDefinition>
    {
        // --- System / configuration entities (not workspace-scoped) ---

        // BusinessWorkspace (the scoping root; no WorkspaceId).
        new("CommerceBusinessWorkspaces", Columns(
            workspaceScoped: false,
            new CommerceColumn("Name", TextNotNull),
            new CommerceColumn("ActiveTemplateId", GuidNullable),
            new CommerceColumn("DefaultCurrencyCode", TextNullable))),

        // BusinessTemplate (built-in => WorkspaceId null; workspace-owned => WorkspaceId set).
        new("CommerceBusinessTemplates", Columns(
            workspaceScoped: false,
            new CommerceColumn("TemplateKey", TextNotNull),
            new CommerceColumn("WorkspaceId", GuidNullable),
            new CommerceColumn("IsBuiltIn", Bool),
            new CommerceColumn("DisplayName", TextNotNull),
            new CommerceColumn("ConfigJson", TextNullable))),

        // CustomFieldDefinition (template-scoped).
        new("CommerceCustomFieldDefinitions", Columns(
            workspaceScoped: false,
            new CommerceColumn("TemplateId", GuidNotNull),
            new CommerceColumn("TargetEntityType", Enum),
            new CommerceColumn("DataType", Enum),
            new CommerceColumn("FieldKey", TextNotNull),
            new CommerceColumn("DisplayName", TextNotNull),
            new CommerceColumn("IsRequired", Bool),
            new CommerceColumn("SortOrder", IntNotNull),
            new CommerceColumn("OptionsJson", TextNullable))),

        // UnitDefinition (built-in => TemplateId null; user-defined => TemplateId set).
        new("CommerceUnitDefinitions", Columns(
            workspaceScoped: false,
            new CommerceColumn("TemplateId", GuidNullable),
            new CommerceColumn("Code", TextNotNull),
            new CommerceColumn("IsBuiltIn", Bool),
            new CommerceColumn("DisplayName", TextNotNull))),

        // --- Workspace-scoped business entities ---

        new("CommerceProducts", Columns(
            workspaceScoped: true,
            new CommerceColumn("Name", TextNotNull),
            new CommerceColumn("Code", TextNullable),
            new CommerceColumn("ProductType", Enum),
            new CommerceColumn("Description", TextNullable),
            new CommerceColumn("DefaultUnitId", GuidNullable),
            new CommerceColumn("SupplierId", GuidNullable),
            new CommerceColumn("DefaultPrice", Money),
            new CommerceColumn("DefaultCost", Money))),

        new("CommerceProductVariants", Columns(
            workspaceScoped: true,
            new CommerceColumn("ProductId", GuidNotNull),
            new CommerceColumn("Name", TextNotNull),
            new CommerceColumn("Sku", TextNullable),
            new CommerceColumn("PriceAdjustment", Money))),

        new("CommerceInventoryItems", Columns(
            workspaceScoped: true,
            new CommerceColumn("Name", TextNotNull),
            new CommerceColumn("Sku", TextNullable),
            new CommerceColumn("ProductId", GuidNullable),
            new CommerceColumn("ProductVariantId", GuidNullable),
            new CommerceColumn("UnitId", GuidNullable),
            new CommerceColumn("QuantityAvailable", DecimalQuantity),
            new CommerceColumn("ReorderThreshold", DecimalQuantity),
            new CommerceColumn("UnitCost", Money))),

        new("CommerceInventoryMovements", Columns(
            workspaceScoped: true,
            new CommerceColumn("InventoryItemId", GuidNotNull),
            new CommerceColumn("MovementType", Enum),
            new CommerceColumn("Quantity", DecimalQuantity),
            new CommerceColumn("SupplierId", GuidNullable),
            new CommerceColumn("OrderId", GuidNullable),
            new CommerceColumn("OccurredAt", TimestampNotNull),
            new CommerceColumn("BusinessKey", TextNullable),
            new CommerceColumn("Note", TextNullable))),

        new("CommerceCustomers", Columns(
            workspaceScoped: true,
            new CommerceColumn("Name", TextNotNull),
            new CommerceColumn("Phone", TextNullable),
            new CommerceColumn("WeChat", TextNullable),
            new CommerceColumn("Email", TextNullable),
            new CommerceColumn("LastOrderAt", TimestampNullable),
            new CommerceColumn("CompletedOrderCount", IntNotNull),
            new CommerceColumn("TotalSpend", Money))),

        new("CommerceCustomerContacts", Columns(
            workspaceScoped: true,
            new CommerceColumn("CustomerId", GuidNotNull),
            new CommerceColumn("Name", TextNotNull),
            new CommerceColumn("Phone", TextNullable),
            new CommerceColumn("Email", TextNullable),
            new CommerceColumn("Role", TextNullable),
            new CommerceColumn("IsPrimary", Bool))),

        new("CommerceOrders", Columns(
            workspaceScoped: true,
            new CommerceColumn("OrderNo", TextNullable),
            new CommerceColumn("CustomerId", GuidNullable),
            new CommerceColumn("SalesStage", Enum),
            new CommerceColumn("PaymentStage", Enum),
            new CommerceColumn("FulfillmentStage", Enum),
            new CommerceColumn("Subtotal", Money),
            new CommerceColumn("Total", Money),
            new CommerceColumn("Cost", Money),
            new CommerceColumn("GrossProfit", Money),
            new CommerceColumn("GrossMargin", DecimalQuantity),
            new CommerceColumn("PaidAmount", Money),
            new CommerceColumn("ReceivableAmount", Money),
            new CommerceColumn("OrderedAt", TimestampNotNull),
            new CommerceColumn("Note", TextNullable))),

        new("CommerceOrderItems", Columns(
            workspaceScoped: true,
            new CommerceColumn("OrderId", GuidNotNull),
            new CommerceColumn("ProductId", GuidNullable),
            new CommerceColumn("ProductVariantId", GuidNullable),
            new CommerceColumn("InventoryItemId", GuidNullable),
            new CommerceColumn("UnitId", GuidNullable),
            new CommerceColumn("Description", TextNullable),
            new CommerceColumn("Quantity", DecimalQuantity),
            new CommerceColumn("UnitPrice", Money),
            new CommerceColumn("UnitCost", Money),
            new CommerceColumn("LineTotal", Money))),

        new("CommercePaymentRecords", Columns(
            workspaceScoped: true,
            new CommerceColumn("OrderId", GuidNullable),
            new CommerceColumn("CashFlowEntryId", GuidNullable),
            new CommerceColumn("Amount", Money),
            new CommerceColumn("PaidAt", TimestampNotNull),
            new CommerceColumn("Method", TextNullable),
            new CommerceColumn("BusinessKey", TextNullable))),

        new("CommerceCashFlowEntries", Columns(
            workspaceScoped: true,
            new CommerceColumn("Direction", Enum),
            new CommerceColumn("Amount", Money),
            new CommerceColumn("SettledAmount", Money),
            new CommerceColumn("SettlementStatus", Enum),
            new CommerceColumn("OccurredAt", TimestampNotNull),
            new CommerceColumn("DueDate", TimestampNullable),
            new CommerceColumn("CategoryName", TextNullable),
            new CommerceColumn("OrderId", GuidNullable),
            new CommerceColumn("PaymentRecordId", GuidNullable),
            new CommerceColumn("ImportBatchId", TextNullable),
            new CommerceColumn("SourceRowKey", TextNullable),
            new CommerceColumn("BusinessKey", TextNullable))),

        new("CommerceSuppliers", Columns(
            workspaceScoped: true,
            new CommerceColumn("Name", TextNotNull),
            new CommerceColumn("ContactName", TextNullable),
            new CommerceColumn("Phone", TextNullable),
            new CommerceColumn("Email", TextNullable),
            new CommerceColumn("Address", TextNullable),
            new CommerceColumn("Note", TextNullable))),

        new("CommerceBusinessTasks", Columns(
            workspaceScoped: true,
            new CommerceColumn("Title", TextNotNull),
            new CommerceColumn("Description", TextNullable),
            new CommerceColumn("Status", Enum),
            new CommerceColumn("DueDate", TimestampNullable),
            new CommerceColumn("CompletedAt", TimestampNullable),
            new CommerceColumn("CustomerId", GuidNullable),
            new CommerceColumn("OrderId", GuidNullable))),

        new("CommerceBusinessInsights", Columns(
            workspaceScoped: true,
            new CommerceColumn("Severity", Enum),
            new CommerceColumn("Title", TextNotNull),
            new CommerceColumn("Message", TextNotNull),
            new CommerceColumn("Category", TextNullable),
            new CommerceColumn("IsAcknowledged", Bool),
            new CommerceColumn("GeneratedAt", TimestampNotNull),
            new CommerceColumn("BusinessKey", TextNullable))),

        new("CommerceBusinessMetricSnapshots", Columns(
            workspaceScoped: true,
            new CommerceColumn("MetricKey", TextNotNull),
            new CommerceColumn("CapturedAt", TimestampNotNull),
            new CommerceColumn("NumericValue", DecimalQuantity),
            new CommerceColumn("MoneyValue", MoneyNullable),
            new CommerceColumn("BusinessKey", TextNullable))),
    };
}

/// <summary>A single column in a Commerce table: its name and its SQLite column definition.</summary>
internal sealed record CommerceColumn(string Name, string Definition);

/// <summary>A single Commerce table: its name and its ordered column list (first column is the PK).</summary>
internal sealed record CommerceTableDefinition(string TableName, IReadOnlyList<CommerceColumn> Columns);
