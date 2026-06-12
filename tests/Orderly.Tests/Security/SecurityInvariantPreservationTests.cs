using System.Security.Cryptography;
using CsCheck;
using Orderly.Core.Models;
using Orderly.Core.Security;
using Orderly.Data.Services;
using Orderly.Tests.Fakes;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// Property 8 (Preservation) — 既有安全不变量保持不变。
///
/// 本测试遵循"观察优先（observation-first）"方法学：在**未修复代码**上观察既有安全不变量
/// 的可观察行为，并将其固化为属性测试，作为修复（任务 7.1–7.5）必须保持的回归防护契约。
///
/// 对应 design.md Preservation / Property 8：
///   FOR ALL input WHERE NOT isBugCondition(input):
///     originalFunction(input) == fixedFunction(input)
///   即既有安全不变量（参数化查询、标识符白名单、PBKDF2-SHA256、失败锁定、AEAD、SSRF、
///   输入边界校验、所有者门控与跨账户再校验）在修复后保持不变。
///
/// **可测范围（已记录的后端缝隙约束）**：
///   本测试项目目标框架为 net8.0，仅引用 <c>Orderly.Core</c> / <c>Orderly.Data</c>，
///   且不存在 InternalsVisibleTo。因此下列不变量从后端公共缝隙直接、稳定可测，并在此固化：
///     - **AEAD 字段加密（AES-GCM）**：<see cref="FieldEncryptionService"/> 的往返、密文格式、
///       随机 nonce、防篡改（认证失败）、附加数据绑定、无数据密钥时 fail-closed。
///     - **输入边界校验**：<see cref="MasterPasswordPolicy"/> 主密码策略；
///       <see cref="LocalOcrService"/> 的 OCR 来源路径校验。
///     - **SSRF 防护**：OCR 来源拒绝 UNC/网络共享路径。
///   下列不变量因实现细节不在 net8.0 后端公共缝隙内（<c>internal LocalCredentialSecurity</c>
///   的 PBKDF2-SHA256/密钥包装；<c>CredentialAttemptTracker</c> 失败锁定依赖真实文件系统 + Windows DPAPI；
///   参数化查询与标识符白名单位于 SQLite 仓储/初始化层；所有者门控与跨账户再校验需完整会话装配）
///   未在此处以属性测试编码，留待集成测试或带 InternalsVisibleTo 的专用测试覆盖，**未触碰任何 UI/锁定区域**。
///
/// **EXPECTED OUTCOME**: 在未修复代码上 PASS（确认需要保持的安全不变量基线）。
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.7, 3.8, 3.9, 3.11**
/// </summary>
public sealed class SecurityInvariantPreservationTests
{
    // 可打印的明文生成器（含空格，避免控制字符触发无关校验差异）。
    private static readonly Gen<string> PlaintextGen =
        Gen.OneOf(Gen.Const(' '), Gen.Char['!', '~']).Array[0, 200]
            .Select(chars => new string(chars));

    private static readonly Gen<string> NonEmptyTokenGen =
        Gen.Char['a', 'z'].Array[1, 32].Select(chars => new string(chars));

    private static FieldEncryptionService NewEncryptor(out FakeDataKeySessionContextService session)
    {
        session = new FakeDataKeySessionContextService(RandomNumberGenerator.GetBytes(32));
        return new FieldEncryptionService(session);
    }

    /// <summary>
    /// Property 8a — AEAD 往返不变量：Encrypt 后 Decrypt 还原原文，且密文非明文透传、带 v1: 前缀。
    /// </summary>
    [Fact]
    public void Aead_encrypt_decrypt_roundtrip_is_preserved()
    {
        PlaintextGen.Sample(plaintext =>
        {
            var encryptor = NewEncryptor(out _);

            var ciphertext = encryptor.Encrypt(plaintext);
            var decrypted = encryptor.Decrypt(ciphertext);

            Assert.Equal(plaintext, decrypted);
            Assert.StartsWith("v1:", ciphertext);
            if (plaintext.Length > 0)
            {
                // 真实 AEAD（非空操作透传）：密文不等于明文。
                Assert.NotEqual(plaintext, ciphertext);
            }
        });
    }

