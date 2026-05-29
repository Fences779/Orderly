using System.Text.Json;

namespace Orderly.Core.Models;

public sealed partial class StringNarrationProductionSheetSnapshot
{
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
}
