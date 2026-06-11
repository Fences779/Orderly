using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orderly.Data.Services;

internal static class QaDataScope
{
    public const string CurrentTag = "p13qa";
    public const string CurrentDisplayMarker = "[P1.3_QA]";
    public const string P2DisplayMarker = "[P2_QA]";
    private const int MaxQaMetadataJsonCharacters = 8192;
    private const int MaxLegacyMetadataCharacters = 1024;

    private static readonly JsonDocumentOptions QaMetadataJsonDocumentOptions = new()
    {
        MaxDepth = 16
    };

    private static readonly string[] QaRemoteIdPrefixes = ["p13qa-", "p2qa-", "p35qa-", "p36qa-"];
    private static readonly string[] QaExternalIdPrefixes = ["p13qa-", "p2qa-", "p35qa-", "p36qa-"];
    private static readonly string[] LegacyTextMarkers =
    [
        CurrentDisplayMarker,
        P2DisplayMarker,
        "[P1.4.1_QA]",
        "[P1_QA_RUNTIME]",
        "【P。3——QA"
    ];
    private static readonly string[] QaMetadataSnippets = [$"\"qa\":{{\"tag\":\"{CurrentTag}\""];

    public static string BuildCustomerSelfPredicate(string alias = "")
    {
        return BuildAny(
            BuildGlobClause(Qualify(alias, "RemoteId"), "$qaRemotePattern", QaRemoteIdPrefixes.Length),
            BuildGlobClause(Qualify(alias, "ExternalId"), "$qaExternalPattern", QaExternalIdPrefixes.Length),
            BuildMarkerClause(Qualify(alias, "Name")),
            BuildMarkerClause(Qualify(alias, "Remark")));
    }

    public static string BuildDealSelfPredicate(string alias = "")
    {
        return BuildAny(
            BuildGlobClause(Qualify(alias, "RemoteId"), "$qaRemotePattern", QaRemoteIdPrefixes.Length),
            BuildMarkerClause(Qualify(alias, "Title")),
            BuildMarkerClause(Qualify(alias, "Requirement")));
    }

    public static string BuildOrderSelfPredicate(string alias = "")
    {
        return BuildAny(
            BuildGlobClause(Qualify(alias, "RemoteId"), "$qaRemotePattern", QaRemoteIdPrefixes.Length),
            BuildGlobClause(Qualify(alias, "ExternalId"), "$qaExternalPattern", QaExternalIdPrefixes.Length),
            BuildMarkerClause(Qualify(alias, "Title")),
            BuildMarkerClause(Qualify(alias, "Requirement")));
    }

    public static string BuildFollowUpSelfPredicate(string alias = "")
    {
        return BuildAny(
            BuildGlobClause(Qualify(alias, "RemoteId"), "$qaRemotePattern", QaRemoteIdPrefixes.Length),
            BuildMarkerClause(Qualify(alias, "Title")),
            BuildMarkerClause(Qualify(alias, "Content")));
    }

    public static string BuildNoteSelfPredicate(string alias = "")
    {
        return BuildAny(
            BuildGlobClause(Qualify(alias, "RemoteId"), "$qaRemotePattern", QaRemoteIdPrefixes.Length),
            BuildMarkerClause(Qualify(alias, "Content")));
    }

    public static string BuildPriceAdjustmentSelfPredicate(string alias = "")
    {
        return BuildAny(
            BuildGlobClause(Qualify(alias, "RemoteId"), "$qaRemotePattern", QaRemoteIdPrefixes.Length),
            BuildMarkerClause(Qualify(alias, "Reason")));
    }

    public static string BuildActivityLogSelfPredicate(string alias = "")
    {
        return BuildAny(
            BuildGlobClause(Qualify(alias, "RemoteId"), "$qaRemotePattern", QaRemoteIdPrefixes.Length),
            BuildMarkerClause(Qualify(alias, "Title")),
            BuildMarkerClause(Qualify(alias, "Description")),
            BuildMarkerClause(Qualify(alias, "MetadataJson")),
            BuildMetadataClause(Qualify(alias, "MetadataJson")));
    }

