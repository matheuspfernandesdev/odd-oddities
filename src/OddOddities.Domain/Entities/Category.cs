namespace OddOddities.Domain.Entities;

/// <summary>
/// Represents a content category (e.g., Science, Nature, History).
/// </summary>
public sealed class Category
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Subcategory> Subcategories { get; set; } = new List<Subcategory>();
}
