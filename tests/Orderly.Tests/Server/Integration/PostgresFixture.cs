using System.Diagnostics;
using Testcontainers.PostgreSql;
using Xunit;

namespace Orderly.Tests.Server.Integration;

/// <summary>
/// 为 Orderly.Server 集成测试提供 PostgreSQL 容器的 Collection Fixture。
/// 如果本地 Docker 守护进程不可达，所有依赖该 fixture 的测试将通过 SkippableFact 跳过。
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? ConnectionString => _container?.GetConnectionString();

    public PostgresFixture()
    {
        IsAvailable = TryCheckDockerQuickly();
        if (IsAvailable)
        {
            _container = new PostgreSqlBuilder()
                .WithDatabase("orderly_test")
                .WithUsername("orderly")
                .WithPassword("orderly_test_pw")
                .Build();
        }
    }

    public async Task InitializeAsync()
    {
        if (_container is null) return;
        try
        {
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PostgreSQL Testcontainer failed to start: {ex.Message}");
            IsAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// 快速探测 Docker daemon 是否可达，避免在没装 Docker 的环境等待启动超时。
    /// </summary>
    private static bool TryCheckDockerQuickly()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info --format '{{.ServerVersion}}'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process is null) return false;
            process.WaitForExit(3000);
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(process.StandardOutput.ReadToEnd());
        }
        catch
        {
            return false;
        }
    }
}

[CollectionDefinition("PostgresIntegration")]
public sealed class PostgresIntegrationCollection : ICollectionFixture<PostgresFixture>
{
}
