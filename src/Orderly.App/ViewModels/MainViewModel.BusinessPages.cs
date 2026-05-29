namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private async Task EnsureBusinessSectionLoadedAsync(string section)
    {
        if (string.Equals(section, SectionInventory, StringComparison.Ordinal) && !_hasLoadedInventoryOnce)
        {
            await RefreshInventoryAsync();
        }
        else if (string.Equals(section, SectionCashflow, StringComparison.Ordinal) && !_hasLoadedCashflowOnce)
        {
            await RefreshCashflowAsync();
        }
    }

    private bool CanRunBusinessDataReadAction()
    {
        return !IsInventoryLoading && !IsCashflowLoading;
    }

    private static string FormatCurrency(decimal? value)
    {
        if (!value.HasValue)
        {
            return "不可得";
        }

        return value.Value == 0 ? "¥0" : $"¥{value.Value:N0}";
    }

    private static string BuildDisplayText(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string FormatBusinessTimestamp(long timestamp)
    {
        if (timestamp <= 0)
        {
            return "未同步";
        }

        try
        {
            var milliseconds = timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        }
        catch (ArgumentOutOfRangeException)
        {
            return "未同步";
        }
    }
}
