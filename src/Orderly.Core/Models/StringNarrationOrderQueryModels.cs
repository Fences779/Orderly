namespace Orderly.Core.Models;

public sealed class StringNarrationOrderQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string Keyword { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string FulfillmentStatus { get; set; } = string.Empty;
    public long StartAt { get; set; }
    public long EndAt { get; set; }
}

public sealed class StringNarrationPageInfo
{
    public int PageSize { get; set; }
    public bool HasMore { get; set; }
    public string NextCursor { get; set; } = string.Empty;
    public int Total { get; set; }
}

public sealed class StringNarrationOrderListResult
{
    public IReadOnlyList<StringNarrationOrderSummary> Orders { get; set; } = [];
    public StringNarrationPageInfo PageInfo { get; set; } = new();
    public StringNarrationFulfillmentStats Stats { get; set; } = new();
}

public sealed class StringNarrationGatewayResponse<T>
{
    public bool Ok { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
}

public sealed class StringNarrationWhoamiResult
{
    public bool Authorized { get; set; }
    public string Gateway { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
    public string OperatorOpenid { get; set; } = string.Empty;
    public IReadOnlyList<string> Permissions { get; set; } = [];
}
