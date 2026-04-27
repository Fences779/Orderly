namespace Orderly.Core.Models;

public sealed class ReplyTemplate
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Scene { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }
    public string SourcePlatform { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