    public static string BuildConversationMessageSelfPredicate(string alias = "")
    {
        return BuildAny(
            BuildGlobClause(Qualify(alias, "RemoteId"), "$qaRemotePattern", QaRemoteIdPrefixes.Length),
            BuildMarkerClause(Qualify(alias, "SenderName")),
            BuildMarkerClause(Qualify(alias, "Content")),
            BuildMarkerClause(Qualify(alias, "SourceMessageId")),
            BuildMarkerClause(Qualify(alias, "MetadataJson")),
            BuildMetadataClause(Qualify(alias, "MetadataJson")));
    }

    public static string BuildAiSuggestionSelfPredicate(string alias = "")
    {
        return BuildAny(
            BuildGlobClause(Qualify(alias, "RemoteId"), "$qaRemotePattern", QaRemoteIdPrefixes.Length),
            BuildMarkerClause(Qualify(alias, "SuggestionText")),
            BuildMarkerClause(Qualify(alias, "Reason")),
            BuildMarkerClause(Qualify(alias, "MetadataJson")),
            BuildMetadataClause(Qualify(alias, "MetadataJson")));
    }

    public static string BuildOcrResultSelfPredicate(string alias = "")
    {
        return BuildAny(
            BuildGlobClause(Qualify(alias, "RemoteId"), "$qaRemotePattern", QaRemoteIdPrefixes.Length),
            BuildMarkerClause(Qualify(alias, "SourcePath")),
            BuildMarkerClause(Qualify(alias, "SourceName")),
            BuildMarkerClause(Qualify(alias, "ExtractedText")),
            BuildMarkerClause(Qualify(alias, "ErrorMessage")),
            BuildMarkerClause(Qualify(alias, "MetadataJson")),
            BuildMetadataClause(Qualify(alias, "MetadataJson")));
    }

    public static string BuildSyncRecordSelfPredicate(string alias = "")
    {
        return BuildAny(
            BuildGlobClause(Qualify(alias, "RemoteId"), "$qaRemotePattern", QaRemoteIdPrefixes.Length),
            BuildMarkerClause(Qualify(alias, "EntityType")),
            BuildMarkerClause(Qualify(alias, "ErrorMessage")),
            BuildMarkerClause(Qualify(alias, "MetadataJson")),
            BuildMetadataClause(Qualify(alias, "MetadataJson")));
    }

    public static string BuildCustomerScopePredicate(string alias = "")
    {
        return BuildCustomerSelfPredicate(alias);
    }

    public static string BuildDealScopePredicate(string alias = "")
    {
        return BuildDealSelfPredicate(alias);
    }

    public static string BuildOrderScopePredicate(string alias = "")
    {
        return BuildOrderSelfPredicate(alias);
    }

    public static string BuildFollowUpScopePredicate(string alias = "")
    {
        return BuildFollowUpSelfPredicate(alias);
    }

    public static string BuildNoteScopePredicate(string alias = "")
    {
        return BuildNoteSelfPredicate(alias);
    }

    public static string BuildPriceAdjustmentScopePredicate(string alias = "")
    {
        return BuildPriceAdjustmentSelfPredicate(alias);
    }

    public static string BuildActivityLogScopePredicate(string alias = "")
    {
        return BuildActivityLogSelfPredicate(alias);
    }

    public static string BuildConversationMessageScopePredicate(string alias = "")
    {
        return BuildAny(
            BuildConversationMessageSelfPredicate(alias),
            $"{Qualify(alias, "CustomerId")} IN ({BuildQaCustomerIdSet()})",
            $"{Qualify(alias, "DealId")} IN ({BuildQaDealIdSet()})",
            $"{Qualify(alias, "OrderId")} IN ({BuildQaOrderIdSet()})");
    }

