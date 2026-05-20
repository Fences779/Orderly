namespace Orderly.Core.Services;

public static class HotkeyTextValidator
{
    private static readonly HashSet<string> AllowedModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl",
        "Control",
        "Alt",
        "Shift",
        "Win",
        "Windows"
    };

    private static readonly HashSet<string> AllowedNamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Back",
        "Tab",
        "Enter",
        "Return",
        "Escape",
        "Esc",
        "Space",
        "Insert",
        "Delete",
        "Home",
        "End",
        "PageUp",
        "PageDown",
        "Up",
        "Down",
        "Left",
        "Right",
        "Add",
        "Subtract",
        "Multiply",
        "Divide"
    };

    public static bool IsValid(string? hotkey)
    {
        return TryNormalizeForDuplicate(hotkey, out _);
    }

    public static string NormalizeOrFallback(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return IsValid(candidate) ? candidate : fallback;
    }

    public static bool TryNormalizeForDuplicate(string? hotkey, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return false;
        }

        var parts = hotkey
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        if (parts.Length < 2)
        {
            return false;
        }

        var modifiers = parts.Take(parts.Length - 1).ToArray();
        if (modifiers.Length == 0 || modifiers.Any(modifier => !AllowedModifiers.Contains(modifier)))
        {
            return false;
        }

        var key = NormalizeKey(parts[^1]);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalizedModifiers = modifiers
            .Select(static modifier => modifier.ToUpperInvariant() switch
            {
                "CONTROL" => "CTRL",
                "WINDOWS" => "WIN",
                _ => modifier.ToUpperInvariant()
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static modifier => modifier switch
            {
                "CTRL" => 0,
                "ALT" => 1,
                "SHIFT" => 2,
                "WIN" => 3,
                _ => 9
            })
            .ThenBy(static modifier => modifier, StringComparer.Ordinal)
            .ToArray();

        normalized = $"{string.Join("+", normalizedModifiers)}+{key}";
        return true;
    }

    private static string NormalizeKey(string key)
    {
        var candidate = key.Trim();
        if (candidate.Length == 1 && char.IsLetterOrDigit(candidate[0]))
        {
            return char.ToUpperInvariant(candidate[0]).ToString();
        }

        var upper = candidate.ToUpperInvariant();
        if (upper.Length is >= 2 and <= 3 && upper[0] == 'F' && int.TryParse(upper[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            return upper;
        }

        if (upper.StartsWith("NUMPAD", StringComparison.Ordinal) && upper.Length == 7 && char.IsDigit(upper[6]))
        {
            return $"NUMPAD{upper[6]}";
        }

        if (!AllowedNamedKeys.Contains(candidate))
        {
            return string.Empty;
        }

        return upper switch
        {
            "CONTROL" => string.Empty,
            "CTRL" => string.Empty,
            "ALT" => string.Empty,
            "SHIFT" => string.Empty,
            "WIN" => string.Empty,
            "WINDOWS" => string.Empty,
            "ESC" => "ESCAPE",
            "RETURN" => "ENTER",
            _ => candidate
        };
    }
}
