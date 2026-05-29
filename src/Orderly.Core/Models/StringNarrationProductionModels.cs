using System.Text.Json;

namespace Orderly.Core.Models;

public sealed class StringNarrationProductionOrderSnapshot
{
    public string ProductionOrderId { get; set; } = string.Empty;
    public string ProductionOrderNo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public IReadOnlyList<StringNarrationWorkOrderSnapshot> WorkOrders { get; set; } = [];
    public JsonElement? Raw { get; set; }

    public bool HasData =>
        !string.IsNullOrWhiteSpace(ProductionOrderId)
        || !string.IsNullOrWhiteSpace(ProductionOrderNo)
        || !string.IsNullOrWhiteSpace(Status)
        || WorkOrders.Count > 0
        || Raw is not null;

    public string ProductionOrderNoText => BuildValue(ProductionOrderNo, "无制作单号");
    public string StatusText => BuildValue(Status, "未知制作单状态");
    public string SourceText => BuildValue(Source, "未知来源");
    public string RemarkText => BuildValue(Remark, "无制作备注");
    public string CreatedAtText => FormatGatewayTime(CreatedAt);
    public string UpdatedAtText => FormatGatewayTime(UpdatedAt);
    public string SummaryText => HasData
        ? $"{ProductionOrderNoText} / {StatusText} / {WorkOrders.Count} 条工单"
        : "暂无制作单";

    private static string BuildValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string FormatGatewayTime(long timestamp)
    {
        if (timestamp <= 0)
        {
            return "暂无";
        }

        try
        {
            var milliseconds = timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        }
        catch (ArgumentOutOfRangeException)
        {
            return "暂无";
        }
    }
}

public sealed class StringNarrationWorkOrderSnapshot
{
    public string WorkOrderId { get; set; } = string.Empty;
    public string WorkOrderNo { get; set; } = string.Empty;
    public string ProductionOrderNo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Assignee { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public JsonElement? Raw { get; set; }

    public string WorkOrderNoText => BuildValue(WorkOrderNo, "无工单号");
    public string StatusText => BuildValue(Status, "未知工单状态");
    public string AssigneeText => BuildValue(Assignee, "未分配");
    public string RemarkText => BuildValue(Remark, "无备注");
    public string CreatedAtText => FormatGatewayTime(CreatedAt);
    public string UpdatedAtText => FormatGatewayTime(UpdatedAt);

    private static string BuildValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string FormatGatewayTime(long timestamp)
    {
        if (timestamp <= 0)
        {
            return "暂无";
        }

        try
        {
            var milliseconds = timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        }
        catch (ArgumentOutOfRangeException)
        {
            return "暂无";
        }
    }
}

public sealed class StringNarrationProductionSheetMaterialItem
{
    public string MaterialName { get; set; } = string.Empty;
    public string QuantityText { get; set; } = string.Empty;

    public string MaterialNameText => BuildValue(MaterialName, "未提供原料名称");
    public string QuantityDisplayText => BuildValue(QuantityText, "未提供数量");
    public string SummaryText => $"{MaterialNameText} x {QuantityDisplayText}";

    private static string BuildValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}

public sealed class StringNarrationProductionSheetSnapshot
{
    private static readonly string[] MaterialCollectionNames =
    [
        "materials",
        "materialList",
        "materialItems",
        "rawMaterials",
        "ingredients",
        "components",
        "parts",
        "beads"
    ];

    private static readonly string[] MaterialNameFieldNames =
    [
        "materialName",
        "name",
        "title",
        "label",
        "itemName",
        "beadName",
        "partName",
        "componentName"
    ];

    private static readonly string[] QuantityFieldNames =
    [
        "quantity",
        "qty",
        "count",
        "num",
        "amount",
        "number",
        "usage"
    ];

    private static readonly string[] ArrangementFieldNames =
    [
        "arrangement",
        "arrangementText",
        "layout",
        "layoutText",
        "pattern",
        "patternText",
        "sequence",
        "sequenceText",
        "productionLayout",
        "arrangementDesc",
        "makingMethod"
    ];

    private static readonly string[] ImageFieldNames =
    [
        "exampleImage",
        "exampleImageUrl",
        "exampleImageUrls",
        "referenceImage",
        "referenceImageUrl",
        "image",
        "imageUrl",
        "preview",
        "previewUrl",
        "cover",
        "coverUrl",
        "thumbnail"
    ];

