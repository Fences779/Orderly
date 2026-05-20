namespace Orderly.Core.Models;

public sealed class CreateFirstOwnerRequest
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string MasterPassword { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public bool ImportLegacyDatabase { get; set; } = true;
    public bool OverwriteTargetOnLegacyImport { get; set; }
}
