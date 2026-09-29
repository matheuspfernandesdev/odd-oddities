namespace OddOddities.Domain.Exceptions;

/// <summary>
/// Thrown when an OpenRouter call for a specific model fails at the API/transport level
/// (non-success status, empty/malformed response, invalid model id, etc.).
/// Signals a model-level failure that should trigger fallback to the next candidate model.
/// </summary>
public sealed class OpenRouterModelException : Exception
{
    /// <summary>The model id that was requested when the failure occurred.</summary>
    public string ModelId { get; }

    /// <summary>HTTP status code of the failed call, when available.</summary>
    public int? StatusCode { get; }

    public OpenRouterModelException(string modelId, int? statusCode, string message)
        : base(message)
    {
        ModelId = modelId;
        StatusCode = statusCode;
    }

    public OpenRouterModelException(string modelId, int? statusCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ModelId = modelId;
        StatusCode = statusCode;
    }
}
