using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;
using System.Text;
using System.Text.Json;

namespace Orderly.Data.Services;

public sealed partial class LocalBackupService : IBackupService
{
    private const int CurrentSchemaVersion = 1;
    private const string BackupEntityType = "local-backup";
    private const string RestoreEntityType = "local-restore";
    private const string RestoreOperator = "local-restore";
    private const string LauncherLocalAccountsTableName = "LocalAccountsSnapshot";
    private const string BackupIntegrityAlgorithm = "HMACSHA256";
    private const string BackupIntegritySessionKeyScope = "session-data-key";
    private const string BackupIntegrityMachineKeyScope = "machine-local-key";
    private const string BackupIntegrityKeyFileName = "backup-integrity.key";
    private const int BackupIntegrityKeyByteLength = 32;
    private const long MaxBackupFileBytes = 64L * 1024L * 1024L;
    private const int MaxBackupJsonDepth = 32;
    private const int MaxBackupTableRows = 50_000;
    private const int MaxBackupRowColumns = 128;
    private const int MaxBackupStringValueLength = 64 * 1024;

    private static readonly string[] IncludedTableNames =
    [
        "Customers",
        "Deals",
        "Orders",
        "FollowUps",
        "CustomerNotes",
        "ReplyTemplates",
        "PriceAdjustments",
        "ActivityLogs",
        "ConversationMessages",
        "AiSuggestions",
        "OcrResults"
    ];

    private static readonly string[] RestoreOrderedTableNames =
    [
        "Customers",
        "Deals",
        "Orders",
        "FollowUps",
        "CustomerNotes",
        "ReplyTemplates",
        "PriceAdjustments",
        "ActivityLogs",
        "ConversationMessages",
        "AiSuggestions",
        "OcrResults"
    ];

    private static readonly string[] TargetInspectionTableNames =
    [
        "Customers",
        "Deals",
        "Orders",
        "FollowUps",
        "CustomerNotes",
        "ReplyTemplates",
        "PriceAdjustments",
        "ActivityLogs",
        "ConversationMessages",
        "AiSuggestions",
        "OcrResults",
        "SyncRecords"
    ];

