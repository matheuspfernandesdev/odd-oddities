using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OddOddities.Infrastructure.Adapters;
using OddOddities.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace OddOddities.UnitTests;

public class OpenRouterModelCatalogAdapterTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;
        public StubHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
    }

    private static OpenRouterModelCatalogAdapter CreateAdapter(string json)
    {
        var httpClient = new HttpClient(new StubHandler(json))
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/")
        };

        return new OpenRouterModelCatalogAdapter(
            httpClient,
            Options.Create(new AppConfiguration()),
            NullLogger<OpenRouterModelCatalogAdapter>.Instance);
    }

    private const string SampleJson = """
    {
      "data": [
        {
          "id": "google/gemma-4-26b-a4b-it:free",
          "name": "Google: Gemma 4 (free)",
          "created": 1775227989,
          "context_length": 262144,
          "architecture": { "output_modalities": ["text"] },
          "pricing": { "prompt": "0", "completion": "0" }
        },
        {
          "id": "cheap/model",
          "name": "Cheap",
          "created": 1700000000,
          "context_length": 128000,
          "architecture": { "output_modalities": ["text"] },
          "pricing": { "prompt": "0.0000001", "completion": "0.0000002" }
        },
        {
          "id": "dynamic/router",
          "name": "Router",
          "created": 1700000001,
          "context_length": 128000,
          "architecture": { "output_modalities": ["text"] },
          "pricing": { "prompt": "-1", "completion": "-1" }
        },
        {
          "id": "google/gemini-3.1-flash-lite-image",
          "name": "Gemini Lite Image",
          "created": 1780000000,
          "context_length": 32768,
          "architecture": { "output_modalities": ["text", "image"] },
          "pricing": { "prompt": "0.00000025", "completion": "0.0000015", "image_output": "0.00003" }
        },
        {
          "id": "~alias/model",
          "name": "Alias",
          "created": 1,
          "context_length": 1,
          "architecture": { "output_modalities": ["text"] },
          "pricing": { "prompt": "0", "completion": "0" }
        }
      ],
      "total_count": 5,
      "links": { "next": null }
    }
    """;

    [Fact]
    public async Task GetTextModels_ParsesPricing_MarksFree_ExcludesAliases_OrdersFreeFirst()
    {
        var adapter = CreateAdapter(SampleJson);

        var models = await adapter.GetTextModelsAsync();

        models.Should().NotContain(m => m.Id.StartsWith('~'));

        var free = models.Single(m => m.Id == "google/gemma-4-26b-a4b-it:free");
        free.IsFree.Should().BeTrue();
        free.PromptPricePerToken.Should().Be(0m);

        var cheap = models.Single(m => m.Id == "cheap/model");
        cheap.IsFree.Should().BeFalse();
        cheap.PromptPricePerToken.Should().Be(0.0000001m);
        cheap.CompletionPricePerToken.Should().Be(0.0000002m);

        var dynamic = models.Single(m => m.Id == "dynamic/router");
        dynamic.PromptPricePerToken.Should().BeNull();

        models.First().Id.Should().Be("google/gemma-4-26b-a4b-it:free");
    }

    [Fact]
    public async Task GetImageModels_ReturnsImageCapableModels_WithImageOutputPrice()
    {
        var adapter = CreateAdapter(SampleJson);

        var models = await adapter.GetImageModelsAsync();

        models.Should().ContainSingle();
        var image = models.Single();
        image.Id.Should().Be("google/gemini-3.1-flash-lite-image");
        image.ImageOutputPrice.Should().Be(0.00003m);
        image.OutputsImage.Should().BeTrue();
    }
}
