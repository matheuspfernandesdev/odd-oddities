using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OddOddities.Domain.ValueObjects;
using OddOddities.Infrastructure.Adapters;

namespace OddOddities.UnitTests;

public class OpenRouterTextGenerationAdapterTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _json;

        public CapturingHandler(string json) => _json = json;

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            };
        }
    }

    private const string ResponseJson = """
    {
      "choices": [
        {
          "message": {
            "content": "{\"textContent\":\"A curiosity.\",\"summary\":\"Summary.\",\"theme\":\"theme\",\"sourceUrl\":\"https://example.com\",\"category\":\"Science\",\"subcategory\":\"Space\"}"
          }
        }
      ],
      "usage": { "prompt_tokens": 100, "completion_tokens": 50, "total_tokens": 150, "cost": 0 }
    }
    """;

    private static (OpenRouterTextGenerationAdapter Adapter, CapturingHandler Handler) CreateAdapter(
        AppConfiguration? config = null)
    {
        var handler = new CapturingHandler(ResponseJson);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/")
        };

        var adapter = new OpenRouterTextGenerationAdapter(
            httpClient,
            Options.Create(config ?? new AppConfiguration()),
            NullLogger<OpenRouterTextGenerationAdapter>.Instance);

        return (adapter, handler);
    }

    [Fact]
    public async Task GenerateCuriosity_RequestDeclaresCaptionLengthRangeFromConfig()
    {
        var (adapter, handler) = CreateAdapter();

        await adapter.GenerateCuriosityAsync("Science", "Space", "test/model");

        handler.RequestBodies.Should().ContainSingle();
        handler.RequestBodies[0].Should().Contain("between 800 and 1600 characters");
    }

    [Fact]
    public async Task GenerateCuriosity_CustomMaxCaptionContentLength_InterpolatesIntoRange()
    {
        var config = new AppConfiguration { MaxCaptionContentLength = 1200 };
        var (adapter, handler) = CreateAdapter(config);

        await adapter.GenerateCuriosityAsync("Science", "Space", "test/model");

        handler.RequestBodies.Should().ContainSingle();
        handler.RequestBodies[0].Should().Contain("between 600 and 1200 characters");
    }

    [Fact]
    public async Task GenerateCuriosity_RequestContainsEditorialBriefMarkers()
    {
        var (adapter, handler) = CreateAdapter();

        await adapter.GenerateCuriosityAsync("Science", "Space", "test/model");

        handler.RequestBodies.Should().ContainSingle();
        var body = handler.RequestBodies[0];
        body.Should().Contain("Editorial profile");
        body.Should().Contain("mysterious");
        body.Should().Contain("Antikythera");
        body.Should().Contain("gore");
        body.Should().Contain("disgusting");
        body.Should().Contain("Choose the angle that best fits the editorial profile");
    }

    [Fact]
    public async Task GenerateCuriosity_RequestDeclaresImageFocusField()
    {
        var (adapter, handler) = CreateAdapter();

        await adapter.GenerateCuriosityAsync("Science", "Space", "test/model");

        handler.RequestBodies.Should().ContainSingle();
        var body = handler.RequestBodies[0];
        body.Should().Contain("imageFocus (the concrete visual subject at the heart of the curiosity, max 300 characters)");
        body.Should().Contain("not a vague mood");
    }
}
