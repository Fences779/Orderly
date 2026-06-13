using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Migration;
using Orderly.Core.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Sqlite;
using TaskStatus = Orderly.Core.Commerce.TaskStatus;

namespace Orderly.Data.Services;

/// <summary>
/// Non-destructive, backup-first, idempotent migration from a legacy generic CRM database into the
/// Universal_Domain_Model (Commerce) schema (Requirements 3.4–3.10). The service is deliberately
/// neutral in naming and contains no Forbidden_Term (C-4): it reads only the generic CRM tables
/// (Customers / Deals / Orders / FollowUps / CustomerNotes) and never touches the legacy
/// customer-specific / industry-specific remote data model (Req 3.5).
///
/// <para><b>Backup-first (Req 3.7, 3.8).</b> Before applying any change the service asks
/// <see cref="ICommerceSourceBackup"/> to create a complete backup of the source database. If the
/// backup cannot be created the migration is aborted immediately with the
/// <c>BackupFailedMigrationAborted</c> token, the source is left unmodified, and the reason is
/// recorded. The migration is non-destructive in any case: it only ever reads the source and writes
/// to the target Commerce tables.</para>
///
/// <para><b>Idempotence (Req 3.6).</b> Every migrated target record is keyed by a deterministic
/// identity derived from the owning workspace, the logical source kind, and the legacy row id (its
/// Business_Key). On a repeat run the service finds the existing target record by that identity and
/// updates it in place instead of inserting a duplicate, and it pins each target record's audit
/// timestamps to deterministic values derived from the source row, so running the migration two or
/// more times yields a target record set identical to running it once.</para>
///
/// <para><b>Mapping rules (Req 3.4).</b></para>
/// <list type="bullet">
///   <item><description>legacy <c>Customer</c> → Commerce <c>Customer</c>.</description></item>
///   <item><description>legacy <c>Order</c> → Commerce <c>Order</c>.</description></item>
///   <item><description>
///     legacy <c>Deal</c> → Commerce <c>Order</c>, <c>BusinessTask</c>, or a note, decided
///     deterministically by the deal's stage (see <see cref="MapDealStage"/>):
///     a <b>Won</b> deal becomes a realized <c>Order</c>; an <b>open/active</b> deal
///     (New / Qualified / Quoting / Negotiating) becomes an actionable <c>BusinessTask</c>;
///     a <b>Lost</b> or <b>Archived</b> deal becomes an informational <c>note</c>.
///   </description></item>
///   <item><description>legacy <c>FollowUp</c> → Commerce <c>BusinessTask</c>.</description></item>
///   <item><description>legacy <c>CustomerNote</c> → a note.</description></item>
///   <item><description>legacy <c>ActivityLog</c> → retained unchanged (never read or transformed).</description></item>
/// </list>
///
/// <para><b>Note representation.</b> The Universal_Domain_Model has no dedicated note entity, so a
/// "note" is represented as a <see cref="BusinessTask"/> whose text is carried in
/// <see cref="BusinessTask.Description"/>, whose <see cref="BusinessTask.Status"/> is
/// <see cref="TaskStatus.Completed"/> (a note is not actionable work), and whose
/// <see cref="CommerceEntity.CustomFieldsJson"/> carries a <c>{"recordKind":"note", …}</c> marker so
/// notes are distinguishable from genuine tasks while remaining queryable and recoverable.</para>
/// </summary>
public sealed partial class CommerceLegacyMigrationService
{
    /// <summary>Stable namespace for deriving deterministic target identities (Business_Key basis).</summary>
    private static readonly Guid IdentityNamespace = new("7b3d0a2e-2c4f-4b8a-9c1d-2e6f5a4b3c21");

    // Logical source-kind discriminators used both for the Business_Key and the CustomFieldsJson marker.
    private const string KindCustomer = "Customer";
    private const string KindOrder = "Order";
    private const string KindDeal = "Deal";
    private const string KindFollowUp = "FollowUp";
    private const string KindCustomerNote = "CustomerNote";

    // Logical target buckets reported in the result's per-target breakdown.
    private const string TargetCustomer = "Customer";
    private const string TargetOrder = "Order";
    private const string TargetBusinessTask = "BusinessTask";
    private const string TargetNote = "note";

