using System;
using System.Linq;

namespace Orderly.App.ViewModels.Helpers;

/// <summary>
/// 密码强度档枚举（设计 §8.5 / Req 8.5）。
/// </summary>
public enum PasswordStrength
{
    Empty,
    Weak,
    Fair,
    Good,
    Strong
}

/// <summary>
/// 密码强度评估纯函数（设计 §9.2 / Req 8.5、8.9）。
/// 规则面向「本地优先 + 商家自用」场景：在长度与字符类别多样性的正向打分之上，
/// 叠加弱模式（长重复串 / 简单顺序模式）惩罚降档。
/// 评估在内存中即时进行，不记录、不持久化、不出现在日志（P4 安全底线）。
/// </summary>
public static class PasswordStrengthEvaluator
{
    /// <summary>
    /// 评估密码强度。空串 → <see cref="PasswordStrength.Empty"/>；长度 &lt; 8 一律
    /// <see cref="PasswordStrength.Weak"/>；其余按长度 / 类别正向打分并叠加弱模式惩罚降档。
    /// </summary>
    public static PasswordStrength Evaluate(string password)
    {
        if (string.IsNullOrEmpty(password)) return PasswordStrength.Empty;

        int length = password.Length;
        bool hasLower = password.Any(char.IsLower);
        bool hasUpper = password.Any(char.IsUpper);
        bool hasDigit = password.Any(char.IsDigit);
        bool hasSymbol = password.Any(c => !char.IsLetterOrDigit(c));
        int categories = (hasLower ? 1 : 0) + (hasUpper ? 1 : 0)
                       + (hasDigit ? 1 : 0) + (hasSymbol ? 1 : 0);

        // 硬下限：长度 < 8 一律 Weak。
        if (length < 8) return PasswordStrength.Weak;

        int score = 0;
        if (length >= 8) score++;
        if (length >= 12) score++;
        if (length >= 16) score++;
        if (categories >= 2) score++;
        if (categories >= 3) score++;
        if (categories >= 4) score++;

        // 弱模式 / 可预测性惩罚（在长度/类别评分之上叠加，不改变既有打分维度）：
        // 命中重复或可预测弱模式时下调档位，使「追加字符却引入弱模式」可能降低强度。
        score -= WeakPatternPenalty(password);
        if (score < 0) score = 0;

        return score switch
        {
            <= 2 => PasswordStrength.Weak,
            3 => PasswordStrength.Fair,
            4 => PasswordStrength.Good,
            _ => PasswordStrength.Strong
        };
    }

    /// <summary>
    /// 检测重复 / 可预测弱模式，返回惩罚分（0 表示无惩罚）。
    /// 仅作为附加惩罚层，不参与长度/类别的正向打分。
    /// </summary>
    private static int WeakPatternPenalty(string password)
    {
        int penalty = 0;

        // 1) 长重复串：同一字符连续出现 ≥ 4 次（如 "aaaa"、"1111"）。
        int run = 1, maxRun = 1;
        for (int i = 1; i < password.Length; i++)
        {
            run = password[i] == password[i - 1] ? run + 1 : 1;
            if (run > maxRun) maxRun = run;
        }
        if (maxRun >= 4) penalty += 2;
        else if (maxRun >= 3) penalty += 1;

        // 2) 简单顺序模式：连续递增/递减 ≥ 4 个字符（如 "1234"、"abcd"、"4321"）。
        int seqUp = 1, seqDown = 1, maxSeq = 1;
        for (int i = 1; i < password.Length; i++)
        {
            int delta = password[i] - password[i - 1];
            seqUp = delta == 1 ? seqUp + 1 : 1;
            seqDown = delta == -1 ? seqDown + 1 : 1;
            maxSeq = Math.Max(maxSeq, Math.Max(seqUp, seqDown));
        }
        if (maxSeq >= 4) penalty += 2;

        return penalty;
    }
}