    /// <summary>
    /// Property 8b — 随机 nonce 不变量：相同明文两次加密产生不同密文，但都能解回原文。
    /// </summary>
    [Fact]
    public void Aead_uses_random_nonce_per_encryption()
    {
        PlaintextGen.Where(p => p.Length > 0).Sample(plaintext =>
        {
            var encryptor = NewEncryptor(out _);

            var first = encryptor.Encrypt(plaintext);
            var second = encryptor.Encrypt(plaintext);

            Assert.NotEqual(first, second);
            Assert.Equal(plaintext, encryptor.Decrypt(first));
            Assert.Equal(plaintext, encryptor.Decrypt(second));
        });
    }

    /// <summary>
    /// Property 8c — 防篡改不变量：翻转密文任一字节（nonce/tag/密文区）都会使解密以受控异常失败（AEAD 认证）。
    ///
    /// 在**字节级**翻转：解码 base64 载荷后翻转 nonce/tag/密文字节（跳过版本字节 index 0），
    /// 再重新编码。任何真实的密文字节变更都必被 AES-GCM 认证捕获并拒绝。
    /// （注：字符级翻转可能落在 base64 的无效填充位上而解码为相同字节，故此处采用字节级翻转。）
    /// </summary>
    [Fact]
    public void Aead_detects_tampering()
    {
        var gen =
            from plaintext in PlaintextGen.Where(p => p.Length > 0)
            from flipPicker in Gen.Int[0, 1_000_000]
            select (plaintext, flipPicker);

        gen.Sample(input =>
        {
            var (plaintext, flipPicker) = input;
            var encryptor = NewEncryptor(out _);
            var ciphertext = encryptor.Encrypt(plaintext);

            const int prefixLength = 3; // "v1:"
            var payload = Convert.FromBase64String(ciphertext[prefixLength..]);

            // 跳过版本字节（index 0），在 nonce/tag/密文区选定字节并保证发生真实变更。
            var index = (flipPicker % (payload.Length - 1)) + 1;
            payload[index] ^= 0xFF;

            var tampered = ciphertext[..prefixLength] + Convert.ToBase64String(payload);

            // 任何 nonce/tag/密文字节的篡改都必被 AEAD 认证捕获 → 受控失败。
            Assert.Throws<InvalidOperationException>(() => encryptor.Decrypt(tampered));
        });
    }

    /// <summary>
    /// Property 8d — 附加数据（AEAD AAD）绑定不变量：以 AAD 加密的密文，
    /// 用不同 AAD 解密失败，用相同 AAD 解密还原原文。
    /// </summary>
    [Fact]
    public void Aead_associated_data_binding_is_preserved()
    {
        var gen =
            from plaintext in PlaintextGen
            from ad1 in NonEmptyTokenGen
            from ad2 in NonEmptyTokenGen
            select (plaintext, ad1, ad2);

        gen.Sample(input =>
        {
            var (plaintext, ad1, ad2) = input;
            var encryptor = NewEncryptor(out _);

            var ciphertext = encryptor.Encrypt(plaintext, ad1);

            // 相同 AAD：还原原文。
            Assert.Equal(plaintext, encryptor.Decrypt(ciphertext, ad1));

            // 不同 AAD：受控失败。
            if (!string.Equals(ad1, ad2, StringComparison.Ordinal))
            {
                Assert.Throws<InvalidOperationException>(() => encryptor.Decrypt(ciphertext, ad2));
            }
        });
    }

    /// <summary>
    /// Property 8e — fail-closed 不变量：无可用会话数据密钥时，加密以受控异常拒绝（绝不明文落盘）。
    /// </summary>
    [Fact]
    public void Aead_fails_closed_when_no_data_key_is_available()
    {
        PlaintextGen.Sample(plaintext =>
        {
            var encryptor = NewEncryptor(out var session);
            session.SuspendDataKey();

            Assert.Throws<InvalidOperationException>(() => encryptor.Encrypt(plaintext));
        });
    }

