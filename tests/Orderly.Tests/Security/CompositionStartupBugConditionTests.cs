using CsCheck;
using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Data.Repositories;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// Property 3 (Bug Condition) — 加密器启动期 fail-closed 断言。
///
/// 本测试编码了"修复后"的期望行为（design.md Property 3 / Requirements 2.3）：
///   当生产路径被（错误）注入空操作加密器 <see cref="PassthroughFieldEncryptionService"/> 时，
///   系统 SHALL 在启动期断言失败并 fail-closed（拒绝启动或拒绝写入），
///   绝不允许敏感字段以明文落盘。
///
/// 对应 design.md Bug Condition：
///   isBugCondition CASE "CompositionStartup"
///     = isProductionStartup(input) AND injectedEncryptorIsPassthrough(input)
///
/// **CRITICAL**: 在未修复代码上本测试预期 FAIL（失败即确认缺陷存在：照常启动、敏感字段明文落盘）。
/// 根因：组合根 <c>EnsureAuthServicesPrepared</c> / <c>InitializeWorkspaceAsync</c> 接受任意
/// <see cref="Orderly.Core.Services.IFieldEncryptionService"/> 实现而不校验其类型/能力，
/// <see cref="PassthroughFieldEncryptionService"/> 可被静默注入，缺少 fail-closed 的启动期不变量断言。
///
/// **Seam 约束（已记录）**: 组合根位于 WPF 项目 <c>Orderly.App</c>（<c>net8.0-windows</c>），
/// 而本测试项目目标框架为 <c>net8.0</c> 且仅引用 <c>Orderly.Core</c> / <c>Orderly.Data</c>，
/// 无法直接引用并调用 <c>App.Composition</c> / <c>App.WorkspaceComposition</c>。
/// 因此本测试在"最小可测后端缝隙"上复现缺陷：直接在真实生产写入路径
/// （<see cref="CustomerRepository"/>，即 <c>InitializeWorkspaceAsync</c> 实际装配的仓储）
/// 注入 <see cref="PassthroughFieldEncryptionService"/>.Instance，断言敏感字段不得以明文写入
/// <c>*Ciphertext</c> 列。修复（任务 7.3）应在生产组合/写入路径引入 fail-closed 断言
/// （或可被后端引用的等价守卫），使本测试在修复后转为通过。
///
/// **Validates: Requirements 2.3**
/// </summary>
public sealed class CompositionStartupBugConditionTests
{
    // 敏感明文生成器：约束为合法客户名称输入（非空、可打印、<=40 字符、无控制字符），
    // 避免触发 CustomerRepository.NormalizeCustomer 的无关校验异常。
    private static readonly Gen<string> SensitiveNameGen =
        Gen.Char['a', 'z'].Array[6, 40]
            .Select(chars => "secret-" + new string(chars));

    /// <summary>
    /// Property 3 — 生产写入路径注入空操作加密器时，敏感字段绝不明文落盘。
    ///
    /// 对任意敏感明文，经真实 <see cref="CustomerRepository"/>（注入
    /// <see cref="PassthroughFieldEncryptionService"/>）写入后，原始 <c>NameCiphertext</c>
    /// 列不得包含该明文。修复前：空操作加密器原样回写明文 → 断言失败（确认缺陷）。
    /// </summary>
    [Fact]
    public void Production_write_path_with_passthrough_encryptor_must_not_persist_plaintext()
    {
        var (factory, databasePath) = CreateCustomersDatabase();
        try
        {
            // 与 App.WorkspaceComposition.InitializeWorkspaceAsync 完全一致的装配方式，
            // 但注入的是空操作加密器（即 injectedEncryptorIsPassthrough 的误配置）。
            var repository = new CustomerRepository(factory, PassthroughFieldEncryptionService.Instance);

            SensitiveNameGen.Sample(secret =>
            {
                var created = repository.CreateAsync(new Customer
                {
                    Name = secret,
                    Status = CustomerStatus.Active,
                    Priority = CustomerPriority.Normal,
                }).GetAwaiter().GetResult();

                var storedCiphertext = ReadRawNameCiphertext(factory, created.Id);

                Assert.False(
                    storedCiphertext.Contains(secret, StringComparison.Ordinal),
                    "生产写入路径注入空操作加密器后，敏感字段以明文落盘："
                    + $"NameCiphertext 列存储了明文 \"{secret}\"（实际写入值：\"{storedCiphertext}\"）。"
                    + "期望（修复后）：生产组合/写入路径应在启动期断言失败并 fail-closed"
                    + "（拒绝启动或拒绝写入），敏感字段绝不以明文落盘。");
            });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDeleteDatabase(databasePath);
        }
    }