    private readonly SqliteConnectionFactory _sourceConnectionFactory;
    private readonly SqliteConnectionFactory _targetConnectionFactory;
    private readonly Guid _workspaceId;
    private readonly ICommerceSourceBackup _backup;
    private readonly IFieldEncryptionService? _fieldEncryption;

    private readonly CommerceCustomerRepository _customers;
    private readonly CommerceOrderRepository _orders;
    private readonly BusinessTaskRepository _tasks;

    /// <summary>
    /// Creates the migration service.
    /// </summary>
    /// <param name="sourceConnectionFactory">Connection factory for the legacy source database (read-only use).</param>
    /// <param name="targetConnectionFactory">Connection factory for the target Commerce database (written to).</param>
    /// <param name="workspaceId">The workspace that owns the migrated business records.</param>
    /// <param name="backup">
    /// The pre-migration backup strategy. Defaults to a file-copy backup of the source database when null.
    /// </param>
    /// <param name="fieldEncryption">
    /// Optional field-encryption service used to read the legacy sensitive columns. When the source
    /// database has already been migrated to the P0 field-encryption format (sensitive plaintext
    /// columns cleared, data held in the <c>*Ciphertext</c> columns), the migration decrypts each
    /// sensitive value with this service so the migrated Commerce records carry the real values rather
    /// than blanks. When null (or when a ciphertext column is empty / absent) the migration falls back
    /// to the legacy plaintext column, so pre-encryption databases continue to migrate unchanged.
    /// </param>
    public CommerceLegacyMigrationService(
        SqliteConnectionFactory sourceConnectionFactory,
        SqliteConnectionFactory targetConnectionFactory,
        Guid workspaceId,
        ICommerceSourceBackup? backup = null,
        IFieldEncryptionService? fieldEncryption = null)
    {
        _sourceConnectionFactory = sourceConnectionFactory ?? throw new ArgumentNullException(nameof(sourceConnectionFactory));
        _targetConnectionFactory = targetConnectionFactory ?? throw new ArgumentNullException(nameof(targetConnectionFactory));
        _workspaceId = workspaceId;
        _backup = backup ?? new CommerceSourceFileBackup();
        _fieldEncryption = fieldEncryption;

        _customers = new CommerceCustomerRepository(_targetConnectionFactory);
        _orders = new CommerceOrderRepository(_targetConnectionFactory);
        _tasks = new BusinessTaskRepository(_targetConnectionFactory);
    }

    /// <summary>
    /// Runs the migration. Creates the source backup first (aborting with
    /// <c>BackupFailedMigrationAborted</c> if it fails), then migrates every mapped legacy record
    /// idempotently, and finally writes a log entry recording the outcome and migrated record count
    /// (Req 3.9). The source database is never modified.
    /// </summary>
    public async Task<CommerceLegacyMigrationResult> MigrateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string sourcePath = _sourceConnectionFactory.DatabasePath;

