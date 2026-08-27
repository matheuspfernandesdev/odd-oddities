# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY src/OddOddities.Domain/OddOddities.Domain.csproj src/OddOddities.Domain/
COPY src/OddOddities.Application/OddOddities.Application.csproj src/OddOddities.Application/
COPY src/OddOddities.Infrastructure/OddOddities.Infrastructure.csproj src/OddOddities.Infrastructure/
COPY src/OddOddities.Worker/OddOddities.Worker.csproj src/OddOddities.Worker/
RUN dotnet restore src/OddOddities.Worker/OddOddities.Worker.csproj

# Copy source code and build
COPY src/ src/
RUN dotnet publish src/OddOddities.Worker/OddOddities.Worker.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN addgroup --system --gid 1001 appgroup && \
    adduser --system --uid 1001 --ingroup appgroup appuser

# Copy published application
COPY --from=build /app/publish .

# Set ownership
RUN chown -R appuser:appgroup /app

USER appuser

# Expose no ports (this is a worker, not a web server)
# EXPOSE is not needed for background workers

# Set environment variables
ENV DOTNET_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

ENTRYPOINT ["dotnet", "OddOddities.Worker.dll"]
