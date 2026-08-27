using OddOddities.Domain.Entities;

namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for SystemSetting persistence operations.
/// </summary>
public interface ISystemSettingRepository
{
    Task<SystemSetting?> GetByIdAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(SystemSetting setting, CancellationToken cancellationToken = default);
}
