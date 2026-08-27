using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OddOddities.Application.Services;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;
using OddOddities.Infrastructure.Data;
using OddOddities.Infrastructure.DependencyInjection;
using OddOddities.Infrastructure.Logging;
using OddOddities.Worker;
using OddOddities.Worker.Middleware;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog with structured logging (RF-10)
// - Console sink with CompactJsonFormatter for stdout output
// - Sensitive data destructuring to redact API keys, tokens, and secrets
// - LogContext enricher for correlation properties (executionId, step, outcome, durationMs)
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .Destructure.With(new SensitiveDataDestructuringPolicy())
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateLogger();

builder.Host.UseSerilog(dispose: true);

// Configure AppConfiguration with IOptions pattern
builder.Services.Configure<AppConfiguration>(
    builder.Configuration.GetSection(AppConfiguration.SectionName));

// Configure EF Core with PostgreSQL
builder.Services.AddDbContext<OddOdditiesDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.MigrationsAssembly(typeof(OddOdditiesDbContext).Assembly.FullName);
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    });
});

// Configure health checks with PostgreSQL check
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "postgresql",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["db", "ready"]);

// Register Infrastructure services (repositories, adapters)
builder.Services.AddInfrastructureServices();

// Register PipelineOrchestrator (RF-01 / RF-11)
builder.Services.AddScoped<PipelineOrchestrator>();

// Add hosted service
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

// Global exception handler (RF-11): captures unhandled exceptions,
// logs structured error, returns sanitized 500 without stack trace
app.UseMiddleware<GlobalExceptionMiddleware>();

// Apply migrations on startup (RF-12: failure prevents scheduler from starting)
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<OddOdditiesDbContext>();

    try
    {
        logger.LogInformation("Applying database migrations...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Failed to apply database migrations. The worker will not start.");
        throw;
    }
}

// Map health check endpoint for Docker healthcheck
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.ToString()
            }),
            totalDuration = report.TotalDuration.ToString()
        };
        await context.Response.WriteAsJsonAsync(result);
    }
});

await app.RunAsync();
