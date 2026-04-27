namespace Orderly.Core.Models;

public static class CustomerStatusCatalog
{
    public static string GetLabel(CustomerStatus status)
    {
        return status switch
        {
            CustomerStatus.Active => "活跃",
            CustomerStatus.Dormant => "沉默",
            CustomerStatus.Blocked => "受限",
            CustomerStatus.Archived => "已归档",
            _ => status.ToString()
        };
    }
}
