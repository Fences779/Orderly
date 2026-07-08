namespace Orderly.Server.Models;

public sealed class ServerOptions
{
    public const string SectionName = "Orderly";

    public string PublicUrl { get; set; } = "https://localhost:5001";
    public string PostgresHost { get; set; } = "localhost";
    public int PostgresPort { get; set; } = 5432;
    public string PostgresDatabase { get; set; } = "orderly";
    public string PostgresUser { get; set; } = "orderly";
    public string PostgresPassword { get; set; } = string.Empty;
    public string JwtSigningKey { get; set; } = string.Empty;
    public string JwtIssuer { get; set; } = "Orderly.Server";
    public string JwtAudience { get; set; } = "Orderly.Client";
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
    public int RefreshTokenLifetimeDays { get; set; } = 30;
    public int BackupRetentionDays { get; set; } = 30;
    public string LocalBackupDirectory { get; set; } = "/opt/orderly/backups";
    public bool RestoreDrillEnabled { get; set; } = true;
    public int RestoreDrillIntervalHours { get; set; } = 24;
    public string LocalExportDirectory { get; set; } = "/opt/orderly/exports";
    public int ExportRetentionHours { get; set; } = 24;
    public int ExportMaxRetryCount { get; set; } = 2;
    public long ExportMaxLocalBytes { get; set; } = 2L * 1024 * 1024 * 1024;
    public long AttachmentQuotaBytes { get; set; } = 1024L * 1024 * 1024;
    public int ArchiveRetentionDays { get; set; } = 30;
    public bool RequirePreMigrationBackup { get; set; } = true;
    public bool RequirePreImportBackup { get; set; } = true;
    public string? BootstrapAdminToken { get; set; }
    public string? BootstrapAdminPassword { get; set; }
    public string AllowedOrigins { get; set; } = "*";

    // Object storage (Aliyun OSS) settings for backups and exports.
    public string OssEndpoint { get; set; } = string.Empty;
    public string OssBucketName { get; set; } = string.Empty;
    public string OssAccessKeyId { get; set; } = string.Empty;
    public string OssAccessKeySecret { get; set; } = string.Empty;
    public string OssBackupPrefix { get; set; } = "backups/";
    public string OssExportPrefix { get; set; } = "exports/";
    public bool OssEnabled => !string.IsNullOrWhiteSpace(OssEndpoint)
        && !string.IsNullOrWhiteSpace(OssBucketName)
        && !string.IsNullOrWhiteSpace(OssAccessKeyId)
        && !string.IsNullOrWhiteSpace(OssAccessKeySecret);

    public string GetConnectionString()
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = PostgresHost,
            Port = PostgresPort,
            Database = PostgresDatabase,
            Username = PostgresUser,
            Password = PostgresPassword,
            SslMode = Npgsql.SslMode.Prefer,
            Pooling = true,
            MaxPoolSize = 50
        };
        return builder.ConnectionString;
    }
}
