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

public sealed partial class StringNarrationProductionSheetSnapshot
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
}
