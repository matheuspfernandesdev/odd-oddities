using OddOddities.Domain.Entities;

namespace OddOddities.Application.Pipeline;

/// <summary>
/// Shared mutable context for a single pipeline execution. Sub-contexts are filled in
/// by the steps that produce them and consumed by later steps. Kept as a class (not a
/// record) because the orchestrator runs steps in a foreach and each step needs to
/// progressively set state on the same instance.
/// </summary>
public sealed class PipelineContext
{
    public string ExecutionId { get; set; } = string.Empty;
    public CategorySelection Selection { get; set; } = new(0, 0, string.Empty, string.Empty);
    public TextContext Text { get; set; } = new(0, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
    public ImageContext Image { get; set; } = new(string.Empty, 0, 0, 0);
    public PublicationContext Publication { get; set; } = new(string.Empty, string.Empty, string.Empty, string.Empty);

    public static PipelineContext Create(
        string executionId,
        Category category,
        Subcategory subcategory) =>
        new()
        {
            ExecutionId = executionId,
            Selection = new CategorySelection(
                category.Id,
                subcategory.Id,
                category.Name,
                subcategory.Name)
        };
}

/// <summary>
/// Category/subcategory selection produced at the start of the pipeline.
/// </summary>
public sealed record CategorySelection(
    long CategoryId,
    long SubcategoryId,
    string CategoryName,
    string SubcategoryName);

/// <summary>
/// Output of the text generation step.
/// </summary>
public sealed record TextContext(
    long PostId,
    string TextContent,
    string Summary,
    string Theme,
    string ContentHash,
    string SourceUrl,
    string Caption);

/// <summary>
/// Output of the image generation step.
/// </summary>
public sealed record ImageContext(
    string ImageObjectKey,
    int Width,
    int Height,
    long Bytes);

/// <summary>
/// Output of the publication step.
/// </summary>
public sealed record PublicationContext(
    string MetaMediaId,
    string MetaPermalink,
    string MetaMediaStatus,
    string MetaMediaStatusCode);
