using OddOddities.Domain.Entities;

namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for Publication persistence operations.
/// </summary>
public interface IPublicationRepository
{
    Task<Publication?> GetByPostIdAsync(long postId, CancellationToken cancellationToken = default);

    Task<Publication> CreateAsync(Publication publication, CancellationToken cancellationToken = default);

    Task UpdateAsync(Publication publication, CancellationToken cancellationToken = default);
}
