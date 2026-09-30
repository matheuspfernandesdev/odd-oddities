using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OddOddities.Application.Pipeline;
using OddOddities.Application.Services;
using OddOddities.Application.Steps;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Exceptions;
using OddOddities.Domain.Interfaces;

namespace OddOddities.UnitTests;

public class ImageGenerationStepTests
{
    private readonly IImageGenerationPort _imagePort = Substitute.For<IImageGenerationPort>();
    private readonly IImageProcessingPort _imageProcessing = Substitute.For<IImageProcessingPort>();
    private readonly IObjectStoragePort _objectStorage = Substitute.For<IObjectStoragePort>();
    private readonly IPostRepository _postRepository = Substitute.For<IPostRepository>();
    private readonly IGenerationAttemptRepository _attemptRepository = Substitute.For<IGenerationAttemptRepository>();
    private readonly IModelSelectionService _modelSelection = Substitute.For<IModelSelectionService>();

    private ImageGenerationStep CreateStep()
    {
        _attemptRepository.CreateAsync(Arg.Any<GenerationAttempt>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<GenerationAttempt>());

        _imageProcessing.ProcessImageAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new ImageProcessingResult
            {
                ImageData = [1, 2, 3],
                Width = 1080,
                Height = 1080,
                Format = "jpeg"
            });

        _objectStorage.GetBucketUsageBytesAsync(Arg.Any<CancellationToken>()).Returns(0L);

        _postRepository.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new Post { Id = 42 });

        return new ImageGenerationStep(
            _imagePort,
            _imageProcessing,
            _objectStorage,
            _postRepository,
            _attemptRepository,
            _modelSelection,
            NullLogger<ImageGenerationStep>.Instance);
    }

    private static PipelineContext NewContext()
    {
        var context = PipelineContext.Create(
            "exec-1",
            new Category { Id = 1, Name = "Science" },
            new Subcategory { Id = 1, Name = "Space" });
        context.Text = new TextContext(42, "text", "summary", "theme", "hash", "https://src", "caption");
        return context;
    }

    private static ModelDescriptor Model(string id, bool isFree = true, decimal? imageOutput = null)
        => new(id, id, null, null, imageOutput, isFree, null, 0);

    private void SetupChain(params ModelDescriptor[] models)
        => _modelSelection.GetImageChainAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ModelDescriptor>)models);

    [Fact]
    public async Task Execute_ModelError_FallsBackToNextModel_AndSucceeds()
    {
        var step = CreateStep();
        SetupChain(Model("img-a"), Model("img-b"));

        _imagePort.GenerateImageAsync(Arg.Any<string>(), "img-a", Arg.Any<CancellationToken>())
            .Returns<Task<ImageGenerationResult>>(_ =>
                throw new OpenRouterModelException("img-a", 400, "bad model"));

        _imagePort.GenerateImageAsync(Arg.Any<string>(), "img-b", Arg.Any<CancellationToken>())
            .Returns(new ImageGenerationResult([9, 9], "img-b", 0.00003m, 25));

        var context = NewContext();
        var result = await step.ExecuteAsync(context);

        result.IsSuccess.Should().BeTrue();
        context.AccumulatedCostUsd.Should().Be(0.00003m);
        await _attemptRepository.Received().CreateAsync(
            Arg.Is<GenerationAttempt>(a => a.Status == AttemptStatus.Error && a.ModelId == "img-a"),
            Arg.Any<CancellationToken>());
        await _attemptRepository.Received().CreateAsync(
            Arg.Is<GenerationAttempt>(a => a.Status == AttemptStatus.Success && a.ModelId == "img-b" && a.PostId == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_AllModelsFail_ReturnsAllModelsFailed()
    {
        var step = CreateStep();
        SetupChain(Model("img-a"), Model("img-b"));

        _imagePort.GenerateImageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<ImageGenerationResult>>(_ =>
                throw new OpenRouterModelException("(any)", 500, "boom"));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ALL_MODELS_FAILED");
        await _imagePort.Received(2).GenerateImageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_BudgetExceeded_FailsWithoutCallingPort()
    {
        var step = CreateStep();
        SetupChain(Model("expensive/img", isFree: false, imageOutput: 0.5m));

        var context = NewContext();
        context.CostCeilingUsd = 0.01m;
        context.AccumulatedCostUsd = 0.01m;

        var result = await step.ExecuteAsync(context);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("BUDGET_EXCEEDED");
        await _imagePort.DidNotReceive().GenerateImageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_EmptyChain_ReturnsNoModelsAvailable()
    {
        var step = CreateStep();
        SetupChain();

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NO_MODELS_AVAILABLE");
    }

    [Fact]
    public async Task Execute_ModelErrorWithLongMessage_TruncatesRejectionReasonTo255()
    {
        var step = CreateStep();
        SetupChain(Model("img-a"));

        var longMessage = new string('e', 500);
        _imagePort.GenerateImageAsync(Arg.Any<string>(), "img-a", Arg.Any<CancellationToken>())
            .Returns<Task<ImageGenerationResult>>(_ =>
                throw new OpenRouterModelException("img-a", 400, longMessage));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ALL_MODELS_FAILED");
        await _attemptRepository.Received().CreateAsync(
            Arg.Is<GenerationAttempt>(a =>
                a.Status == AttemptStatus.Error
                && a.RejectionReason != null
                && a.RejectionReason.Length <= 255),
            Arg.Any<CancellationToken>());
    }
}
