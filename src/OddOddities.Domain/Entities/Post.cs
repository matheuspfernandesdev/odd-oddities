using OddOddities.Domain.Enums;

namespace OddOddities.Domain.Entities;

/// <summary>
/// Represents a complete post (curiosity + image) ready for or published to Instagram.
/// </summary>
public sealed class Post
{
    public long Id { get; set; }
    public long CategoryId { get; set; }
    public long SubcategoryId { get; set; }
    public string TextContent { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string ImageObjectKey { get; set; } = string.Empty;
    public int ImageWidth { get; set; } = 1080;
    public int ImageHeight { get; set; } = 1080;
    public long ImageBytes { get; set; }
    public PostStatus Status { get; set; } = PostStatus.Generated;
    public FailureStep? FailureStep { get; set; }
    public string? FailureReason { get; set; }
    public string? ErrorCode { get; set; }
    public string? FailureDetails { get; set; }
    public string Caption { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }

    public Category Category { get; set; } = null!;
    public Subcategory Subcategory { get; set; } = null!;
    public Publication? Publication { get; set; }
    public ICollection<GenerationAttempt> GenerationAttempts { get; set; } = new List<GenerationAttempt>();
}
