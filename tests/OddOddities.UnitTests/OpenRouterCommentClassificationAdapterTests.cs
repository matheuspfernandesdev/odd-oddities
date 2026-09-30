using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OddOddities.Domain.Exceptions;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;
using OddOddities.Infrastructure.Adapters;

namespace OddOddities.UnitTests;

/// <summary>
/// RF-20: request shape and response parsing of the comment classification adapter
/// (one chat completion per batch, JSON "results" payload, OpenRouterModelException on
/// API/JSON failures).
/// </summary>
public class OpenRouterCommentClassificationAdapterTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly HttpStatusCode _statusCode;

        public List<string?> RequestBodies { get; } = new();

        public StubHandler(string response, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _response = response;
            _statusCode = statusCode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            };
        }
    }

    private static OpenRouterCommentClassificationAdapter CreateAdapter(StubHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1")
        };

        return new OpenRouterCommentClassificationAdapter(
            httpClient,
            Options.Create(new AppConfiguration()),
            NullLogger<OpenRouterCommentClassificationAdapter>.Instance);
    }

    private static IReadOnlyList<MediaComment> Comments()
        => new[]
        {
            new MediaComment("c_1", "Cover deep sea vents!", DateTime.UtcNow, "fan1"),
            new MediaComment("c_2", "Nice account", DateTime.UtcNow, "fan2")
        };

    private const string ValidResponse = """
    {
      "results": [
        { "commentId": "c_1", "isSuggestion": true, "theme": "Hydrothermal vents", "summary": "Animals living around deep sea vents." },
        { "commentId": "c_2", "isSuggestion": false, "theme": "", "summary": "" }
      ],
      "usage": { "prompt_tokens": 120, "completion_tokens": 40, "cost": 0 }
    }
    """;

    [Fact]
    public async Task ClassifyCommentsAsync_MapsResults_AndSendsSingleJsonRequest()
    {
        var handler = new StubHandler(ValidResponse);
        var adapter = CreateAdapter(handler);

        var results = await adapter.ClassifyCommentsAsync(Comments(), "free/classifier");

        results.Should().HaveCount(2);

        var suggestion = results.Single(r => r.CommentId == "c_1");
        suggestion.IsSuggestion.Should().BeTrue();
        suggestion.Theme.Should().Be("Hydrothermal vents");
        suggestion.Summary.Should().Be("Animals living around deep sea vents.");

        var notSuggestion = results.Single(r => r.CommentId == "c_2");
        notSuggestion.IsSuggestion.Should().BeFalse();
        notSuggestion.Theme.Should().BeEmpty();

        handler.RequestBodies.Should().ContainSingle();
        var body = handler.RequestBodies[0]!;
        body.Should().Contain("free/classifier");
        body.Should().Contain("json_object");
        body.Should().Contain("c_1");
        body.Should().Contain("c_2");
        body.Should().Contain("Cover deep sea vents!");
    }

    [Fact]
    public async Task ClassifyCommentsAsync_HttpError_ThrowsOpenRouterModelException()
    {
        var handler = new StubHandler("{\"error\":\"quota\"}", HttpStatusCode.TooManyRequests);
        var adapter = CreateAdapter(handler);

        var act = async () => await adapter.ClassifyCommentsAsync(Comments(), "free/classifier");

        var ex = await act.Should().ThrowAsync<OpenRouterModelException>();
        ex.Which.StatusCode.Should().Be(429);
        ex.Which.ModelId.Should().Be("free/classifier");
    }

    [Fact]
    public async Task ClassifyCommentsAsync_MalformedJson_ThrowsOpenRouterModelException()
    {
        var handler = new StubHandler("not-json-at-all");
        var adapter = CreateAdapter(handler);

        var act = async () => await adapter.ClassifyCommentsAsync(Comments(), "free/classifier");

        var ex = await act.Should().ThrowAsync<OpenRouterModelException>();
        ex.Which.Message.Should().Contain("not valid JSON");
    }

    [Fact]
    public async Task ClassifyCommentsAsync_EmptyPayload_ThrowsOpenRouterModelException()
    {
        var handler = new StubHandler("""{ "results": [] }""");
        var adapter = CreateAdapter(handler);

        var act = async () => await adapter.ClassifyCommentsAsync(Comments(), "free/classifier");

        var ex = await act.Should().ThrowAsync<OpenRouterModelException>();
        ex.Which.Message.Should().Contain("empty comment classification payload");
    }

    [Fact]
    public async Task ClassifyCommentsAsync_EmptyBatch_ThrowsArgumentException()
    {
        var handler = new StubHandler(ValidResponse);
        var adapter = CreateAdapter(handler);

        var act = async () => await adapter.ClassifyCommentsAsync(
            Array.Empty<MediaComment>(), "free/classifier");

        await act.Should().ThrowAsync<ArgumentException>();
        handler.RequestBodies.Should().BeEmpty();
    }
}
