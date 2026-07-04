using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Dapper;
using Orderly.Server.Data;
using Orderly.Server.Hubs;
using Orderly.Server.Models;
using Orderly.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var serverOptions = new ServerOptions();
builder.Configuration.GetSection(ServerOptions.SectionName).Bind(serverOptions);
builder.Configuration.Bind(serverOptions);

// Override from environment variables for Docker/ops friendliness.
serverOptions.PublicUrl = GetEnvOrConfig("ORDERLY_PUBLIC_URL", serverOptions.PublicUrl);
serverOptions.PostgresHost = GetEnvOrConfig("ORDERLY_POSTGRES_HOST", serverOptions.PostgresHost);
if (int.TryParse(Environment.GetEnvironmentVariable("ORDERLY_POSTGRES_PORT"), out var pgPort)) serverOptions.PostgresPort = pgPort;
serverOptions.PostgresDatabase = GetEnvOrConfig("ORDERLY_POSTGRES_DB", serverOptions.PostgresDatabase);
serverOptions.PostgresUser = GetEnvOrConfig("ORDERLY_POSTGRES_USER", serverOptions.PostgresUser);
serverOptions.PostgresPassword = GetEnvOrConfig("ORDERLY_POSTGRES_PASSWORD", serverOptions.PostgresPassword);
serverOptions.JwtSigningKey = GetEnvOrConfig("ORDERLY_JWT_SIGNING_KEY", serverOptions.JwtSigningKey);
serverOptions.BootstrapAdminToken = GetEnvOrConfig("ORDERLY_BOOTSTRAP_ADMIN_TOKEN", serverOptions.BootstrapAdminToken);
serverOptions.AllowedOrigins = GetEnvOrConfig("ORDERLY_ALLOWED_ORIGINS", serverOptions.AllowedOrigins);
if (int.TryParse(Environment.GetEnvironmentVariable("ORDERLY_BACKUP_RETENTION_DAYS"), out var retention)) serverOptions.BackupRetentionDays = retention;
serverOptions.LocalBackupDirectory = GetEnvOrConfig("ORDERLY_LOCAL_BACKUP_DIR", serverOptions.LocalBackupDirectory);
serverOptions.OssEndpoint = GetEnvOrConfig("ORDERLY_OSS_ENDPOINT", serverOptions.OssEndpoint);
serverOptions.OssBucketName = GetEnvOrConfig("ORDERLY_OSS_BUCKET", serverOptions.OssBucketName);
serverOptions.OssAccessKeyId = GetEnvOrConfig("ORDERLY_OSS_ACCESS_KEY_ID", serverOptions.OssAccessKeyId);
serverOptions.OssAccessKeySecret = GetEnvOrConfig("ORDERLY_OSS_ACCESS_KEY_SECRET", serverOptions.OssAccessKeySecret);
serverOptions.OssBackupPrefix = GetEnvOrConfig("ORDERLY_OSS_BACKUP_PREFIX", serverOptions.OssBackupPrefix);
serverOptions.OssExportPrefix = GetEnvOrConfig("ORDERLY_OSS_EXPORT_PREFIX", serverOptions.OssExportPrefix);

builder.Services.AddSingleton(serverOptions);

// Data & services
builder.Services.AddSingleton<PostgresConnectionFactory>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<ICloudAuthService, CloudAuthService>();
builder.Services.AddScoped<ICloudPermissionService, CloudPermissionService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IWorkspaceSyncService, WorkspaceSyncService>();
builder.Services.AddScoped<IWorkspaceSyncQueryService, WorkspaceSyncQueryService>();
builder.Services.AddScoped<ICloudImportService, CloudImportService>();
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
builder.Services.AddScoped<CommerceCommandService>();
builder.Services.AddScoped<IEmergencyDraftRepository, EmergencyDraftRepository>();
builder.Services.AddScoped<IEmergencyDraftProcessor, EmergencyDraftProcessor>();
builder.Services.AddHostedService<EmergencyDraftBackgroundService>();
builder.Services.AddSingleton<IBlobStorage, AliyunOssBlobStorage>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddHostedService<ExportBackgroundService>();
builder.Services.AddScoped<IDatabaseBackupService, DatabaseBackupService>();
builder.Services.AddHostedService<BackupBackgroundService>();
builder.Services.AddSingleton<ISignalRNotifier, SignalRNotifier>();

