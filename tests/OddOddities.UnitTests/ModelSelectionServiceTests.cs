using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OddOddities.Application.Services;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.UnitTests;

public class ModelSelectionServiceTests
{
    private readonly IModelCatalogPort _catalog = Substitute.For<IModelCatalogPort>();
    private readonly AppConfiguration _config = new();

    private ModelSelectionService CreateService()
        => new(_catalog, Options.Create(_config), NullLogger<ModelSelectionService>.Instance);

    private static ModelDescriptor Text(
        string id,
        decimal? prompt = null,
        decimal? completion = null,
        int contextLength = 262_144,
        bool isFree = false)
        => new(id, id, prompt, completion, null, isFree, contextLength, 0);

    private static ModelDescriptor Image(string id, decimal? imageOutput, bool isFree = false)
        => new(id, id, null, null, imageOutput, isFree, null, 0);

    [Fact]
    public async Task GetTextChain_PreferredFirst_ThenCatalogCandidates_RespectingMax()
    {
        _config.OpenRouter.TextModelId = "preferred/text";
        _config.ModelSelection.MaxTextModelAttempts = 3;

        _catalog.GetTextModelsAsync(Arg.Any<CancellationToken>()).Returns(new List<ModelDescriptor>
        {
            Text("free/a", 0, 0, isFree: true),
            Text("cheap/b", 0.0000001m, 0.0000002m),
            Text("mid/c", 0.0000005m, 0.0000005m),
            Text("expensive/d", 0.01m, 0.01m)
        });

        var chain = await CreateService().GetTextChainAsync();

        chain.Select(m => m.Id).Should().Equal("preferred/text", "free/a", "cheap/b");
    }

    [Fact]
    public async Task GetTextChain_CatalogFailure_FallsBackToPreferredOnly()
    {
        _config.OpenRouter.TextModelId = "preferred/text";

        _catalog.GetTextModelsAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<ModelDescriptor>>>(_ =>
                throw new HttpRequestException("catalog down"));

        var chain = await CreateService().GetTextChainAsync();

        chain.Should().ContainSingle().Which.Id.Should().Be("preferred/text");
    }

    [Fact]
    public async Task GetTextChain_FiltersByMinContextLengthAndPerRequestCost()
    {
        _config.OpenRouter.TextModelId = "";
        _config.ModelSelection.MinContextLength = 8_000;
        _config.ModelSelection.MaxTextCostPerRequestUsd = 0.01m;

        _catalog.GetTextModelsAsync(Arg.Any<CancellationToken>()).Returns(new List<ModelDescriptor>
        {
            Text("ok/free", 0, 0, contextLength: 32_000, isFree: true),
            Text("small/ctx", 0, 0, contextLength: 1_000, isFree: true),
            // estimate: 0.00001*1500 + 0.00001*600 = 0.021 > 0.01 → excluded
            Text("pricy/x", 0.00001m, 0.00001m, contextLength: 128_000),
            // estimate: 0.000001*1500 + 0.000001*600 = 0.0021 <= 0.01 → included
            Text("cheap/y", 0.000001m, 0.000001m, contextLength: 128_000),
            Text("unknown/z", null, null, contextLength: 128_000)
        });

        var chain = await CreateService().GetTextChainAsync();

        chain.Select(m => m.Id).Should().Equal("ok/free", "cheap/y");
    }

    [Fact]
    public async Task GetImageChain_PreferredMissingFromCatalog_GoesFirst()
    {
        _config.OpenRouter.ImageModelId = "gone/invalid-model";
        _config.ModelSelection.MaxImageModelAttempts = 2;

        _catalog.GetImageModelsAsync(Arg.Any<CancellationToken>()).Returns(new List<ModelDescriptor>
        {
            Image("cheap/img", 0.00003m),
            Image("mid/img", 0.00006m),
            Image("expensive/img", 1m)
        });

        var chain = await CreateService().GetImageChainAsync();

        chain.Select(m => m.Id).Should().Equal("gone/invalid-model", "cheap/img");
    }

    [Fact]
    public async Task GetImageChain_ExcludesCandidatesAboveCostCap()
    {
        _config.OpenRouter.ImageModelId = "";
        _config.ModelSelection.MaxImageCostPerRequestUsd = 0.05m;

        _catalog.GetImageModelsAsync(Arg.Any<CancellationToken>()).Returns(new List<ModelDescriptor>
        {
            Image("within/cap", 0.03m),
            Image("over/cap", 0.5m),
            Image("free/img", 0, isFree: true)
        });

        var chain = await CreateService().GetImageChainAsync();

        chain.Select(m => m.Id).Should().Equal("within/cap");
    }

    [Fact]
    public async Task GetImageChain_ExcludesFreeModels()
    {
        _config.OpenRouter.ImageModelId = "preferred/img";

        _catalog.GetImageModelsAsync(Arg.Any<CancellationToken>()).Returns(new List<ModelDescriptor>
        {
            Image("free/a", 0, isFree: true),
            Image("paid/b", 0.00003m),
            Image("paid/c", 0.00006m)
        });

        var chain = await CreateService().GetImageChainAsync();

        chain.Select(m => m.Id).Should().Equal("preferred/img", "paid/b", "paid/c");
    }

    [Fact]
    public async Task GetImageChain_PreferredPlusFiveCheapestPaid_RespectsMaxSix()
    {
        _config.OpenRouter.ImageModelId = "openai/gpt-image-2";
        _config.ModelSelection.MaxImageModelAttempts = 6;

        _catalog.GetImageModelsAsync(Arg.Any<CancellationToken>()).Returns(new List<ModelDescriptor>
        {
            Image("free/a", 0, isFree: true),
            Image("paid/1", 0.000001m),
            Image("paid/2", 0.000002m),
            Image("paid/3", 0.000003m),
            Image("paid/4", 0.000004m),
            Image("paid/5", 0.000005m),
            Image("paid/6", 0.000006m)
        });

        var chain = await CreateService().GetImageChainAsync();

        chain.Select(m => m.Id).Should().Equal(
            "openai/gpt-image-2",
            "paid/1",
            "paid/2",
            "paid/3",
            "paid/4",
            "paid/5");
    }
}
