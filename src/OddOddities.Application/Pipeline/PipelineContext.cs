using OddOddities.Domain.Entities;
using OddOddities.Domain.ValueObjects;

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

    /// <summary>
    /// Modality of this execution, decided once per run by the orchestrator through
    /// ISchedulerPort.IsVideoRunToday() (RF-15). False (image run) until the
    /// orchestrator resolves it.
    /// </summary>
    public bool IsVideoRun { get; set; }

    /// <summary>
    /// Effective cost ceiling (USD) for this execution, resolved once per run by the
    /// orchestrator: ModelSelection.MaxCostPerVideoRunUsd for video runs and
    /// ModelSelection.MaxCostPerRunUsd for image runs. Defaults to the image-run
    /// ceiling so a context built outside the orchestrator keeps the pre-RF-15 budget.
    /// </summary>
    public decimal CostCeilingUsd { get; set; } = new ModelSelectionConfiguration().MaxCostPerRunUsd;

    /// <summary>
    /// Video generation output (RF-17). Null on image runs and until the video step runs.
    /// </summary>
    public VideoContext? Video { get; set; }

    /// <summary>
    /// Accepted comment suggestion (RF-20). Null when there is no suggestion this run.
    /// </summary>
    public SuggestionContext? Suggestion { get; set; }

    /// <summary>
    /// Accumulated USD cost of this pipeline execution (text + image or text + video),
    /// used to enforce PipelineContext.CostCeilingUsd across generation attempts.
    /// </summary>
    public decimal AccumulatedCostUsd { get; set; }

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

/// <summary>
/// Output of the video generation step (RF-17). Declared in RF-15 so later steps can
/// depend on the shape; the orchestrator leaves PipelineContext.Video null until the
/// video step fills it.
/// </summary>
public sealed record VideoContext(
    string ObjectKey,
    long Bytes,
    int DurationSeconds,
    decimal CostUsd,
    string ModelId);

/// <summary>
/// Comment suggestion accepted for this execution (RF-20), produced by the
/// CommentSuggestionStep and consumed by TextGenerationStep (RF-21) and
/// PublicationStep (RF-22). Declared in RF-15; PipelineContext.Suggestion stays null
/// until a suggestion is accepted.
/// </summary>
public sealed record SuggestionContext(
    string Theme,
    string Summary,
    string AuthorUsername,
    string CommentId,
    string SourceCommentText);
