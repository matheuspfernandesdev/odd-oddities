namespace OddOddities.Domain.Entities;

/// <summary>
/// Audit trail for changes to Post and Publication entities.
/// </summary>
public sealed class PostAudit
{
    public long Id { get; set; }
    public long PostId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    public Post Post { get; set; } = null!;
}
