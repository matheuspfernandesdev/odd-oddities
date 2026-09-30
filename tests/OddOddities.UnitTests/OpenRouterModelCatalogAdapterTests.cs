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

    private const string SampleVideoJson = """
    {
      "data": [
        {
          "id": "google/veo-3.1",
          "name": "Google: Veo 3.1",
          "created": 1719792000,
          "supported_durations": [4, 6, 8],
          "supported_resolutions": ["720p", "1080p"],
          "supported_aspect_ratios": ["16:9", "9:16"],
          "pricing_skus": { "per-video-second": "0.20", "per-video-second-1080p": "0.75" }
        },
        {
          "id": "bytedance/seedance-2.0-mini",
          "name": "ByteDance: Seedance 2.0 Mini",
          "created": 1770000000,
          "supported_durations": [4, 5, 8],
          "supported_resolutions": ["480p"],
          "supported_aspect_ratios": ["9:16", "16:9"],
          "pricing_skus": { "per-video-second": "0.05" }
        },
        {
          "id": "tokens/only-model",
          "name": "Tokens Only",
          "created": 1760000000,
          "supported_durations": [5],
          "supported_aspect_ratios": ["9:16"],
          "pricing_skus": { "per-video-token": "0.000001" }
        },
        {
          "id": "free/video-model",
          "name": "Free Video",
          "created": 1750000000,
          "supported_durations": [5],
          "supported_aspect_ratios": ["9:16"],
          "pricing_skus": { "per-video-second": "0" }
        }
      ]
    }
    """;

    [Fact]
    public async Task GetVideoModels_NormalizesLowestPerSecondSku_AndParsesSupportedParams()
    {
        var adapter = CreateAdapter(SampleVideoJson);

        var models = await adapter.GetVideoModelsAsync();

        var veo = models.Single(m => m.Id == "google/veo-3.1");
        // Lowest "per-video-second*" SKU wins: 0.20, not the 1080p variant 0.75.
        veo.PricePerSecondUsd.Should().Be(0.20m);

        var seedance = models.Single(m => m.Id == "bytedance/seedance-2.0-mini");
        seedance.PricePerSecondUsd.Should().Be(0.05m);
        seedance.SupportedDurations.Should().Equal(4, 5, 8);
        seedance.SupportedAspectRatios.Should().Equal("9:16", "16:9");
        seedance.IsFree.Should().BeFalse();

        models.Single(m => m.Id == "free/video-model").IsFree.Should().BeTrue();
    }

    [Fact]
    public async Task GetVideoModels_ExcludesModelsWithoutPerSecondSku()
    {
        var adapter = CreateAdapter(SampleVideoJson);

        var models = await adapter.GetVideoModelsAsync();

        models.Should().NotContain(m => m.Id == "tokens/only-model");
        models.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetVideoModels_OrdersFreeFirstThenCheapestPerSecond()
    {
        var adapter = CreateAdapter(SampleVideoJson);

        var models = await adapter.GetVideoModelsAsync();

        models.Select(m => m.Id).Should().Equal(
            "free/video-model",
            "bytedance/seedance-2.0-mini",
            "google/veo-3.1");
    }

    /// <summary>Shape of the live /videos/models catalog (verified 2026-09): no model uses the
    /// documented "per-video-second*" keys — per-second prices arrive as "duration_seconds*",
    /// token-billed models use "video_tokens*" and some providers price in cents.</summary>
    private const string SampleLiveVideoJson = """
    {
      "data": [
        {
          "id": "minimax/hailuo-3-max",
          "name": "MiniMax: Hailuo 3 Max",
          "created": 1780000000,
          "supported_durations": [5, 6, 8],
          "supported_aspect_ratios": ["16:9", "9:16"],
          "pricing_skus": { "duration_seconds": "0.08", "duration_seconds_480p": "0.05" }
        },
        {
          "id": "alibaba/wan-2.6",
          "name": "Alibaba: Wan 2.6",
          "created": 1770000000,
          "supported_durations": [5, 10],
          "supported_aspect_ratios": ["16:9", "9:16"],
          "pricing_skus": {
            "text_to_video_duration_seconds_480p": "0.04",
            "image_to_video_duration_seconds_720p": "0.10"
          }
        },
        {
          "id": "bytedance/seedance-2.0-mini",
          "name": "ByteDance: Seedance 2.0 Mini",
          "created": 1775000000,
          "supported_durations": [4, 5, 8],
          "supported_aspect_ratios": ["9:16", "16:9"],
          "pricing_skus": { "video_tokens": "0.0000035", "video_tokens_without_audio": "0.0000035" }
        },
        {
          "id": "runway/gen-4.5",
          "name": "Runway: Gen-4.5",
          "created": 1776000000,
          "supported_durations": [5],
          "supported_aspect_ratios": ["16:9", "9:16"],
          "pricing_skus": { "cents_per_second_output": "12" }
        }
      ]
    }
    """;

    [Fact]
    public async Task GetVideoModels_MatchesLiveDurationSecondsSkuForm_AndExcludesNonUsdPerSecondSkus()
    {
        var adapter = CreateAdapter(SampleLiveVideoJson);

        var models = await adapter.GetVideoModelsAsync();

        // Live per-second USD SKUs are recognized under the "duration_seconds*" naming...
        var hailuo = models.Single(m => m.Id == "minimax/hailuo-3-max");
        hailuo.PricePerSecondUsd.Should().Be(0.05m); // lowest SKU (480p), not 0.08

        var wan = models.Single(m => m.Id == "alibaba/wan-2.6");
        wan.PricePerSecondUsd.Should().Be(0.04m); // text_to_video_duration_seconds_480p

        // ...ordered cheapest per second...
        models.Select(m => m.Id).Should().Equal("alibaba/wan-2.6", "minimax/hailuo-3-max");

        // ...while token-billed (RF-16 AC2) and cents-denominated SKUs stay ineligible.
        models.Should().NotContain(m => m.Id == "bytedance/seedance-2.0-mini");
        models.Should().NotContain(m => m.Id == "runway/gen-4.5");
    }
}
