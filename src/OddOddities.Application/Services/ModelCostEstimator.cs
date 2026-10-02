using OddOddities.Domain.Constants;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Application.Services;

/// <summary>
/// Static cost-estimation helpers shared by model selection and pipeline steps.
/// Estimates are used for pre-call budget filtering; actual costs from API usage
/// accumulate on the pipeline context after each call.
/// </summary>
public static class ModelCostEstimator
{
    /// <summary>Estimated USD cost of one text generation request for the given model.</summary>
    public static decimal EstimateTextCostPerRequest(ModelDescriptor model)
    {
        if (model.IsFree)
            return 0m;

        // Unknown pricing (e.g. preferred model missing from the catalog): cannot
        // estimate — treat as 0 so the preferred model is never filtered out;
        // the actual cost still accumulates after the call.
        if (model.PromptPricePerToken is null && model.CompletionPricePerToken is null)
            return 0m;

        return (model.PromptPricePerToken ?? 0m) * PipelineConstants.EstimatedPromptTokens
             + (model.CompletionPricePerToken ?? 0m) * PipelineConstants.EstimatedCompletionTokens;
    }

    /// <summary>Estimated USD cost of one image generation request for the given model.</summary>
    public static decimal EstimateImageCostPerRequest(ModelDescriptor model)
    {
        if (model.IsFree)
            return 0m;

        return model.ImageOutputPrice ?? 0m;
    }

    /// <summary>Returns true when the estimated cost still fits in the run budget.</summary>
    public static bool FitsBudget(decimal accumulatedCostUsd, decimal estimatedCostUsd, decimal maxCostPerRunUsd)
        => accumulatedCostUsd + estimatedCostUsd <= maxCostPerRunUsd;
}
