using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OddOddities.Domain.ValueObjects;
using OddOddities.Infrastructure.Data;
using OddOddities.Worker;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

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

// Add hosted service
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

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
