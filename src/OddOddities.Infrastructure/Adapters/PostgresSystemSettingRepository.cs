using Microsoft.EntityFrameworkCore;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Interfaces;
using OddOddities.Infrastructure.Data;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// PostgreSQL implementation of ISystemSettingRepository using Entity Framework Core.
/// Provides data access for SystemSetting key/value operations.
/// </summary>
public sealed class PostgresSystemSettingRepository : ISystemSettingRepository
{
    private readonly OddOdditiesDbContext _context;

    public PostgresSystemSettingRepository(OddOdditiesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<SystemSetting?> GetByIdAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return await _context.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SystemSettings
            .AsNoTracking()
            .OrderBy(s => s.Key)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(SystemSetting setting, CancellationToken cancellationToken = default)
    {
        if (setting is null)
            throw new ArgumentNullException(nameof(setting));

        var existing = await _context.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == setting.Key, cancellationToken);

        if (existing is not null)
        {
            existing.Value = setting.Value;
            existing.IsEncrypted = setting.IsEncrypted;
            existing.Description = setting.Description;
            existing.UpdatedAt = setting.UpdatedAt;
        }
        else
        {
            _context.SystemSettings.Add(setting);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
