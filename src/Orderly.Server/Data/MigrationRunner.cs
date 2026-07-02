using System.Reflection;
using DbUp;
using DbUp.Engine;
using Orderly.Server.Models;

namespace Orderly.Server.Data;

public sealed class MigrationRunner
{
    private readonly ServerOptions _options;

    public MigrationRunner(ServerOptions options)
    {
        _options = options;
    }

    public DatabaseUpgradeResult Run()
    {
        var connectionString = _options.GetConnectionString();
        var engine = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(), script => script.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .LogToConsole()
            .WithTransactionPerScript()
            .Build();

        return engine.PerformUpgrade();
    }

    public bool EnsureSchema()
    {
        var result = Run();
        return result.Successful;
    }
}
