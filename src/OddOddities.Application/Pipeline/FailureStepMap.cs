using OddOddities.Domain.Enums;

namespace OddOddities.Application.Pipeline;

/// <summary>
/// Maps pipeline step names (as declared by IPipelineStep.StepName) to <see cref="FailureStep"/>.
/// Centralizes the string→enum mapping so we don't have two parallel switch statements.
/// </summary>
internal static class FailureStepMap
{
    private static readonly IReadOnlyDictionary<string, FailureStep> ByName =
        new Dictionary<string, FailureStep>(StringComparer.OrdinalIgnoreCase)
        {
            ["textgeneration"] = FailureStep.TextGeneration,
            ["sourcevalidation"] = FailureStep.SourceValidation,
            ["imagegeneration"] = FailureStep.ImageGeneration,
            ["imagestorage"] = FailureStep.ImageStorage,
            ["minio"] = FailureStep.ImageStorage,
            ["database"] = FailureStep.Database,
            ["instagramapi"] = FailureStep.InstagramApi,
            ["metapublishing"] = FailureStep.InstagramApi,
            // "videogenerationstep" is the name required by RF-14 AC5; RF-17 declares the
            // step's StepName as "VideoGeneration", so both are mapped to avoid falling
            // back to TextGeneration for video failures.
            ["videogenerationstep"] = FailureStep.VideoGeneration,
            ["videogeneration"] = FailureStep.VideoGeneration,
            ["videostorage"] = FailureStep.VideoStorage
        };

    /// <summary>
    /// Returns the <see cref="FailureStep"/> for a given step name.
    /// Falls back to <see cref="FailureStep.TextGeneration"/> when the name is unknown.
    /// </summary>
    public static FailureStep FromStepName(string stepName)
    {
        if (!string.IsNullOrEmpty(stepName) &&
            ByName.TryGetValue(stepName, out var mapped))
        {
            return mapped;
        }

        return FailureStep.TextGeneration;
    }
}
