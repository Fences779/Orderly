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
    private const long MaxBackupFileBytes = 256L * 1024L * 1024L;

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

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
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
}