    private static readonly HashSet<string> KnownBackupSqlTableNames = new(
        IncludedTableNames
            .Concat(RestoreOrderedTableNames)
            .Concat(TargetInspectionTableNames),
        StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, HashSet<string>> RestoreTableColumns =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["Customers"] = BuildColumnSet(
                "Id", "Name", "Status", "Priority", "SourcePlatform", "Channel", "ContactHandle", "Phone", "Remark",
                "ExternalId", "RawPayload", "LastContactAt", "CreatedAt", "UpdatedAt", "DeletedAt", "RemoteId",
                "IsSynced", "Version", "NameCiphertext", "ContactHandleCiphertext", "PhoneCiphertext",
                "RemarkCiphertext", "ExternalIdCiphertext", "RawPayloadCiphertext", "LastContactAtCiphertext"),
            ["Deals"] = BuildColumnSet(
                "Id", "CustomerId", "Title", "Stage", "EstimatedAmount", "Requirement", "SourcePlatform", "Channel",
                "ExpectedCloseAt", "ClosedAt", "LostReason", "CreatedAt", "UpdatedAt", "DeletedAt", "RemoteId",
                "IsSynced", "Version", "TitleCiphertext", "EstimatedAmountCiphertext", "RequirementCiphertext",
                "ExpectedCloseAtCiphertext", "ClosedAtCiphertext", "LostReasonCiphertext"),
            ["Orders"] = BuildColumnSet(
                "Id", "CustomerId", "DealId", "Title", "Status", "Amount", "Requirement", "SourcePlatform", "Channel",
                "ExternalId", "RawPayload", "NextFollowUpAt", "CreatedAt", "UpdatedAt", "DeletedAt", "RemoteId",
                "IsSynced", "Version", "TitleCiphertext", "AmountCiphertext", "RequirementCiphertext",
                "ExternalIdCiphertext", "RawPayloadCiphertext", "NextFollowUpAtCiphertext"),
            ["FollowUps"] = BuildColumnSet(
                "Id", "CustomerId", "DealId", "OrderId", "Title", "Content", "Status", "ScheduledAt", "CompletedAt",
                "ReminderAt", "CreatedAt", "UpdatedAt", "DeletedAt", "RemoteId", "IsSynced", "Version",
                "TitleCiphertext", "ContentCiphertext", "ScheduledAtCiphertext", "CompletedAtCiphertext",
                "ReminderAtCiphertext"),
            ["CustomerNotes"] = BuildColumnSet(
                "Id", "CustomerId", "DealId", "OrderId", "Type", "Content", "IsPinned", "CreatedAt", "UpdatedAt",
                "DeletedAt", "RemoteId", "IsSynced", "Version", "ContentCiphertext"),
            ["ReplyTemplates"] = BuildColumnSet(
                "Id", "Title", "Scene", "Content", "IsFavorite", "SourcePlatform", "CreatedAt", "UpdatedAt",
                "ContentCiphertext"),
            ["PriceAdjustments"] = BuildColumnSet(
                "Id", "CustomerId", "DealId", "OrderId", "OriginalAmount", "AdjustedAmount", "Reason", "Status",
                "RequestedBy", "ApprovedBy", "ApprovedAt", "CreatedAt", "UpdatedAt", "DeletedAt", "RemoteId",
                "IsSynced", "Version", "OriginalAmountCiphertext", "AdjustedAmountCiphertext", "ReasonCiphertext",
                "RequestedByCiphertext", "ApprovedByCiphertext", "ApprovedAtCiphertext"),
            ["ActivityLogs"] = BuildColumnSet(
                "Id", "Type", "CustomerId", "DealId", "OrderId", "Title", "Description", "Operator", "MetadataJson",
                "CreatedAt", "UpdatedAt", "DeletedAt", "RemoteId", "IsSynced", "Version", "TitleCiphertext",
                "DescriptionCiphertext", "OperatorCiphertext", "MetadataJsonCiphertext"),
            ["ConversationMessages"] = BuildColumnSet(
                "Id", "CustomerId", "OrderId", "DealId", "Direction", "Channel", "SenderName", "Content",
                "MessageTime", "SourceMessageId", "MetadataJson", "CreatedAt", "UpdatedAt", "DeletedAt",
                "RemoteId", "IsSynced", "Version", "SenderNameCiphertext", "ContentCiphertext",
                "MessageTimeCiphertext", "SourceMessageIdCiphertext", "MetadataJsonCiphertext"),
            ["AiSuggestions"] = BuildColumnSet(
                "Id", "CustomerId", "OrderId", "MessageId", "SuggestionText", "Reason", "Confidence", "Status",
                "MetadataJson", "CreatedAt", "UpdatedAt", "DeletedAt", "RemoteId", "IsSynced", "Version",
                "SuggestionTextCiphertext", "ReasonCiphertext", "ConfidenceCiphertext", "MetadataJsonCiphertext"),
            ["OcrResults"] = BuildColumnSet(
                "Id", "CustomerId", "OrderId", "SourcePath", "SourceName", "ExtractedText", "Status", "ErrorMessage",
                "MetadataJson", "CreatedAt", "UpdatedAt", "DeletedAt", "RemoteId", "IsSynced", "Version",
                "SourcePathCiphertext", "SourceNameCiphertext", "ExtractedTextCiphertext", "ErrorMessageCiphertext",
                "MetadataJsonCiphertext")
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>> SensitivePlaintextColumnDefaults =
        new Dictionary<string, IReadOnlyDictionary<string, object>>(StringComparer.Ordinal)
        {
            ["Customers"] = BuildDefaultMap(
                ("Name", ""),
                ("ContactHandle", ""),
                ("Phone", ""),
                ("Remark", ""),
                ("ExternalId", ""),
                ("RawPayload", ""),
                ("LastContactAt", DBNull.Value)),
            ["Deals"] = BuildDefaultMap(
                ("Title", ""),
                ("EstimatedAmount", 0),
                ("Requirement", ""),
                ("ExpectedCloseAt", DBNull.Value),
                ("ClosedAt", DBNull.Value),
                ("LostReason", "")),
            ["Orders"] = BuildDefaultMap(
                ("Title", ""),
                ("Amount", 0),
                ("Requirement", ""),
                ("ExternalId", ""),
                ("RawPayload", ""),
                ("NextFollowUpAt", DBNull.Value)),
            ["FollowUps"] = BuildDefaultMap(
                ("Title", ""),
                ("Content", ""),
                ("ScheduledAt", ""),
                ("CompletedAt", DBNull.Value),
                ("ReminderAt", DBNull.Value)),
            ["CustomerNotes"] = BuildDefaultMap(
                ("Content", "")),
            ["ReplyTemplates"] = BuildDefaultMap(
                ("Content", "")),
            ["PriceAdjustments"] = BuildDefaultMap(
                ("OriginalAmount", 0),
                ("AdjustedAmount", 0),
                ("Reason", ""),
                ("RequestedBy", ""),
                ("ApprovedBy", ""),
                ("ApprovedAt", DBNull.Value)),
            ["ActivityLogs"] = BuildDefaultMap(
                ("Title", ""),
                ("Description", ""),
                ("Operator", ""),
                ("MetadataJson", "")),
            ["ConversationMessages"] = BuildDefaultMap(
                ("SenderName", ""),
                ("Content", ""),
                ("MessageTime", ""),
                ("SourceMessageId", ""),
                ("MetadataJson", "")),
            ["AiSuggestions"] = BuildDefaultMap(
                ("SuggestionText", ""),
                ("Reason", ""),
                ("Confidence", DBNull.Value),
                ("MetadataJson", "")),
            ["OcrResults"] = BuildDefaultMap(
                ("SourcePath", ""),
                ("SourceName", ""),
                ("ExtractedText", ""),
                ("ErrorMessage", ""),
                ("MetadataJson", ""))
        };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonDocumentOptions BackupJsonDocumentOptions = new()
    {
        MaxDepth = MaxBackupJsonDepth
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ISyncService _syncService;
    private readonly ISyncRecordRepository _syncRecordRepository;
    private readonly IActivityLogRepository _activityLogRepository;
    private readonly LauncherConnectionFactory? _launcherConnectionFactory;
    private readonly ISessionContextService? _sessionContextService;

    public LocalBackupService(
        SqliteConnectionFactory connectionFactory,
        ISyncService syncService,
        ISyncRecordRepository syncRecordRepository,
        IActivityLogRepository activityLogRepository,
        LauncherConnectionFactory? launcherConnectionFactory = null,
        ISessionContextService? sessionContextService = null)
    {
        _connectionFactory = connectionFactory;
        _syncService = syncService;
        _syncRecordRepository = syncRecordRepository;
        _activityLogRepository = activityLogRepository;
        _launcherConnectionFactory = launcherConnectionFactory;
        _sessionContextService = sessionContextService;
    }

    private static HashSet<string> BuildColumnSet(params string[] columns)
    {
        return new HashSet<string>(columns, StringComparer.Ordinal);
    }

    private static string RequireKnownBackupSqlTableName(string tableName)
    {
        return KnownBackupSqlTableNames.Contains(tableName)
            ? tableName
            : throw new InvalidOperationException($"表 {tableName} 不允许用于备份 SQL。");
    }

    private static IReadOnlyDictionary<string, object> BuildDefaultMap(params (string Column, object DefaultValue)[] columns)
    {
        return columns.ToDictionary(
            static column => column.Column,
            static column => column.DefaultValue,
            StringComparer.Ordinal);
    }
}