    public static string BuildAiSuggestionScopePredicate(string alias = "")
    {
        return BuildAny(
            BuildAiSuggestionSelfPredicate(alias),
            $"{Qualify(alias, "CustomerId")} IN ({BuildQaCustomerIdSet()})",
            $"{Qualify(alias, "OrderId")} IN ({BuildQaOrderIdSet()})",
            $"{Qualify(alias, "MessageId")} IN ({BuildQaConversationMessageIdSet()})");
    }

    public static string BuildOcrResultScopePredicate(string alias = "")
    {
        return BuildAny(
            BuildOcrResultSelfPredicate(alias),
            $"{Qualify(alias, "CustomerId")} IN ({BuildQaCustomerIdSet()})",
            $"{Qualify(alias, "OrderId")} IN ({BuildQaOrderIdSet()})");
    }

    public static string BuildSyncRecordScopePredicate(string alias = "")
    {
        return BuildAny(
            BuildSyncRecordSelfPredicate(alias),
            BuildEntityAssociationClause(alias, "Customer", BuildQaCustomerIdSet()),
            BuildEntityAssociationClause(alias, "Deal", BuildQaDealIdSet()),
            BuildEntityAssociationClause(alias, "Order", BuildQaOrderIdSet()),
            BuildEntityAssociationClause(alias, "ConversationMessage", BuildQaConversationMessageIdSet()),
            BuildEntityAssociationClause(alias, "AiSuggestion", BuildQaAiSuggestionIdSet()),
            BuildEntityAssociationClause(alias, "OcrResult", BuildQaOcrResultIdSet()));
    }

    public static string BuildCustomerAssociationPredicate(string alias = "")
    {
        return BuildCustomerSelfPredicate(alias);
    }

    public static string BuildDealAssociationPredicate(string alias = "")
    {
        return BuildAny(
            BuildDealSelfPredicate(alias),
            $"{Qualify(alias, "CustomerId")} IN ({BuildQaCustomerIdSet()})");
    }

    public static string BuildOrderAssociationPredicate(string alias = "")
    {
        return BuildAny(
            BuildOrderSelfPredicate(alias),
            $"{Qualify(alias, "CustomerId")} IN ({BuildQaCustomerIdSet()})",
            $"{Qualify(alias, "DealId")} IN ({BuildQaDealIdSet()})");
    }

    public static string BuildActivityLogAssociationPredicate(string alias = "")
    {
        return BuildAny(
            BuildActivityLogSelfPredicate(alias),
            $"{Qualify(alias, "CustomerId")} IN ({BuildQaCustomerIdSet()})",
            $"{Qualify(alias, "DealId")} IN ({BuildQaDealIdSet()})",
            $"{Qualify(alias, "OrderId")} IN ({BuildQaOrderIdSet()})");
    }

    public static void AddScopeParameters(SqliteCommand command)
    {
        AddValues(command, "$qaRemotePattern", QaRemoteIdPrefixes, prefix => $"{prefix}*");
        AddValues(command, "$qaExternalPattern", QaExternalIdPrefixes, prefix => $"{prefix}*");
        AddValues(command, "$qaMarker", LegacyTextMarkers, marker => marker);
        AddValues(command, "$qaMetadataSnippet", QaMetadataSnippets, snippet => snippet);
    }

    public static string BuildSeedActivityMetadataJson(string remoteId)
    {
        return EnsureActivityMetadataTagged(string.Empty, "seed", remoteId);
    }

    public static string EnsureActivityMetadataTagged(string metadataJson, string source, string? key = null)
    {
        var root = ParseObject(metadataJson);
        var qa = new JsonObject
        {
            ["tag"] = CurrentTag,
            ["source"] = source
        };

        if (!string.IsNullOrWhiteSpace(key))
        {
            qa["key"] = key;
        }

        var markers = new JsonArray();
        foreach (var marker in LegacyTextMarkers)
        {
            markers.Add(marker);
        }

        qa["markers"] = markers;
        root["qa"] = qa;
        return root.ToJsonString();
    }