    /// <summary>
    /// 具体反例（确定性单元用例）：固定敏感明文经空操作加密器写入后明文落盘。
    /// 提供清晰、可复现的反例以理解根因（缺少 fail-closed 的启动期加密器断言）。
    /// </summary>
    [Fact]
    public void Passthrough_encryptor_persists_known_sensitive_value_as_plaintext()
    {
        const string secret = "TopSecret-CustomerName-13800138000";
        var (factory, databasePath) = CreateCustomersDatabase();
        try
        {
            var repository = new CustomerRepository(factory, PassthroughFieldEncryptionService.Instance);

            var created = repository.CreateAsync(new Customer
            {
                Name = secret,
                Status = CustomerStatus.Active,
                Priority = CustomerPriority.Normal,
            }).GetAwaiter().GetResult();

            var storedCiphertext = ReadRawNameCiphertext(factory, created.Id);

            Assert.False(
                storedCiphertext.Contains(secret, StringComparison.Ordinal),
                "空操作加密器使已知敏感明文落盘："
                + $"NameCiphertext = \"{storedCiphertext}\"。"
                + "期望（修复后）：生产路径 fail-closed，明文绝不落盘。");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDeleteDatabase(databasePath);
        }
    }

    private static (SqliteConnectionFactory Factory, string DatabasePath) CreateCustomersDatabase()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"orderly-prop3-{Guid.NewGuid():N}.db");

        var factory = new SqliteConnectionFactory(databasePath);

        // 直接创建生产 CustomerRepository 读写所需的 Customers 表结构
        // （与 DatabaseInitializer 的列定义一致），绕过文件加固/种子等无关机制，
        // 使本测试只针对"加密器误配置导致明文落盘"这一缺陷。
        using var connection = factory.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Customers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                NameCiphertext TEXT NOT NULL DEFAULT '',
                Status INTEGER NOT NULL DEFAULT 0,
                Priority INTEGER NOT NULL DEFAULT 1,
                SourcePlatform TEXT NOT NULL DEFAULT '',
                Channel TEXT NOT NULL DEFAULT '',
                ContactHandle TEXT NOT NULL DEFAULT '',
                ContactHandleCiphertext TEXT NOT NULL DEFAULT '',
                Phone TEXT NOT NULL DEFAULT '',
                PhoneCiphertext TEXT NOT NULL DEFAULT '',
                Remark TEXT NOT NULL DEFAULT '',
                RemarkCiphertext TEXT NOT NULL DEFAULT '',
                ExternalId TEXT NOT NULL DEFAULT '',
                ExternalIdCiphertext TEXT NOT NULL DEFAULT '',
                RawPayload TEXT NOT NULL DEFAULT '',
                RawPayloadCiphertext TEXT NOT NULL DEFAULT '',
                LastContactAt TEXT NULL,
                LastContactAtCiphertext TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1
            );
            """;
        command.ExecuteNonQuery();

        return (factory, databasePath);
    }

    private static string ReadRawNameCiphertext(SqliteConnectionFactory factory, int customerId)
    {
        using var connection = factory.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT NameCiphertext FROM Customers WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", customerId);
        var value = command.ExecuteScalar();
        return value as string ?? string.Empty;
    }

    private static void TryDeleteDatabase(string databasePath)
    {
        try
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
