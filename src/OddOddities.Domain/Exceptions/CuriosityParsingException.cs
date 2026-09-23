namespace OddOddities.Domain.Exceptions;

/// <summary>
/// Thrown when the model's curiosity JSON content cannot be parsed into the expected shape.
/// Signals a retryable text-generation failure caused by irregular model output
/// (array root, code fences, double-encoding, invalid JSON, etc.).
/// </summary>
public sealed class CuriosityParsingException : Exception
{
    private const int MaxSnippetLength = 300;

    /// <summary>Truncated snippet of the raw content that failed to parse.</summary>
    public string? RawContentSnippet { get; }

    public CuriosityParsingException(string? rawContent, string reason)
        : this(rawContent, reason, null)
    {
    }

    public CuriosityParsingException(string? rawContent, string reason, Exception? innerException)
        : base(BuildMessage(reason, rawContent), innerException)
    {
        RawContentSnippet = Truncate(rawContent);
    }

    private static string BuildMessage(string reason, string? rawContent)
        => $"{reason} Raw content: '{Truncate(rawContent)}'.";

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value.Length <= MaxSnippetLength
            ? value
            : value[..MaxSnippetLength] + "...";
    }
}
