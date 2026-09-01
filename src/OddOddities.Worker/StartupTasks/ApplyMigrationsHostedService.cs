using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OddOddities.Infrastructure.Data;

namespace OddOddities.Worker.StartupTasks;

/// <summary>
/// Hosted service that applies EF Core migrations on startup (RF-12).
/// Runs before the Worker starts scheduling pipeline executions.
/// </summary>
public sealed class ApplyMigrationsHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ApplyMigrationsHostedService> _logger;

    public ApplyMigrationsHostedService(
        IServiceProvider serviceProvider,
        ILogger<ApplyMigrationsHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OddOdditiesDbContext>();

        try
        {
            _logger.LogInformation("Applying database migrations...");
            await dbContext.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to apply database migrations. The worker will not start.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
