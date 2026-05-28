using System.Text.Json;
using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class StringNarrationGatewayBusinessService : IStringNarrationBusinessService
{
    private const string InventoryListAction = "inventoryList";
    private const string CashflowListAction = "cashflowList";

    private readonly StringNarrationGatewayClient _client;

    public StringNarrationGatewayBusinessService(StringNarrationGatewayClient client)
    {
        _client = client;
    }

    public async Task<StringNarrationInventoryListResult> GetInventoryAsync(StringNarrationInventoryQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new StringNarrationInventoryQuery();
        var root = await _client.InvokeAsync(InventoryListAction, query, cancellationToken);
        var payload = GetPayloadRoot(root);
        var items = ParseInventoryItems(payload);
        var movements = ParseInventoryMovements(payload);
        return new StringNarrationInventoryListResult
        {
            Items = items,
            RecentMovements = movements,
            Total = ReadInt(payload, "total", "count"),
            UpdatedAt = ReadLong(payload, "updatedAt", "calculatedAt", "syncedAt")
        };
    }

    public async Task<StringNarrationCashflowListResult> GetCashflowAsync(StringNarrationCashflowQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new StringNarrationCashflowQuery();
        var root = await _client.InvokeAsync(CashflowListAction, query, cancellationToken);
        var payload = GetPayloadRoot(root);
        var entries = ParseCashflowEntries(payload);
        var summary = TryGet(payload, "summary", out var summaryElement) && summaryElement.ValueKind == JsonValueKind.Object
            ? ParseCashflowSummary(summaryElement, entries)
            : BuildCashflowSummary(entries);

        return new StringNarrationCashflowListResult
        {
            Entries = entries,
            Summary = summary,
            Total = ReadInt(payload, "total", "count"),
            UpdatedAt = ReadLong(payload, "updatedAt", "calculatedAt", "syncedAt")
        };
    }

    private static IReadOnlyList<StringNarrationInventoryItem> ParseInventoryItems(JsonElement root)
    {
        var array = GetFirstArray(root, "items", "inventory", "skus", "list");
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<StringNarrationInventoryItem>();
        foreach (var element in array.EnumerateArray())
        {
            items.Add(new StringNarrationInventoryItem
            {
                Id = ReadString(element, "_id", "id"),
                SkuId = ReadString(element, "skuId", "_id", "id"),
                Name = ReadString(element, "name", "title"),
                Category = ReadString(element, "category"),
                BasePrice = ReadDecimal(element, "basePrice"),
                CostPrice = ReadDecimal(element, "costPrice"),
                PurchasePrice = ReadDecimal(element, "purchasePrice"),
                StockOnHand = ReadDecimal(element, "stockOnHand", "stock", "quantity"),
                StockReserved = ReadDecimal(element, "stockReserved", "reservedStock"),
                SafetyStock = ReadDecimal(element, "safetyStock", "stockWarningThreshold"),
                StockUnit = ReadString(element, "stockUnit", "unit"),
                StockLocation = ReadString(element, "stockLocation", "location"),
                SupplierName = ReadString(element, "supplierName", "supplier"),
                InventoryRemark = ReadString(element, "inventoryRemark", "remark"),
                ReorderEnabled = ReadBool(element, "reorderEnabled"),
                Enabled = !TryGet(element, "enabled", out var enabledElement) || ReadBool(enabledElement),
                LastRestockedAt = ReadLong(element, "lastRestockedAt", "restockedAt"),
                UpdatedAt = ReadLong(element, "updatedAt"),
                Tags = ReadStringArray(element, "tags")
            });
        }

        return items;
    }

    private static IReadOnlyList<StringNarrationInventoryMovement> ParseInventoryMovements(JsonElement root)
    {
        var array = GetFirstArray(root, "recentMovements", "movements", "inventoryMovements");
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<StringNarrationInventoryMovement>();
        foreach (var element in array.EnumerateArray())
        {
            items.Add(new StringNarrationInventoryMovement
            {
                Id = ReadString(element, "_id", "id"),
                SkuId = ReadString(element, "skuId"),
                SkuName = ReadString(element, "skuName", "name"),
                MovementType = ReadString(element, "movementType", "type"),
                Quantity = ReadDecimal(element, "quantity"),
                UnitCost = ReadDecimal(element, "unitCost"),
                TotalCost = ReadDecimal(element, "totalCost"),
                RelatedOrderNo = ReadString(element, "relatedOrderNo", "orderNo"),
                OperatorId = ReadString(element, "operatorId", "operator"),
                Note = ReadString(element, "note", "remark"),
                OccurredAt = ReadLong(element, "occurredAt", "createdAt")
            });
        }

        return items;
    }

    private static IReadOnlyList<StringNarrationCashflowEntry> ParseCashflowEntries(JsonElement root)
    {
        var array = GetFirstArray(root, "entries", "cashflow", "cashflows", "list");
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var entries = new List<StringNarrationCashflowEntry>();
        foreach (var element in array.EnumerateArray())
        {
            entries.Add(new StringNarrationCashflowEntry
            {
                Id = ReadString(element, "_id", "id"),
                Direction = ReadString(element, "direction", "type"),
                Amount = ReadDecimal(element, "amount"),
                Category = ReadString(element, "category"),
                PaymentMethod = ReadString(element, "paymentMethod", "channel"),
                Status = ReadString(element, "status"),
                RelatedOrderId = ReadString(element, "relatedOrderId", "orderId"),
                RelatedOrderNo = ReadString(element, "relatedOrderNo", "orderNo"),
                RelatedQuoteId = ReadString(element, "relatedQuoteId", "quoteId"),
                RelatedSkuId = ReadString(element, "relatedSkuId", "skuId"),
                CounterpartyName = ReadString(element, "counterpartyName", "counterparty"),
                OperatorId = ReadString(element, "operatorId", "operator"),
                Note = ReadString(element, "note", "remark"),
                OccurredAt = ReadLong(element, "occurredAt", "paidAt", "createdAt"),
                CreatedAt = ReadLong(element, "createdAt")
            });
        }

        return entries;
    }

    private static StringNarrationCashflowSummary ParseCashflowSummary(JsonElement element, IReadOnlyList<StringNarrationCashflowEntry> entries)
    {
        var fallback = BuildCashflowSummary(entries);
        return new StringNarrationCashflowSummary
        {
            IncomeTotal = ReadDecimal(element, "incomeTotal", "income") is var income && income != 0 ? income : fallback.IncomeTotal,
            ExpenseTotal = ReadDecimal(element, "expenseTotal", "expense") is var expense && expense != 0 ? expense : fallback.ExpenseTotal,
            NetAmount = ReadDecimal(element, "netAmount", "net") is var net && net != 0 ? net : fallback.NetAmount,
            ReceivableAmount = ReadDecimal(element, "receivableAmount", "receivable"),
            PayableAmount = ReadDecimal(element, "payableAmount", "payable"),
            EntryCount = ReadInt(element, "entryCount", "count") is var count && count > 0 ? count : fallback.EntryCount
        };
    }

    private static StringNarrationCashflowSummary BuildCashflowSummary(IReadOnlyList<StringNarrationCashflowEntry> entries)
    {
        var income = entries.Where(entry => entry.IsIncome).Sum(entry => entry.Amount);
        var expense = entries.Where(entry => !entry.IsIncome).Sum(entry => entry.Amount);
        return new StringNarrationCashflowSummary
        {
            IncomeTotal = income,
            ExpenseTotal = expense,
            NetAmount = income - expense,
            EntryCount = entries.Count
        };
    }

    private static JsonElement GetPayloadRoot(JsonElement root)
    {
        if (TryGet(root, "data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            return data;
        }

        if (TryGet(root, "payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
        {
            return payload;
        }

        return root;
    }

    private static JsonElement GetFirstArray(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGet(element, name, out var property) && property.ValueKind == JsonValueKind.Array)
            {
                return property;
            }
        }

        return default;
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement property)
    {
        property = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out property);
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
            {
                continue;
            }

            var value = property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty
            };
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static decimal ReadDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String && decimal.TryParse(property.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static int ReadInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static long ReadLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
            {
                return NormalizeTimestamp(number);
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (long.TryParse(value, out var parsedLong))
                {
                    return NormalizeTimestamp(parsedLong);
                }

                if (DateTimeOffset.TryParse(value, out var parsedDate))
                {
                    return parsedDate.ToUnixTimeMilliseconds();
                }
            }
        }

        return 0;
    }

    private static bool ReadBool(JsonElement element, params string[] names)
    {
        if (names.Length == 0)
        {
            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(element.GetString(), out var parsed) && parsed,
                JsonValueKind.Number => element.TryGetInt32(out var number) && number != 0,
                _ => false
            };
        }

        foreach (var name in names)
        {
            if (TryGet(element, name, out var property) && ReadBool(property))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var property))
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            return property.EnumerateArray()
                .Select(item => ReadStringValue(item))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }

        var raw = ReadStringValue(property);
        return string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split([',', '，', '/', '、', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ReadStringValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static long NormalizeTimestamp(long timestamp)
    {
        if (timestamp <= 0)
        {
            return 0;
        }

        return timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
    }
}
