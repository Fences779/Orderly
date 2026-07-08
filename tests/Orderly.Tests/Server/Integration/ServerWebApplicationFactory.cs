using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orderly.Server.Models;

namespace Orderly.Tests.Server.Integration;

/// <summary>
/// WebApplicationFactory for Orderly.Server integration tests. Replaces the PostgreSQL
/// connection string with the Testcontainer instance and disables hosted background
/// services that are not needed for API-level tests.
/// </summary>
internal sealed class ServerWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly PostgresFixture _fixture;
    private readonly Action<IServiceCollection>? _configureServices;

    public ServerWebApplicationFactory(PostgresFixture fixture, Action<IServiceCollection>? configureServices = null)
    {
        _fixture = fixture;
        _configureServices = configureServices;
        ApplyEnvironmentOverrides();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = _fixture.ConnectionString;
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("PostgreSQL Testcontainer is not available.");
        }

        var pgBuilder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ServerOptions.SectionName}:{nameof(ServerOptions.PostgresHost)}"] = pgBuilder.Host,
                [$"{ServerOptions.SectionName}:{nameof(ServerOptions.PostgresPort)}"] = pgBuilder.Port.ToString(),
                [$"{ServerOptions.SectionName}:{nameof(ServerOptions.PostgresDatabase)}"] = pgBuilder.Database,
                [$"{ServerOptions.SectionName}:{nameof(ServerOptions.PostgresUser)}"] = pgBuilder.Username,
                [$"{ServerOptions.SectionName}:{nameof(ServerOptions.PostgresPassword)}"] = pgBuilder.Password,
                [$"{ServerOptions.SectionName}:{nameof(ServerOptions.JwtSigningKey)}"] = "ORDERLY_TEST_JWT_SIGNING_KEY_32BYTES",
                [$"{ServerOptions.SectionName}:{nameof(ServerOptions.BootstrapAdminToken)}"] = string.Empty,
                [$"{ServerOptions.SectionName}:{nameof(ServerOptions.RequirePreMigrationBackup)}"] = "false",
                [$"{ServerOptions.SectionName}:{nameof(ServerOptions.RequirePreImportBackup)}"] = "false",
                [$"{ServerOptions.SectionName}:{nameof(ServerOptions.RestoreDrillEnabled)}"] = "false",
                [$"{ServerOptions.SectionName}:{nameof(ServerOptions.OssEndpoint)}"] = string.Empty,
            });
        });

        // Disable background services to avoid interference in short-lived tests.
        builder.ConfigureServices(services =>
        {
            var hosted = services.Where(sd => sd.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
            foreach (var descriptor in hosted)
            {
                services.Remove(descriptor);
            }

            _configureServices?.Invoke(services);
        });
    }

    private void ApplyEnvironmentOverrides()
    {
        var connectionString = _fixture.ConnectionString;
        if (string.IsNullOrEmpty(connectionString))
        {
            return;
        }

        var pgBuilder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        Environment.SetEnvironmentVariable("ORDERLY_POSTGRES_HOST", pgBuilder.Host);
        Environment.SetEnvironmentVariable("ORDERLY_POSTGRES_PORT", pgBuilder.Port.ToString());
        Environment.SetEnvironmentVariable("ORDERLY_POSTGRES_DB", pgBuilder.Database);
        Environment.SetEnvironmentVariable("ORDERLY_POSTGRES_USER", pgBuilder.Username);
        Environment.SetEnvironmentVariable("ORDERLY_POSTGRES_PASSWORD", pgBuilder.Password);
        Environment.SetEnvironmentVariable("ORDERLY_JWT_SIGNING_KEY", "ORDERLY_TEST_JWT_SIGNING_KEY_32BYTES");
        Environment.SetEnvironmentVariable("ORDERLY_BOOTSTRAP_ADMIN_TOKEN", string.Empty);
        Environment.SetEnvironmentVariable("ORDERLY_REQUIRE_PRE_MIGRATION_BACKUP", "false");
        Environment.SetEnvironmentVariable("ORDERLY_REQUIRE_PRE_IMPORT_BACKUP", "false");
        Environment.SetEnvironmentVariable("ORDERLY_RESTORE_DRILL_ENABLED", "false");
        Environment.SetEnvironmentVariable("ORDERLY_OSS_ENDPOINT", string.Empty);
    }
}
