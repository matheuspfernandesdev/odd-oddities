using OddOddities.Domain.Exceptions;
using OddOddities.Infrastructure.Adapters;

namespace OddOddities.UnitTests;

public class CuriosityJsonParserTests
{
    private const string ValidObjectJson =
        """
        {
          "textContent": "The Peregrine Falcon is the fastest animal.",
          "summary": "Peregrine Falcons exceed 389 km/h.",
          "theme": "Avian Speed",
          "sourceUrl": "https://www.nationalgeographic.com/animals/birds/facts/peregrine-falcon",
          "category": "Animals",
          "subcategory": "Birds"
        }
        """;

    private static string WrapInArray(string objectJson) => $"[\n  {objectJson}\n]";

    [Fact]
    public void Parse_ObjectRoot_ReturnsPayload()
    {
        var result = CuriosityJsonParser.Parse(ValidObjectJson);

        Assert.Equal("The Peregrine Falcon is the fastest animal.", result.TextContent);
        Assert.Equal("Peregrine Falcons exceed 389 km/h.", result.Summary);
        Assert.Equal("Avian Speed", result.Theme);
        Assert.Equal("https://www.nationalgeographic.com/animals/birds/facts/peregrine-falcon", result.SourceUrl);
        Assert.Equal("Animals", result.Category);
        Assert.Equal("Birds", result.Subcategory);
    }

    [Fact]
    public void Parse_ArrayRoot_ReturnsFirstElement()
    {
        var result = CuriosityJsonParser.Parse(WrapInArray(ValidObjectJson));

        Assert.Equal("The Peregrine Falcon is the fastest animal.", result.TextContent);
        Assert.Equal("Animals", result.Category);
    }

    [Fact]
    public void Parse_WithMarkdownCodeFence_ReturnsPayload()
    {
        var fenced = $"```json\n{ValidObjectJson}\n```";

        var result = CuriosityJsonParser.Parse(fenced);

        Assert.Equal("The Peregrine Falcon is the fastest animal.", result.TextContent);
    }

    [Fact]
    public void Parse_ArrayRootWithCodeFence_ReturnsPayload()
    {
        var fenced = $"```json\n{WrapInArray(ValidObjectJson)}\n```";

        var result = CuriosityJsonParser.Parse(fenced);

        Assert.Equal("The Peregrine Falcon is the fastest animal.", result.TextContent);
    }

    [Fact]
    public void Parse_DoubleEncodedString_ReturnsPayload()
    {
        var doubleEncoded = System.Text.Json.JsonSerializer.Serialize(ValidObjectJson);

        var result = CuriosityJsonParser.Parse(doubleEncoded);

        Assert.Equal("The Peregrine Falcon is the fastest animal.", result.TextContent);
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        var ex = Assert.Throws<CuriosityParsingException>(() => CuriosityJsonParser.Parse(null));

        Assert.Contains("null or empty", ex.Message);
    }

    [Fact]
    public void Parse_Whitespace_Throws()
    {
        Assert.Throws<CuriosityParsingException>(() => CuriosityJsonParser.Parse("   "));
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        var ex = Assert.Throws<CuriosityParsingException>(() => CuriosityJsonParser.Parse("not json at all"));

        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public void Parse_EmptyArray_Throws()
    {
        var ex = Assert.Throws<CuriosityParsingException>(() => CuriosityJsonParser.Parse("[]"));

        Assert.Contains("empty JSON array", ex.Message);
    }

    [Fact]
    public void Parse_ScalarRoot_Throws()
    {
        var ex = Assert.Throws<CuriosityParsingException>(() => CuriosityJsonParser.Parse("42"));

        Assert.Contains("expected an object", ex.Message);
    }

    [Fact]
    public void Parse_LongContent_TruncatesSnippetInMessage()
    {
        var longInvalid = new string('x', 1000);

        var ex = Assert.Throws<CuriosityParsingException>(() => CuriosityJsonParser.Parse(longInvalid));

        Assert.NotNull(ex.RawContentSnippet);
        Assert.True(ex.RawContentSnippet!.Length <= 303);
        Assert.EndsWith("...", ex.RawContentSnippet);
        Assert.True(ex.Message.Length < 500);
    }

    [Fact]
    public void Parse_InvalidJson_PreservesInnerException()
    {
        var ex = Assert.Throws<CuriosityParsingException>(() => CuriosityJsonParser.Parse("{ invalid"));

        Assert.NotNull(ex.InnerException);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(ex.InnerException);
    }
}
