namespace OddOddities.Domain.Entities;

/// <summary>
/// Records the interaction with the Meta Instagram Graph API for a Post.
/// </summary>
public sealed class Publication
{
    public long Id { get; set; }
    public long PostId { get; set; }
    public string MetaMediaId { get; set; } = string.Empty;
    public string MetaMediaStatus { get; set; } = string.Empty;
    public string MetaMediaStatusCode { get; set; } = string.Empty;
    public string? MetaPermalink { get; set; }
    public int AttemptCount { get; set; } = 1;
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Post Post { get; set; } = null!;
}
