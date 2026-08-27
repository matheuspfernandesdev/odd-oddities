using OddOddities.Domain.Entities;

namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for PostAudit persistence operations.
/// </summary>
public interface IPostAuditRepository
{
    Task<PostAudit> CreateAsync(PostAudit audit, CancellationToken cancellationToken = default);
}
