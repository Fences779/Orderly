using System;
using System.IO;
using System.Linq;
using System.Text;
using CsCheck;
using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// Property-based test for credential non-leakage (design §11 Property 7 / §6.4 安全约束).
///
/// <para><b>Property 7: 凭证不泄露.</b>
/// 对任意明文密码 / PIN，安全审计写入（同步链 <see cref="SecurityAuditService.Record"/> 与持久化
/// <see cref="SecurityAuditService.RecordAsync"/>）后，下列产物均绝不包含明文凭证原文：</para>
/// <list type="bullet">
///   <item>同步链记录 <see cref="SecurityAuditService.GetRecords"/> 的 <c>SubjectIdentifier</c> /
///     <c>RecordHash</c> / <c>PreviousHash</c>（主体经单向哈希，绝不存明文，Req 14.3）。</item>
///   <item>持久化读回 <see cref="SecurityAuditService.QueryAsync"/> 的 <c>Kind</c> / <c>AccountLabel</c> /
///     <c>Detail</c>（仅脱敏元数据，Req 12.4 / 8.8）。</item>
///   <item>落盘字节（SQLCipher 全库加密，密文中亦不出现明文哨兵，Req 14.4）。</item>
/// </list>
///
/// <para>测试遵循产线调用约定：审计写入的 <c>detail</c> / <c>accountLabel</c> 一律传脱敏元数据，绝不传明文；
/// 同步链的 <c>subject</c> 即便直接喂入带哨兵的明文凭证，实现也以单向哈希存储——这正是「主体哈希化」
/// 的防泄露契约（Req 14.3）。每轮迭代使用独立的临时 SQLCipher 加密库，对任意生成的明文凭证断言三处产物
/// 均不含哨兵。</para>
///
/// **Validates: Requirements 14.3, 14.4, 8.8, 12.4**
/// </summary>
public sealed class CredentialNonLeakagePropertyTests
{
    // 明文凭证（新密码 / 新 PIN）原文的独特哨兵前缀：任何包含该前缀的字符串若出现在审计产物 /
    // 落盘字节中，都意味着明文真的泄露了。前缀足够独特，避免与脱敏文案或账号标签偶然子串碰撞。
    private const string SecretPrefix = "PLAINTEXT_CREDENTIAL_";

    // 凭证种类，用于挑选对应的脱敏文案；明文本身绝不写入审计。
    private enum CredentialKind
    {
        MasterPassword,
        Pin,
    }

    // 带哨兵的明文密码：字母数字 + 标点，长度 6..24，模拟真实密码空间。
    private static readonly Gen<string> PasswordSecretGen =
        from body in Gen.String[Gen.Char['!', '~'], 6, 24]
        select SecretPrefix + body;

    // 带哨兵的明文 PIN：恰好 6 位数字（Req 8 PIN 规则），仍以哨兵前缀标记以便检出泄露。
    private static readonly Gen<string> PinSecretGen =
        from digits in Gen.Char['0', '9'].Array[6]
        select SecretPrefix + new string(digits);

    // 非机密的账号标签（脱敏元数据），独立于明文凭证生成。
    private static readonly Gen<string> AccountLabelGen =
        Gen.OneOfConst("owner", "member-1", "member-2", "店长", "前台店员", "acct-7f3a");

    private static readonly Gen<(CredentialKind Kind, string Secret, string AccountLabel)> CaseGen =
        Gen.OneOf(
            from secret in PasswordSecretGen
            from label in AccountLabelGen
            select (CredentialKind.MasterPassword, secret, label),
            from secret in PinSecretGen
            from label in AccountLabelGen
            select (CredentialKind.Pin, secret, label));

    [Fact]
    public void Property7_plaintext_credentials_never_leak_into_audit_artifacts()
    {
        CaseGen.Sample(
            c =>
            {
                // 每轮使用独立的临时 SQLCipher 加密库，确保走真实持久化往返与落盘加密。
                var dir = Path.Combine(Path.GetTempPath(), "orderly-noleak-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                var dbPath = Path.Combine(dir, "audit.db");
                var key = Enumerable.Range(0, 32).Select(i => (byte)(i * 3 + 11)).ToArray();

                try
                {
                    SqliteConnectionFactory Factory() => new(dbPath, () => (byte[])key.Clone());
                    var service = new SecurityAuditService(() => Factory());

                    // 脱敏元数据：仅描述「哪种凭证被修改」，绝不嵌入明文凭证原文（Req 12.4 / 8.8）。
                    var detail = c.Kind == CredentialKind.MasterPassword
                        ? "主密码已修改（仅元数据，无明文）"
                        : "PIN 已修改（仅元数据，无明文）";

                    // (1) 同步链：subject 即便直接喂入明文凭证，也以单向哈希存储，绝不留明文（Req 14.3）。
                    service.Record(SecurityEventType.CredentialChange, c.Secret, SecurityEventOutcome.Success);

                    // (2) 持久化接缝：accountLabel / detail 一律传脱敏元数据，绝不传明文。
                    service.RecordAsync(SecurityAuditEventKind.CredentialChanged, c.AccountLabel, detail)
                        .GetAwaiter().GetResult();

                    // ---- 断言一：同步链记录不含明文哨兵，且主体确实被哈希（非原样保存）。 ----
                    var syncRecords = service.GetRecords();
                    Assert.Single(syncRecords);
                    var syncRecord = syncRecords[0];

                    Assert.DoesNotContain(SecretPrefix, syncRecord.SubjectIdentifier, StringComparison.Ordinal);
                    Assert.DoesNotContain(SecretPrefix, syncRecord.RecordHash, StringComparison.Ordinal);
                    Assert.DoesNotContain(SecretPrefix, syncRecord.PreviousHash, StringComparison.Ordinal);
                    // 主体哈希化生效：存储标识与明文凭证不相等（否则即为明文落库）。
                    Assert.NotEqual(c.Secret, syncRecord.SubjectIdentifier);

                    // ---- 断言二：持久化读回的 Kind / AccountLabel / Detail 不含明文哨兵。 ----
                    var reader = new SecurityAuditService(() => Factory());
                    var entries = reader.QueryAsync().GetAwaiter().GetResult();
                    Assert.Single(entries);
                    var entry = entries[0];

                    Assert.DoesNotContain(SecretPrefix, entry.Kind, StringComparison.Ordinal);
                    Assert.DoesNotContain(SecretPrefix, entry.AccountLabel, StringComparison.Ordinal);
                    Assert.DoesNotContain(SecretPrefix, entry.Detail, StringComparison.Ordinal);

                    // ---- 断言三：落盘字节（SQLCipher 加密）中亦不出现明文哨兵。 ----
                    SqliteConnection.ClearAllPools();
                    var raw = File.ReadAllBytes(dbPath);
                    // 同时检查 UTF-8 与 UTF-16LE 表示，覆盖明文以不同编码意外落库的情形。
                    Assert.False(ContainsBytes(raw, Encoding.UTF8.GetBytes(c.Secret)),
                        "明文凭证（UTF-8）出现在落盘字节中。");
                    Assert.False(ContainsBytes(raw, Encoding.Unicode.GetBytes(c.Secret)),
                        "明文凭证（UTF-16LE）出现在落盘字节中。");
                }
                finally
                {
                    SqliteConnection.ClearAllPools();
                    try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
                }
            },
            iter: PbtConfig.MinIterations);
    }

    /// <summary>朴素子序列字节查找，用于在落盘字节流中检测明文凭证哨兵是否泄露。</summary>
    private static bool ContainsBytes(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return false;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
