using OddOddities.Domain.Entities;

namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for text generation via OpenRouter.
/// </summary>
public interface ITextGenerationPort
{
    Task<(string TextContent, string Summary, string Theme, string SourceUrl, string Category, string Subcategory)> GenerateCuriosityAsync(
        string category,
        string subcategory,
        CancellationToken cancellationToken = default);
}
