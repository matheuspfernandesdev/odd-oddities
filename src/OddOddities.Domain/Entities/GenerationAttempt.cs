using OddOddities.Domain.Enums;

namespace OddOddities.Domain.Entities;

/// <summary>
/// Records each attempt to generate content for a Post, even when rejected.
/// </summary>
public sealed class GenerationAttempt
{
    public long Id { get; set; }
    public long PostId { get; set; }
    public int AttemptNumber { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public AttemptStatus Status { get; set; }
    public string? RejectionReason { get; set; }
    public string? RawResponse { get; set; }
    public decimal? CostUsd { get; set; }
    public int? TokensIn { get; set; }
    public int? TokensOut { get; set; }
    public long DurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Post Post { get; set; } = null!;
}
