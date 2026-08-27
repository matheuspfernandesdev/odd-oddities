using OddOddities.Domain.Entities;

namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for GenerationAttempt persistence operations.
/// </summary>
public interface IGenerationAttemptRepository
{
    Task<GenerationAttempt> CreateAsync(GenerationAttempt attempt, CancellationToken cancellationToken = default);

    Task<int> GetAttemptCountForPostAsync(long postId, CancellationToken cancellationToken = default);
}
