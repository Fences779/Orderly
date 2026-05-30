using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private static string FormatBackupCounts(IReadOnlyDictionary<string, int> counts)
    {
        if (counts.Count == 0)
        {
            return string.Empty;
        }

        var prioritizedKeys = new[]
        {
            "Customers",
            "Deals",
            "Orders",
            "FollowUps",
            "CustomerNotes",
            "PriceAdjustments",
            "ConversationMessages",
            "AiSuggestions",
            "OcrResults",
            "ActivityLogs",
            "SyncRecords"
        };

        var parts = new List<string>();
        foreach (var key in prioritizedKeys)
        {
            if (counts.TryGetValue(key, out var count))
            {
                parts.Add($"{key}:{count}");
            }
        }

        return string.Join(" / ", parts);
    }

    private static string GetRestoreTargetCode(BackupRestoreTargetState targetState)
    {
        return targetState switch
        {
            BackupRestoreTargetState.EmptyDatabase => "Empty",
            BackupRestoreTargetState.QaDatabase => "QaOnly",
            BackupRestoreTargetState.NonEmptyProductionDatabase => "ProductionNonEmpty",
            _ => "Unknown"
        };
    }

    private static string GetRestoreTargetLabel(BackupRestoreTargetState targetState)
    {
        return targetState switch
        {
            BackupRestoreTargetState.EmptyDatabase => "空库",
            BackupRestoreTargetState.QaDatabase => "QA/测试库",
            BackupRestoreTargetState.NonEmptyProductionDatabase => "非空生产库",
            _ => "未知"
        };
    }
}