    private static JsonObject ParseObject(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson) || metadataJson.Length > MaxQaMetadataJsonCharacters)
        {
            return new JsonObject();
        }

        try
        {
            if (JsonNode.Parse(metadataJson, documentOptions: QaMetadataJsonDocumentOptions) is JsonObject root)
            {
                return root;
            }
        }
        catch (JsonException)
        {
        }

        return new JsonObject
        {
            ["legacyMetadata"] = metadataJson.Length <= MaxLegacyMetadataCharacters
                ? metadataJson
                : metadataJson[..MaxLegacyMetadataCharacters]
        };
    }

    private static string BuildQaCustomerIdSet()
    {
        return $"SELECT Id FROM Customers WHERE {BuildCustomerSelfPredicate("Customers")}";
    }

    private static string BuildQaDealIdSet()
    {
        return $"SELECT Id FROM Deals WHERE {BuildDealSelfPredicate("Deals")} OR Deals.CustomerId IN ({BuildQaCustomerIdSet()})";
    }

    private static string BuildQaOrderIdSet()
    {
        return $"SELECT Id FROM Orders WHERE {BuildOrderSelfPredicate("Orders")} OR Orders.CustomerId IN ({BuildQaCustomerIdSet()}) OR Orders.DealId IN ({BuildQaDealIdSet()})";
    }

    private static string BuildQaConversationMessageIdSet()
    {
        return $"SELECT Id FROM ConversationMessages WHERE {BuildConversationMessageScopePredicate("ConversationMessages")}";
    }

    private static string BuildQaAiSuggestionIdSet()
    {
        return $"SELECT Id FROM AiSuggestions WHERE {BuildAiSuggestionScopePredicate("AiSuggestions")}";
    }

    private static string BuildQaOcrResultIdSet()
    {
        return $"SELECT Id FROM OcrResults WHERE {BuildOcrResultScopePredicate("OcrResults")}";
    }

    private static string BuildGlobClause(string column, string parameterBase, int count)
    {
        return BuildJoinedClause(column, parameterBase, count, parameterName => $"ifnull({column}, '') GLOB {parameterName}");
    }

    private static string BuildMarkerClause(string column)
    {
        return BuildJoinedClause(column, "$qaMarker", LegacyTextMarkers.Length, parameterName => $"instr(ifnull({column}, ''), {parameterName}) > 0");
    }

    private static string BuildMetadataClause(string column)
    {
        return BuildJoinedClause(column, "$qaMetadataSnippet", QaMetadataSnippets.Length, parameterName => $"instr(ifnull({column}, ''), {parameterName}) > 0");
    }

    private static string BuildJoinedClause(string column, string parameterBase, int count, Func<string, string> buildPart)
    {
        if (count <= 0)
        {
            return "0 = 1";
        }

        var parts = new string[count];
        for (var index = 0; index < count; index++)
        {
            parts[index] = buildPart($"{parameterBase}{index}");
        }

        return $"({string.Join(" OR ", parts)})";
    }

    private static void AddValues<T>(SqliteCommand command, string parameterBase, IReadOnlyList<T> values, Func<T, object> convert)
    {
        for (var index = 0; index < values.Count; index++)
        {
            command.Parameters.AddWithValue($"{parameterBase}{index}", convert(values[index]));
        }
    }

    private static string BuildAny(params string[] clauses)
    {
        return $"({string.Join(" OR ", clauses.Where(static clause => !string.IsNullOrWhiteSpace(clause)))})";
    }

    private static string BuildEntityAssociationClause(string alias, string entityType, string idSet)
    {
        return $"({Qualify(alias, "EntityType")} = '{entityType}' AND {Qualify(alias, "EntityId")} IN ({idSet}))";
    }

    private static string Qualify(string alias, string column)
    {
        return string.IsNullOrWhiteSpace(alias) ? column : $"{alias}.{column}";
    }
}
