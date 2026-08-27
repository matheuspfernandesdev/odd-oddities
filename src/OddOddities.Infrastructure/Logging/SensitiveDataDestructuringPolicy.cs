using OddOddities.Domain.ValueObjects;
using Serilog.Core;
using Serilog.Events;

namespace OddOddities.Infrastructure.Logging;

/// <summary>
/// Serilog destructuring policy that redacts sensitive fields in configuration objects.
/// Prevents API keys, tokens, secrets, and passwords from appearing in log output.
/// </summary>
public sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy
{
    private static readonly HashSet<Type> SensitiveTypes = new()
    {
        typeof(OpenRouterConfiguration),
        typeof(MetaConfiguration),
        typeof(MinioConfiguration),
        typeof(TokenEncryptionConfiguration),
        typeof(ConnectionStringsConfiguration)
    };

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue result)
    {
        var type = value.GetType();

        if (!SensitiveTypes.Contains(type))
        {
            result = null!;
            return false;
        }

        var properties = new List<LogEventProperty>
        {
            new("$type", new ScalarValue(type.Name))
        };

        foreach (var prop in type.GetProperties())
        {
            var propValue = prop.GetValue(value);
            var isSensitive = IsSensitiveProperty(prop.Name, propValue);

            var logValue = isSensitive
                ? new ScalarValue("***")
                : propertyValueFactory.CreatePropertyValue(propValue ?? string.Empty);

            properties.Add(new LogEventProperty(prop.Name, logValue));
        }

        result = new StructureValue(properties);
        return true;
    }

    private static bool IsSensitiveProperty(string propertyName, object? value)
    {
        if (value is null or string { Length: 0 })
        {
            return false;
        }

        var sensitiveNames = new[]
        {
            "ApiKey", "AppSecret", "AccessToken", "SecretKey",
            "Key", "Password", "ClientSecret", "Token"
        };

        return sensitiveNames.Any(sensitive =>
            propertyName.Contains(sensitive, StringComparison.OrdinalIgnoreCase));
    }
}