        // No legacy source => nothing to migrate; no change applied.
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            var missing = new CommerceLegacyMigrationResult
            {
                Outcome = CommerceLegacyMigrationOutcome.SourceDatabaseMissing,
                OutcomeToken = CommerceLegacyMigrationOutcomeTokens.SourceDatabaseMissing,
                MigratedRecordCount = 0,
                Reason = "未发现 legacy 源数据库，无需迁移。", // "No legacy source database found; nothing to migrate."
            };
            await TryWriteLogAsync(missing, cancellationToken);
            return missing;
        }

        // Backup-first (Req 3.8): create a complete backup before any change. Abort on failure.
        CommerceSourceBackupResult backupResult = await _backup.CreateBackupAsync(sourcePath, cancellationToken);
        if (!backupResult.Succeeded)
        {
            var aborted = new CommerceLegacyMigrationResult
            {
                Outcome = CommerceLegacyMigrationOutcome.BackupFailedMigrationAborted,
                OutcomeToken = CommerceLegacyMigrationOutcomeTokens.BackupFailedMigrationAborted,
                MigratedRecordCount = 0,
                Reason = backupResult.Reason,
            };
            await TryWriteLogAsync(aborted, cancellationToken);
            return aborted;
        }

        // Read the legacy source (read-only) into memory, then close the source connection before writing.
        LegacySnapshot snapshot = await ReadSourceAsync(cancellationToken);

        // Ensure the target Commerce schema exists (idempotent, Req 3.3) before writing.
        await EnsureTargetSchemaAsync(cancellationToken);

        var counts = new Dictionary<string, int>
        {
            [TargetCustomer] = 0,
            [TargetOrder] = 0,
            [TargetBusinessTask] = 0,
            [TargetNote] = 0,
        };

        await MigrateCustomersAsync(snapshot, counts, cancellationToken);
        await MigrateOrdersAsync(snapshot, counts, cancellationToken);
        await MigrateDealsAsync(snapshot, counts, cancellationToken);
        await MigrateFollowUpsAsync(snapshot, counts, cancellationToken);
        await MigrateCustomerNotesAsync(snapshot, counts, cancellationToken);

        int total = counts.Values.Sum();
        var result = new CommerceLegacyMigrationResult
        {
            Outcome = CommerceLegacyMigrationOutcome.Completed,
            OutcomeToken = CommerceLegacyMigrationOutcomeTokens.MigrationCompleted,
            MigratedRecordCount = total,
            BackupPath = backupResult.BackupPath,
            Reason = "迁移完成。", // "Migration completed."
            CountsByTarget = counts,
        };

        await TryWriteLogAsync(result, cancellationToken);
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // Mapping helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The documented deterministic Deal mapping rule (Req 3.4). Decides which Commerce target a
    /// legacy deal maps to purely from its stage so a re-run always produces the same target kind:
    /// <list type="bullet">
    ///   <item><description><c>Won</c> → <see cref="DealTarget.Order"/> (a realized sale).</description></item>
    ///   <item><description><c>New</c>, <c>Qualified</c>, <c>Quoting</c>, <c>Negotiating</c> → <see cref="DealTarget.BusinessTask"/> (active work to pursue).</description></item>
    ///   <item><description><c>Lost</c>, <c>Archived</c> → <see cref="DealTarget.Note"/> (informational record).</description></item>
    /// </list>
    /// </summary>
    internal static DealTarget MapDealStage(int legacyStage) => legacyStage switch
    {
        4 /* Won */ => DealTarget.Order,
        5 /* Lost */ => DealTarget.Note,
        6 /* Archived */ => DealTarget.Note,
        _ /* New / Qualified / Quoting / Negotiating */ => DealTarget.BusinessTask,
    };

    /// <summary>The Commerce target a legacy deal maps to.</summary>
    internal enum DealTarget
    {
        Order,
        BusinessTask,
        Note
    }

    private async Task MigrateCustomersAsync(LegacySnapshot snapshot, IDictionary<string, int> counts, CancellationToken cancellationToken)
    {
        foreach (LegacyCustomer row in snapshot.Customers)
        {
            DateTime createdAt = ParseUtc(row.CreatedAt, DateTime.UtcNow);
            var customer = new Customer
            {
                Id = DeterministicId(KindCustomer, row.Id),
                CreatedAt = createdAt,
                WorkspaceId = _workspaceId,
                Name = row.Name,
                Phone = NullIfEmpty(row.Phone),
                WeChat = NullIfEmpty(row.ContactHandle),
                CustomFieldsJson = BuildMarker(KindCustomer, row.Id, extra =>
                {
                    extra["recordKind"] = "customer";
                    if (!string.IsNullOrWhiteSpace(row.Remark))
                    {
                        extra["remark"] = row.Remark;
                    }
                }),
            };
            PinAudit(customer, row.UpdatedAt, createdAt);

            await UpsertAsync(_customers, customer, cancellationToken);
            counts[TargetCustomer]++;
        }
    }

    private async Task MigrateOrdersAsync(LegacySnapshot snapshot, IDictionary<string, int> counts, CancellationToken cancellationToken)
    {
        foreach (LegacyOrder row in snapshot.Orders)
        {
            await UpsertOrderAsync(
                kind: KindOrder,
                sourceId: row.Id,
                customerId: row.CustomerId,
                orderNo: $"LEGACY-O-{row.Id}",
                salesStage: MapOrderStatus(row.Status),
                amount: row.Amount,
                note: NullIfEmpty(row.Title),
                createdAtText: row.CreatedAt,
                updatedAtText: row.UpdatedAt,
                marker: extra =>
                {
                    extra["recordKind"] = "order";
                    if (!string.IsNullOrWhiteSpace(row.Requirement))
                    {
                        extra["requirement"] = row.Requirement;
                    }
                },
                cancellationToken: cancellationToken);
            counts[TargetOrder]++;
        }
    }

    private async Task MigrateDealsAsync(LegacySnapshot snapshot, IDictionary<string, int> counts, CancellationToken cancellationToken)
    {
        foreach (LegacyDeal row in snapshot.Deals)
        {
            switch (MapDealStage(row.Stage))
            {
                case DealTarget.Order:
                    await UpsertOrderAsync(
                        kind: KindDeal,
                        sourceId: row.Id,
                        customerId: row.CustomerId,
                        orderNo: $"LEGACY-D-{row.Id}",
                        salesStage: OrderSalesStage.Confirmed,
                        amount: row.EstimatedAmount,
                        note: NullIfEmpty(row.Title),
                        createdAtText: row.CreatedAt,
                        updatedAtText: row.UpdatedAt,
                        marker: extra =>
                        {
                            extra["recordKind"] = "order";
                            extra["legacyDealStage"] = "Won";
                        },
                        cancellationToken: cancellationToken);
                    counts[TargetOrder]++;
                    break;

                case DealTarget.BusinessTask:
                    await UpsertTaskAsync(
                        kind: KindDeal,
                        sourceId: row.Id,
                        title: string.IsNullOrWhiteSpace(row.Title) ? "成交机会" : row.Title,
                        description: NullIfEmpty(row.Requirement),
                        status: MapOpenDealStatus(row.Stage),
                        dueDate: ParseUtcNullable(row.ExpectedCloseAt),
                        completedAt: null,
                        customerId: row.CustomerId,
                        orderId: null,
                        createdAtText: row.CreatedAt,
                        updatedAtText: row.UpdatedAt,
                        marker: extra =>
                        {
                            extra["recordKind"] = "task";
                            extra["legacyDealStage"] = "Open";
                        },
                        cancellationToken: cancellationToken);
                    counts[TargetBusinessTask]++;
                    break;

                case DealTarget.Note:
                    await UpsertTaskAsync(
                        kind: KindDeal,
                        sourceId: row.Id,
                        title: string.IsNullOrWhiteSpace(row.Title) ? "成交机会备注" : row.Title,
                        description: NullIfEmpty(row.Requirement),
                        status: TaskStatus.Completed,
                        dueDate: null,
                        completedAt: ParseUtcNullable(row.ClosedAt),
                        customerId: row.CustomerId,
                        orderId: null,
                        createdAtText: row.CreatedAt,
                        updatedAtText: row.UpdatedAt,
                        marker: extra =>
                        {
                            extra["recordKind"] = "note";
                            extra["legacySource"] = KindDeal;
                            extra["legacyDealStage"] = row.Stage == 5 ? "Lost" : "Archived";
                        },
                        cancellationToken: cancellationToken);
                    counts[TargetNote]++;
                    break;
            }
        }
    }

    private async Task MigrateFollowUpsAsync(LegacySnapshot snapshot, IDictionary<string, int> counts, CancellationToken cancellationToken)
    {
        foreach (LegacyFollowUp row in snapshot.FollowUps)
        {
            await UpsertTaskAsync(
                kind: KindFollowUp,
                sourceId: row.Id,
                title: string.IsNullOrWhiteSpace(row.Title) ? "跟进" : row.Title,
                description: NullIfEmpty(row.Content),
                status: MapFollowUpStatus(row.Status),
                dueDate: ParseUtcNullable(row.ScheduledAt),
                completedAt: ParseUtcNullable(row.CompletedAt),
                customerId: row.CustomerId,
                orderId: row.OrderId,
                createdAtText: row.CreatedAt,
                updatedAtText: row.UpdatedAt,
                marker: extra => extra["recordKind"] = "task",
                cancellationToken: cancellationToken);
            counts[TargetBusinessTask]++;
        }
    }

    private async Task MigrateCustomerNotesAsync(LegacySnapshot snapshot, IDictionary<string, int> counts, CancellationToken cancellationToken)
    {
        foreach (LegacyCustomerNote row in snapshot.CustomerNotes)
        {
            string content = row.Content ?? string.Empty;
            string title = BuildNoteTitle(content);
            await UpsertTaskAsync(
                kind: KindCustomerNote,
                sourceId: row.Id,
                title: title,
                description: NullIfEmpty(content),
                status: TaskStatus.Completed,
                dueDate: null,
                completedAt: null,
                customerId: row.CustomerId,
                orderId: row.OrderId,
                createdAtText: row.CreatedAt,
                updatedAtText: row.UpdatedAt,
                marker: extra =>
                {
                    extra["recordKind"] = "note";
                    extra["legacySource"] = KindCustomerNote;
                    extra["legacyNoteType"] = row.Type;
                    extra["isPinned"] = row.IsPinned;
                },
                cancellationToken: cancellationToken);
            counts[TargetNote]++;
        }
    }

    private async Task UpsertOrderAsync(
        string kind,
        long sourceId,
        long customerId,
        string orderNo,
        OrderSalesStage salesStage,
        double amount,
        string? note,
        string? createdAtText,
        string? updatedAtText,
        Action<Dictionary<string, object>> marker,
        CancellationToken cancellationToken)
    {
        DateTime createdAt = ParseUtc(createdAtText, DateTime.UtcNow);
        CommerceMoney total = ToMoney(amount);
        var order = new Order
        {
            Id = DeterministicId(kind, sourceId),
            CreatedAt = createdAt,
            WorkspaceId = _workspaceId,
            OrderNo = orderNo,
            CustomerId = customerId > 0 ? DeterministicId(KindCustomer, customerId) : null,
            SalesStage = salesStage,
            PaymentStage = OrderPaymentStage.Unpaid,
            FulfillmentStage = OrderFulfillmentStage.NotStarted,
            Subtotal = total,
            Total = total,
            Cost = CommerceMoney.Zero,
            GrossProfit = total,
            GrossMargin = 0m,
            PaidAmount = CommerceMoney.Zero,
            ReceivableAmount = total,
            OrderedAt = createdAt,
            Note = note,
            CustomFieldsJson = BuildMarker(kind, sourceId, marker),
        };
        PinAudit(order, updatedAtText, createdAt);
        await UpsertAsync(_orders, order, cancellationToken);
    }

    private async Task UpsertTaskAsync(
        string kind,
        long sourceId,
        string title,
        string? description,
        TaskStatus status,
        DateTime? dueDate,
        DateTime? completedAt,
        long customerId,
        long? orderId,
        string? createdAtText,
        string? updatedAtText,
        Action<Dictionary<string, object>> marker,
        CancellationToken cancellationToken)
    {
        DateTime createdAt = ParseUtc(createdAtText, DateTime.UtcNow);

        // A deal-as-note and a deal-as-task share the legacy Deal id; disambiguate the target
        // identity by the resolved record kind so the Business_Key stays unique per target record.
        Guid? linkedOrderId = orderId is > 0 ? DeterministicId(KindOrder, orderId.Value) : null;

        var task = new BusinessTask
        {
            Id = DeterministicId(kind, sourceId),
            CreatedAt = createdAt,
            WorkspaceId = _workspaceId,
            Title = title,
            Description = description,
            Status = status,
            DueDate = dueDate,
            CompletedAt = completedAt,
            CustomerId = customerId > 0 ? DeterministicId(KindCustomer, customerId) : null,
            OrderId = linkedOrderId,
            CustomFieldsJson = BuildMarker(kind, sourceId, marker),
        };
        PinAudit(task, updatedAtText, createdAt);
        await UpsertAsync(_tasks, task, cancellationToken);
    }

    /// <summary>
    /// Idempotent upsert (Req 3.6): insert when the deterministically-keyed target record does not
    /// yet exist, otherwise update it in place so a repeat run produces no duplicate.
    /// </summary>
    private static async Task UpsertAsync<TEntity>(
        Orderly.Core.Commerce.Repositories.ICommerceRepository<TEntity> repository,
        TEntity entity,
        CancellationToken cancellationToken)
        where TEntity : CommerceEntity
    {
        TEntity? existing = await repository.GetByIdIncludingDeletedAsync(entity.Id, cancellationToken);
        if (existing is null)
        {
            await repository.CreateAsync(entity, cancellationToken);
        }
        else
        {
            await repository.UpdateAsync(entity, cancellationToken);
        }
    }

    private static OrderSalesStage MapOrderStatus(int legacyStatus) => legacyStatus switch
    {
        2 /* Quoted */ => OrderSalesStage.Quoted,
        3 /* PendingFollowUp */ => OrderSalesStage.Confirmed,
        4 /* Won */ => OrderSalesStage.Confirmed,
        5 /* Closed */ => OrderSalesStage.Completed,
        _ /* PendingCommunication / PendingQuote */ => OrderSalesStage.Draft,
    };

    private static TaskStatus MapOpenDealStatus(int legacyStage) => legacyStage switch
    {
        2 /* Quoting */ => TaskStatus.InProgress,
        3 /* Negotiating */ => TaskStatus.InProgress,
        _ /* New / Qualified */ => TaskStatus.Pending,
    };

    private static TaskStatus MapFollowUpStatus(int legacyStatus) => legacyStatus switch
    {
        1 /* InProgress */ => TaskStatus.InProgress,
        2 /* Completed */ => TaskStatus.Completed,
        3 /* Skipped */ => TaskStatus.Cancelled,
        4 /* Cancelled */ => TaskStatus.Cancelled,
        _ /* Pending / Overdue */ => TaskStatus.Pending,
    };

    private static string BuildNoteTitle(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "备注"; // "Note"
        }

        string firstLine = content.Replace("\r", " ").Replace("\n", " ").Trim();
        return firstLine.Length <= 40 ? firstLine : firstLine[..40];
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Pins the target record's audit timestamps to deterministic values so repeat runs are
    /// byte-identical: <c>CreatedAt</c> is the source creation time and <c>UpdatedAt</c> is the
    /// source update time (falling back to <c>CreatedAt</c>). Without this the
    /// <see cref="CommerceEntity"/> setters would stamp <c>UpdatedAt</c> with the (run-dependent)
    /// current time, breaking idempotence (Req 3.6).
    /// </summary>
    private static void PinAudit(CommerceEntity entity, string? updatedAtText, DateTime createdAt)
    {
        DateTime updatedAt = ParseUtc(updatedAtText, createdAt);
        if (updatedAt < createdAt)
        {
            updatedAt = createdAt;
        }

        entity.RestoreAuditState(updatedAt, deletedAt: null, EntityLifecycleStatus.Active);
    }

    /// <summary>
    /// Builds the <c>CustomFieldsJson</c> marker recording the logical record kind and the legacy
    /// Business_Key (source kind + id) for traceability, plus any caller-supplied extras.
    /// </summary>
    private static string BuildMarker(string sourceKind, long sourceId, Action<Dictionary<string, object>> configure)
    {
        var payload = new Dictionary<string, object>
        {
            ["legacyMigration"] = new Dictionary<string, object>
            {
                ["source"] = sourceKind,
                ["sourceId"] = sourceId,
            },
        };
        configure(payload);
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// Derives a deterministic <see cref="Guid"/> (the target record's Business_Key identity) from
    /// the owning workspace, the logical source kind, and the legacy row id, so that the same legacy
    /// record always maps to the same target identity across runs (Req 3.6).
    /// </summary>
    private Guid DeterministicId(string sourceKind, long sourceId)
    {
        string name = $"{_workspaceId:N}|{sourceKind}|{sourceId.ToString(CultureInfo.InvariantCulture)}";
        Span<byte> input = stackalloc byte[16 + Encoding.UTF8.GetByteCount(name)];
        IdentityNamespace.TryWriteBytes(input);
        Encoding.UTF8.GetBytes(name, input[16..]);

        Span<byte> hash = stackalloc byte[16];
        MD5.HashData(input, hash);
        return new Guid(hash);
    }

    private static CommerceMoney ToMoney(double value)
    {
        decimal amount;
        try
        {
            amount = (decimal)value;
        }
        catch (OverflowException)
        {
            amount = value > 0 ? CommerceMoney.MaxValue : CommerceMoney.MinValue;
        }

        if (amount < CommerceMoney.MinValue)
        {
            amount = CommerceMoney.MinValue;
        }
        else if (amount > CommerceMoney.MaxValue)
        {
            amount = CommerceMoney.MaxValue;
        }

        return CommerceMoney.From(amount);
    }

    private static DateTime ParseUtc(string? text, DateTime fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
        {
            return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        }

        return fallback;
    }

    private static DateTime? ParseUtcNullable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
        {
            return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        }

        return null;
    }
}