    private static readonly string[] BeadCollectionNames =
    [
        "beads",
        "beadList",
        "materials",
        "items"
    ];

    public string ProductionOrderNo { get; set; } = string.Empty;
    public string WorkOrderNo { get; set; } = string.Empty;
    public string WorkOrderStatus { get; set; } = string.Empty;
    public string WorkOrderStatusColorKey { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public string ArrangementText { get; set; } = string.Empty;
    public string ExampleImageUrl { get; set; } = string.Empty;
    public IReadOnlyList<StringNarrationProductionSheetMaterialItem> Materials { get; set; } = [];

    public bool HasMaterials => Materials.Count > 0;
    public bool HasExampleImage => !string.IsNullOrWhiteSpace(ExampleImageUrl);
    public string ProductionOrderNoText => BuildValue(ProductionOrderNo, "无制作单号");
    public string WorkOrderNoText => BuildValue(WorkOrderNo, "无工单号");
    public string WorkOrderStatusText => BuildValue(WorkOrderStatus, "未知工单状态");
    public string RemarkText => BuildValue(Remark, "无制作备注");
    public string ArrangementDisplayText => BuildValue(ArrangementText, "未提供排列方式");
    public string ExampleImageFallbackText => HasExampleImage ? string.Empty : "未提供例图";
    
    public string WristSizeText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ArrangementText)) return "暂无";
            var parts = ArrangementText.Split('/');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Contains("手围"))
                {
                    return trimmed.Replace("手围", "").Trim();
                }
            }
            return "暂无";
        }
    }

    public string LoopTypeText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ArrangementText)) return "暂无";
            var parts = ArrangementText.Split('/');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Contains("结构"))
                {
                    return trimmed.Replace("结构", "").Trim();
                }
            }
            return "暂无";
        }
    }

    public System.Collections.Generic.IReadOnlyList<string> ArrangementSteps
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ArrangementText)) return System.Array.Empty<string>();
            
            var parts = ArrangementText.Split('/');
            string sequencePart = string.Empty;
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Contains("顺序："))
                {
                    sequencePart = trimmed.Substring(trimmed.IndexOf("顺序：") + 3).Trim();
                    break;
                }
                if (trimmed.Contains("顺序:"))
                {
                    sequencePart = trimmed.Substring(trimmed.IndexOf("顺序:") + 3).Trim();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(sequencePart))
            {
                var remainingParts = parts
                    .Select(p => p.Trim())
                    .Where(p => !p.Contains("手围") && !p.Contains("结构"))
                    .Where(p => !string.IsNullOrWhiteSpace(p));
                sequencePart = string.Join(" ", remainingParts);
            }

            if (string.IsNullOrWhiteSpace(sequencePart))
            {
                return System.Array.Empty<string>();
            }

            var separators = new[] { "->", "→", "=>", "," };
            var rawSteps = sequencePart.Split(separators, System.StringSplitOptions.RemoveEmptyEntries);
            return rawSteps.Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        }
    }

    public System.Collections.Generic.IReadOnlyList<string> ArrangementFlowItems
    {
        get
        {
            var steps = ArrangementSteps;
            if (steps.Count == 0) return System.Array.Empty<string>();
            var flow = new System.Collections.Generic.List<string>();
            for (int i = 0; i < steps.Count; i++)
            {
                flow.Add(steps[i]);
                if (i < steps.Count - 1)
                {
                    flow.Add("→");
                }
            }
            return flow;
        }
    }

    public bool HasArrangementSteps => ArrangementSteps.Count > 0;
    public bool HasWristSize => WristSizeText != "暂无";
    public bool HasLoopType => LoopTypeText != "暂无";

    public bool HasDisplayableContent => HasMaterials || !string.IsNullOrWhiteSpace(ArrangementText) || HasExampleImage || !string.IsNullOrWhiteSpace(ProductionOrderNo) || !string.IsNullOrWhiteSpace(WorkOrderNo);

    public static StringNarrationProductionSheetSnapshot Create(StringNarrationOrderDetail? detail)
    {
        if (detail is null)
        {
            return new StringNarrationProductionSheetSnapshot();
        }

        var primaryWorkOrder = detail.WorkOrders.FirstOrDefault(item => item.Raw is not null)
            ?? detail.WorkOrders.FirstOrDefault()
            ?? detail.ProductionOrder.WorkOrders.FirstOrDefault(item => item.Raw is not null)
            ?? detail.ProductionOrder.WorkOrders.FirstOrDefault();

        var rawCandidates = new List<JsonElement>();
        if (primaryWorkOrder?.Raw is JsonElement workOrderRaw && workOrderRaw.ValueKind == JsonValueKind.Object)
        {
            rawCandidates.Add(workOrderRaw);
        }

        if (detail.ProductionOrder.Raw is JsonElement productionRaw && productionRaw.ValueKind == JsonValueKind.Object)
        {
            rawCandidates.Add(productionRaw);
        }

        var firstCover = detail.ItemsSnapshot
            .Select(item => item.Cover)
            .Concat(new[] { detail.CoverSnapshot })
            .Select(NormalizeImageUrl)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? string.Empty;

        var materials = ResolveMaterials(rawCandidates);
        if (materials.Count == 0)
        {
            materials = ResolveMaterialsFromProduct(detail);
        }

        var arrangementText = ResolveArrangementText(rawCandidates);
        if (string.IsNullOrWhiteSpace(arrangementText))
        {
            arrangementText = BuildArrangementFromProduct(detail);
        }

        var statusColorKey = MapStatusTextToCode(primaryWorkOrder?.Status, 
            MapStatusTextToCode(detail.ProductionOrder.Status, detail.FulfillmentStatus));

        return new StringNarrationProductionSheetSnapshot
        {
            ProductionOrderNo = FirstNonEmpty(primaryWorkOrder?.ProductionOrderNo, detail.ProductionOrder.ProductionOrderNo),
            WorkOrderNo = primaryWorkOrder?.WorkOrderNo ?? string.Empty,
            WorkOrderStatus = FirstNonEmpty(primaryWorkOrder?.Status, detail.ProductionOrder.Status, detail.FulfillmentStatusLabel),
            WorkOrderStatusColorKey = statusColorKey,
            Remark = FirstNonEmpty(primaryWorkOrder?.Remark, detail.ProductionOrder.Remark, detail.AdminRemark),
            ArrangementText = arrangementText,
            ExampleImageUrl = ResolveExampleImageUrl(rawCandidates, firstCover),
            Materials = materials
        };
    }

    private static IReadOnlyList<StringNarrationProductionSheetMaterialItem> ResolveMaterials(IEnumerable<JsonElement> candidates)
    {
        foreach (var candidate in candidates)
        {
            var materials = ExtractMaterials(candidate, 0);
            if (materials.Count > 0)
            {
                return materials;
            }
        }

        return [];
    }

    private static IReadOnlyList<StringNarrationProductionSheetMaterialItem> ExtractMaterials(JsonElement element, int depth)
    {
        if (depth > 5 || element.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        foreach (var collectionName in MaterialCollectionNames)
        {
            if (!TryGetPropertyIgnoreCase(element, collectionName, out var materialsElement) || materialsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var parsed = ParseMaterialArray(materialsElement);
            if (parsed.Count > 0)
            {
                return parsed;
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var nested = ExtractMaterials(property.Value, depth + 1);
            if (nested.Count > 0)
            {
                return nested;
            }
        }

        return [];
    }

    private static IReadOnlyList<StringNarrationProductionSheetMaterialItem> ParseMaterialArray(JsonElement arrayElement)
    {
        var result = new List<StringNarrationProductionSheetMaterialItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in arrayElement.EnumerateArray())
        {
            var material = ParseMaterialItem(item);
            if (material is null)
            {
                continue;
            }

            if (!seen.Add(material.SummaryText))
            {
                continue;
            }

            result.Add(material);
        }

        return result;
    }

    private static StringNarrationProductionSheetMaterialItem? ParseMaterialItem(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.String or JsonValueKind.Number)
        {
            var scalar = NormalizeScalar(element);
            return string.IsNullOrWhiteSpace(scalar)
                ? null
                : new StringNarrationProductionSheetMaterialItem { MaterialName = scalar, QuantityText = "未提供数量" };
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var materialName = ReadScalarProperty(element, MaterialNameFieldNames);
        if (string.IsNullOrWhiteSpace(materialName))
        {
            materialName = ReadFirstNonEmptyString(element);
        }

        if (string.IsNullOrWhiteSpace(materialName))
        {
            return null;
        }

        var quantityText = ReadScalarProperty(element, QuantityFieldNames);
        return new StringNarrationProductionSheetMaterialItem
        {
            MaterialName = materialName,
            QuantityText = string.IsNullOrWhiteSpace(quantityText) ? "未提供数量" : quantityText
        };
    }

    private static string ResolveArrangementText(IEnumerable<JsonElement> candidates)
    {
        foreach (var candidate in candidates)
        {
            var text = FindNamedValue(candidate, ArrangementFieldNames, 0);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static string ResolveExampleImageUrl(IEnumerable<JsonElement> candidates, string fallbackCover)
    {
        foreach (var candidate in candidates)
        {
            var imageUrl = NormalizeImageUrl(FindNamedValue(candidate, ImageFieldNames, 0));
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                return imageUrl;
            }
        }

        return NormalizeImageUrl(fallbackCover);
    }

    private static IReadOnlyList<StringNarrationProductionSheetMaterialItem> ResolveMaterialsFromProduct(StringNarrationOrderDetail detail)
    {
        var beads = ExtractBeadsFromDetail(detail);
        if (beads.Count == 0)
        {
            return [];
        }

        var grouped = beads
            .GroupBy(item => item.GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new StringNarrationProductionSheetMaterialItem
            {
                MaterialName = group.First().DisplayName,
                QuantityText = $"{group.Count()} 颗"
            })
            .OrderByDescending(item => ParseLeadingInt(item.QuantityText))
            .ThenBy(item => item.MaterialName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return grouped;
    }

    private static string BuildArrangementFromProduct(StringNarrationOrderDetail detail)
    {
        var beads = ExtractBeadsFromDetail(detail);
        if (beads.Count == 0)
        {
            return string.Empty;
        }

        var compressed = CompressBeadSequence(beads);
        var layoutText = string.Join(" -> ", compressed);
        var extras = new List<string>();
        var wristSize = ReadPositiveInt(detail.DesignSnapshot, "wristSizeMm");
        if (wristSize <= 0)
        {
            wristSize = detail.ItemsSnapshot
                .Select(item => ReadPositiveInt(item.Raw, "wristSizeMm"))
                .FirstOrDefault(value => value > 0);
        }

        if (wristSize > 0)
        {
            extras.Add($"手围 {wristSize}mm");
        }

        var loopType = ReadNonEmptyString(detail.DesignSnapshot, "loopType");
        if (string.IsNullOrWhiteSpace(loopType))
        {
            loopType = detail.ItemsSnapshot
                .Select(item => ReadNonEmptyString(item.Raw, "loopType"))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(loopType))
        {
            extras.Add($"结构 {NormalizeLoopType(loopType)}");
        }

        return extras.Count == 0
            ? layoutText
            : $"{string.Join(" / ", extras)} / 顺序：{layoutText}";
    }

    private static IReadOnlyList<ProductionBeadToken> ExtractBeadsFromDetail(StringNarrationOrderDetail detail)
    {
        var fromDesign = ExtractBeads(detail.DesignSnapshot);
        if (fromDesign.Count > 0)
        {
            return fromDesign;
        }

        foreach (var item in detail.ItemsSnapshot)
        {
            var fromItem = ExtractBeads(item.Raw);
            if (fromItem.Count > 0)
            {
                return fromItem;
            }
        }

        return [];
    }

    private static IReadOnlyList<ProductionBeadToken> ExtractBeads(JsonElement? source)
    {
        if (source is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        foreach (var collectionName in BeadCollectionNames)
        {
            if (!TryGetPropertyIgnoreCase(element, collectionName, out var collection) || collection.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var beads = ParseBeadArray(collection);
            if (beads.Count > 0)
            {
                return beads;
            }
        }

        return [];
    }

    private static IReadOnlyList<ProductionBeadToken> ParseBeadArray(JsonElement array)
    {
        var result = new List<ProductionBeadToken>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ReadScalarProperty(item, MaterialNameFieldNames);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = ReadScalarProperty(item, ["originalName"]);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var diameter = ReadPositiveInt(item, "diameterMm", "size");
            var displayName = diameter > 0 ? $"{name.Trim()}({diameter}mm)" : name.Trim();
            var quantity = ReadPositiveInt(item, QuantityFieldNames);
            if (quantity <= 0)
            {
                quantity = 1;
            }

            for (var index = 0; index < quantity; index++)
            {
                result.Add(new ProductionBeadToken(displayName, $"{name.Trim()}|{diameter}"));
            }
        }

        return result;
    }

    private static IReadOnlyList<string> CompressBeadSequence(IReadOnlyList<ProductionBeadToken> beads)
    {
        var result = new List<string>();
        if (beads.Count == 0)
        {
            return result;
        }

        var current = beads[0].DisplayName;
        var count = 1;
        for (var index = 1; index < beads.Count; index++)
        {
            if (string.Equals(beads[index].DisplayName, current, StringComparison.OrdinalIgnoreCase))
            {
                count++;
                continue;
            }

            result.Add(count > 1 ? $"{current} x{count}" : current);
            current = beads[index].DisplayName;
            count = 1;
        }

        result.Add(count > 1 ? $"{current} x{count}" : current);
        return result;
    }

    private static string FindNamedValue(JsonElement element, IReadOnlyList<string> fieldNames, int depth)
    {
        if (depth > 5)
        {
            return string.Empty;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var fieldName in fieldNames)
            {
                if (!TryGetPropertyIgnoreCase(element, fieldName, out var value))
                {
                    continue;
                }

                var extracted = ExtractMeaningfulText(value);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    return extracted;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                {
                    continue;
                }

                var nested = FindNamedValue(property.Value, fieldNames, depth + 1);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindNamedValue(item, fieldNames, depth + 1);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return string.Empty;
    }

    private static string ExtractMeaningfulText(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
        {
            return NormalizeScalar(value);
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var nested = ExtractMeaningfulText(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }

            return string.Empty;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            var direct = ReadFirstNonEmptyString(value);
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            foreach (var property in value.EnumerateObject())
            {
                var nested = ExtractMeaningfulText(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return string.Empty;
    }

    private static int ReadPositiveInt(JsonElement? element, params string[] fieldNames)
    {
        if (element is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        foreach (var fieldName in fieldNames)
        {
            if (!TryGetPropertyIgnoreCase(json, fieldName, out var value))
            {
                continue;
            }

            var parsed = ParsePositiveInt(value);
            if (parsed > 0)
            {
                return parsed;
            }
        }

        return 0;
    }

    private static string ReadScalarProperty(JsonElement element, IReadOnlyList<string> fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            if (!TryGetPropertyIgnoreCase(element, fieldName, out var value))
            {
                continue;
            }

            var scalar = NormalizeScalar(value);
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                return scalar;
            }
        }

        return string.Empty;
    }

    private static string ReadNonEmptyString(JsonElement? element, params string[] fieldNames)
    {
        if (element is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return ReadScalarProperty(json, fieldNames);
    }

    private static string ReadFirstNonEmptyString(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var property in element.EnumerateObject())
        {
            var scalar = NormalizeScalar(property.Value);
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                return scalar;
            }
        }

        return string.Empty;
    }

    private static int ParsePositiveInt(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsedByNumber))
        {
            return parsedByNumber > 0 ? parsedByNumber : 0;
        }

        var scalar = NormalizeScalar(value);
        return int.TryParse(scalar, out var parsedByString) && parsedByString > 0 ? parsedByString : 0;
    }

    private static string NormalizeScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeImageUrl(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return string.Equals(normalized, "[object Object]", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : normalized;
    }

    private static string NormalizeLoopType(string value)
    {
        return value.Trim() switch
        {
            "single" => "单圈",
            "double" => "双圈",
            _ => value.Trim()
        };
    }

    private static int ParseLeadingInt(string value)
    {
        var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) ? parsed : 0;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string MapStatusTextToCode(string? statusText, string defaultCode)
    {
        if (string.IsNullOrWhiteSpace(statusText)) return defaultCode;
        statusText = statusText.Trim();
        return statusText switch
        {
            "已支付待确认" or "paid_pending_confirm" => "paid_pending_confirm",
            "待制作" or "pending_make" => "pending_make",
            "制作中" or "making" => "making",
            "待发货" or "ready_to_ship" => "ready_to_ship",
            "已发货" or "shipped" => "shipped",
            "已完成" or "completed" => "completed",
            "异常" or "exception" => "exception",
            _ => defaultCode
        };
    }

    private static string BuildValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private sealed record ProductionBeadToken(string DisplayName, string GroupKey);
}
