using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OddOddities.Application.DependencyInjection;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;
using OddOddities.Infrastructure.Data;
using OddOddities.Infrastructure.DependencyInjection;
using OddOddities.Infrastructure.Logging;
using OddOddities.Infrastructure.Middleware;
using OddOddities.Worker.HealthChecks;
using OddOddities.Worker.StartupTasks;
using Serilog;
using Serilog.Formatting.Compact;
using OddOddities.Worker;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .Destructure.With(new SensitiveDataDestructuringPolicy())
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateLogger();

builder.Host.UseSerilog(dispose: true);

builder.Services.Configure<AppConfiguration>(
    builder.Configuration.GetSection(AppConfiguration.SectionName));

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

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "postgresql",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["db", "ready"]);

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices();

builder.Services.AddHostedService<ApplyMigrationsHostedService>();
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();
app.MapHealthChecks("/health");

await app.RunAsync();
