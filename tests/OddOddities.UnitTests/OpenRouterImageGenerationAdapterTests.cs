using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OddOddities.Domain.ValueObjects;
using OddOddities.Infrastructure.Adapters;

namespace OddOddities.UnitTests;

public class OpenRouterImageGenerationAdapterTests
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
      "data": [ { "b64_json": "AQID" } ],
      "usage": { "cost": 0.00003 }
    }
    """;

    private static (OpenRouterImageGenerationAdapter Adapter, CapturingHandler Handler) CreateAdapter()
    {
        var handler = new CapturingHandler(ResponseJson);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/")
        };

        var adapter = new OpenRouterImageGenerationAdapter(
            httpClient,
            Options.Create(new AppConfiguration()),
            NullLogger<OpenRouterImageGenerationAdapter>.Instance);

        return (adapter, handler);
    }

    [Fact]
    public async Task GenerateImage_PromptMakesFocusTheFocalPointWithSupportingSetting()
    {
        var (adapter, handler) = CreateAdapter();

        await adapter.GenerateImageAsync(
            "a corroded ancient Greek bronze device with interlocking gears",
            "test/model");

        handler.RequestBodies.Should().ContainSingle();
        var body = handler.RequestBodies[0];

        // The curiosity remains the clear focal point of the image.
        body.Should().Contain(
            "clear, unmistakable focal point is: a corroded ancient Greek bronze device with interlocking gears");

        // ...but the rest of the image gets a coherent setting and supporting detail.
        body.Should().Contain("rich, coherent setting and supporting visual elements");
        body.Should().Contain("without competing with or obscuring the focal point");

        body.Should().Contain("Artistic, dreamlike quality, suitable for Instagram");
        body.Should().Contain("No text, letters, numbers or watermarks");
    }
}
