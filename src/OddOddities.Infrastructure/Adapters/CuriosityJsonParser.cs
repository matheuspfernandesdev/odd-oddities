using System.Text.Json;
using System.Text.Json.Serialization;
using OddOddities.Domain.Exceptions;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// Robustly parses the raw JSON content returned by the text-generation model
/// into a <see cref="CuriosityPayload"/>. Handles common LLM output variations:
/// array roots, markdown code fences, and double-encoded JSON strings.
/// </summary>
public static class CuriosityJsonParser
{
    private const int MaxUnwrapAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parses raw model content into a <see cref="CuriosityPayload"/>.
    /// </summary>
    /// <exception cref="CuriosityParsingException">When the content cannot be parsed.</exception>
    public static CuriosityPayload Parse(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
            throw new CuriosityParsingException(contentJson, "Model returned null or empty content.");

        var current = StripCodeFences(contentJson.Trim());

        for (var attempt = 0; attempt < MaxUnwrapAttempts; attempt++)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(current);
            }
            catch (JsonException ex)
            {
                throw new CuriosityParsingException(contentJson, "Model content is not valid JSON.", ex);
            }

            using (document)
            {
                var root = document.RootElement;

                switch (root.ValueKind)
                {
                    case JsonValueKind.Object:
                        return Deserialize(root, contentJson);

                    case JsonValueKind.Array:
                        if (root.GetArrayLength() == 0)
                            throw new CuriosityParsingException(
                                contentJson,
                                "Model returned an empty JSON array.");

                        var first = root[0];
                        if (first.ValueKind == JsonValueKind.Object)
                            return Deserialize(first, contentJson);

                        if (first.ValueKind == JsonValueKind.String)
                        {
                            current = first.GetString() ?? string.Empty;
                            continue;
                        }

                        throw new CuriosityParsingException(
                            contentJson,
                            "Model returned a JSON array without an object element.");

                    case JsonValueKind.String:
                        current = root.GetString() ?? string.Empty;
                        continue;

                    default:
                        throw new CuriosityParsingException(
                            contentJson,
                            $"Model returned a JSON root of kind {root.ValueKind}, expected an object.");
                }
            }
        }

        throw new CuriosityParsingException(
            contentJson,
            "Model content exceeded maximum unwrap depth.");
    }

    private static CuriosityPayload Deserialize(JsonElement element, string rawContent)
    {
        var payload = JsonSerializer.Deserialize<CuriosityPayload>(element.GetRawText(), JsonOptions);

        return payload ?? throw new CuriosityParsingException(
            rawContent,
            "Failed to materialize curiosity payload.");
    }

    private static string StripCodeFences(string input)
    {
        if (!input.StartsWith("```", StringComparison.Ordinal))
            return input;

        var firstNewline = input.IndexOf('\n');
        if (firstNewline < 0)
            return input;

        var body = input[(firstNewline + 1)..].TrimEnd();
        if (body.EndsWith("```", StringComparison.Ordinal))
            body = body[..^3];

        return body.Trim();
    }
}

/// <summary>Shape of the curiosity JSON object produced by the text-generation model.</summary>
public sealed class CuriosityPayload
{
    [JsonPropertyName("textContent")]
    public string? TextContent { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("theme")]
    public string? Theme { get; set; }

    [JsonPropertyName("imageFocus")]
    public string? ImageFocus { get; set; }

    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("subcategory")]
    public string? Subcategory { get; set; }
}