    /// <summary>
    /// Property 8f — 输入边界校验不变量：合法强主密码被接受；弱/含空白/超界被拒绝。
    /// </summary>
    [Fact]
    public void Master_password_policy_input_validation_is_preserved()
    {
        // 合法强密码：满足全部类别要求且无空白、长度恒 >= 最小长度（区间内）。
        // 各类别数组下限取 3，加固定后缀 "Aa1!"（4 位），总长 >= 3*3+1+4 = 14 >= MinimumLength(12)。
        var strongGen =
            from upper in Gen.Char['A', 'Z'].Array[3, 8]
            from lower in Gen.Char['a', 'z'].Array[3, 8]
            from digit in Gen.Char['0', '9'].Array[3, 8]
            from special in Gen.OneOfConst('!', '@', '#', '$', '%', '^', '&', '*')
            select new string(upper) + new string(lower) + new string(digit) + special + "Aa1!";

        strongGen.Sample(password =>
        {
            // 截断到最大长度内（保持合法）。
            var bounded = password.Length > MasterPasswordPolicy.MaximumLength
                ? password[..MasterPasswordPolicy.MaximumLength]
                : password;
            var ok = MasterPasswordPolicy.TryValidate(bounded, out var error);
            Assert.True(ok, $"合法强主密码被拒绝：\"{bounded}\"（{error}）。");
            Assert.Equal(string.Empty, error);
        });

        // 含空白：必被拒绝（防注入/边界校验保持）。
        var withWhitespaceGen =
            from token in Gen.Char['a', 'z'].Array[1, 10].Select(c => new string(c))
            select "Aa1!" + token + " " + token;

        withWhitespaceGen.Sample(password =>
        {
            Assert.False(
                MasterPasswordPolicy.TryValidate(password, out _),
                $"含空白的主密码未被拒绝：\"{password}\"。");
        });

        // 过短：必被拒绝。
        Gen.Char['a', 'z'].Array[0, MasterPasswordPolicy.MinimumLength - 1]
            .Select(c => new string(c))
            .Sample(tooShort =>
            {
                Assert.False(
                    MasterPasswordPolicy.TryValidate(tooShort, out _),
                    $"过短的主密码未被拒绝：\"{tooShort}\"（长度 {tooShort.Length}）。");
            });
    }

    /// <summary>
    /// Property 8g — SSRF 防护不变量：OCR 来源拒绝 UNC/网络共享路径。
    /// </summary>
    [Fact]
    public void Ocr_source_rejects_network_share_paths_ssrf_guard_is_preserved()
    {
        var gen =
            from server in NonEmptyTokenGen
            from share in NonEmptyTokenGen
            from file in NonEmptyTokenGen
            select $@"\\{server}\{share}\{file}.png";

        gen.Sample(uncPath =>
        {
            var service = NewOcrService();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                service.CreateOcrTaskAsync(new OcrResult
                {
                    CustomerId = 1,
                    SourcePath = uncPath,
                    SourceName = "img.png",
                }).GetAwaiter().GetResult());

            Assert.Contains("网络共享", ex.Message);
        });
    }

    /// <summary>
    /// Property 8h — 输入边界校验不变量：OCR 来源拒绝非白名单扩展名（仅允许图片）。
    /// </summary>
    [Fact]
    public void Ocr_source_rejects_disallowed_extensions_is_preserved()
    {
        var gen =
            from name in NonEmptyTokenGen
            from ext in Gen.OneOfConst("txt", "exe", "dll", "js", "pdf", "zip")
            select (name, ext);

        gen.Sample(input =>
        {
            var (name, ext) = input;
            var service = NewOcrService();
            var path = $@"C:\images\{name}.{ext}";

            Assert.Throws<InvalidOperationException>(() =>
                service.CreateOcrTaskAsync(new OcrResult
                {
                    CustomerId = 1,
                    SourcePath = path,
                    SourceName = $"{name}.{ext}",
                }).GetAwaiter().GetResult());
        });
    }

    private static LocalOcrService NewOcrService()
        => new(
            new InMemoryOcrResultRepository(),
            new RecordingActivityLogRepository(),
            new NoOpConversationService(),
            new NoOpConversationMessageRepository());
}