// Auth
var keyBytes = Encoding.UTF8.GetBytes(serverOptions.JwtSigningKey);
if (keyBytes.Length < 32)
{
    // Fallback for local development only; production must provide a real key.
    var devKey = "ORDERLY_DEV_ONLY_JWT_SIGNING_KEY_MUST_BE_32B";
    keyBytes = Encoding.UTF8.GetBytes(devKey);
}
var signingKey = new SymmetricSecurityKey(keyBytes);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = serverOptions.JwtIssuer,
            ValidAudience = serverOptions.JwtAudience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdClaim = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (!Guid.TryParse(userIdClaim, out var userId)) return;
                var tokenVersionClaim = context.Principal?.FindFirst("token_version")?.Value;
                if (!int.TryParse(tokenVersionClaim, out var tokenVersion)) return;

                var authService = context.HttpContext.RequestServices.GetRequiredService<ICloudAuthService>();
                var membership = await authService.GetMembershipAsync(userId);
                var user = await authService.GetUserAsync(userId);
                if (user == null || !user.IsEnabled || !await authService.ValidateTokenVersionAsync(userId, tokenVersion) || membership == null || !membership.IsEnabled)
                {
                    context.Fail("User or membership is no longer valid.");
                    return;
                }

                var identity = context.Principal!.Identity as ClaimsIdentity;
                identity!.AddClaim(new Claim("workspace_id", membership.WorkspaceId.ToString("N")));
                identity.AddClaim(new Claim(ClaimTypes.Role, membership.CloudRole));
                identity.AddClaim(new Claim("business_label", membership.BusinessLabel));
                identity.AddClaim(new Claim("display_name", user.DisplayName));
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

builder.Services.AddCors(options =>
{
    var origins = serverOptions.AllowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries);
    options.AddPolicy("OrderlyCors", policy =>
    {
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Database migrations
var migrationRunner = new MigrationRunner(serverOptions);
var migrationResult = migrationRunner.Run();
if (!migrationResult.Successful)
{
    app.Logger.LogError(migrationResult.Error, "Database migration failed.");
    // In production we do not start as writable. Throw to prevent silent running.
    throw new InvalidOperationException("Database migration failed. The server cannot start in a writable state.", migrationResult.Error);
}

// Bootstrap admin
var authService = app.Services.GetRequiredService<ICloudAuthService>();
var bootstrapToken = Environment.GetEnvironmentVariable("ORDERLY_BOOTSTRAP_ADMIN_TOKEN");
if (!string.IsNullOrEmpty(bootstrapToken))
{
    await authService.EnsureBootstrapAdminAsync(bootstrapToken);
}

app.UseCors("OrderlyCors");
app.UseAuthentication();
app.UseAuthorization();
app.UseCurrentUserContext();
app.UseConflictExceptionHandling();

app.MapControllers();
app.MapHub<WorkspaceHub>("/hubs/workspace");

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Time = DateTime.UtcNow }));
app.MapGet("/health/db", async (PostgresConnectionFactory factory) =>
{
    try
    {
        await using var connection = (System.Data.Common.DbConnection)await factory.OpenConnectionAsync();
        var result = await ((System.Data.IDbConnection)connection).ExecuteScalarAsync<int>("SELECT 1;");
        return Results.Ok(new { Status = "Healthy", Db = "PostgreSQL", Time = DateTime.UtcNow });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Database unhealthy: {ex.Message}", statusCode: 503);
    }
});
app.MapGet("/health/version", () => Results.Ok(new { Version = "0.2.0-cloud-preview", Build = "Orderly.Server" }));

app.Run();

static string GetEnvOrConfig(string envName, string fallback) =>
    Environment.GetEnvironmentVariable(envName) ?? fallback;
