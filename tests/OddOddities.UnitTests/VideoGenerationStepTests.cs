using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OddOddities.Application.Pipeline;
using OddOddities.Application.Services;
using OddOddities.Application.Steps;
using OddOddities.Domain.Constants;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Exceptions;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.UnitTests;

/// <summary>
/// Covers RF-17: short-circuit outside video runs, budget enforcement, fallback chain
/// failure semantics and the successful path (GenerationAttempt + Post + context.Video).
/// </summary>
public class VideoGenerationStepTests
{
    private readonly IVideoGenerationPort _videoPort = Substitute.For<IVideoGenerationPort>();
    private readonly IObjectStoragePort _objectStorage = Substitute.For<IObjectStoragePort>();
    private readonly IPostRepository _postRepository = Substitute.For<IPostRepository>();
    private readonly IGenerationAttemptRepository _attemptRepository = Substitute.For<IGenerationAttemptRepository>();
    private readonly IModelSelectionService _modelSelection = Substitute.For<IModelSelectionService>();
    private readonly Post _post = new() { Id = 42 };

    private VideoGenerationStep CreateStep()
    {
        _attemptRepository.CreateAsync(Arg.Any<GenerationAttempt>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<GenerationAttempt>());

        _objectStorage.GetBucketUsageBytesAsync(Arg.Any<CancellationToken>()).Returns(0L);

        _postRepository.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_post);

        return new VideoGenerationStep(
            _videoPort,
            _objectStorage,
            _postRepository,
            _attemptRepository,
            _modelSelection,
            Options.Create(new AppConfiguration()),
            NullLogger<VideoGenerationStep>.Instance);
    }

    private static PipelineContext NewContext(bool isVideoRun = true)
    {
        var context = PipelineContext.Create(
            "exec-1",
            new Category { Id = 1, Name = "Science" },
            new Subcategory { Id = 1, Name = "Space" });
        context.Text = new TextContext(42, "text", "summary", "theme", "hash", "https://src", "caption");
        context.IsVideoRun = isVideoRun;
        context.CostCeilingUsd = 0.20m;
        return context;
    }

    private static VideoModelDescriptor Model(string id, decimal? pricePerSecondUsd = 0m)
        => new(id, id, pricePerSecondUsd, new[] { 5 }, new[] { "9:16" }, 0);

    private void SetupChain(params VideoModelDescriptor[] models)
        => _modelSelection.GetVideoChainAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<VideoModelDescriptor>)models);

    [Fact]
    public async Task Execute_NotVideoRun_SkipsWithoutTouchingAnything()
    {
        var step = CreateStep();
        SetupChain(Model("vid-a"));

        var result = await step.ExecuteAsync(NewContext(isVideoRun: false));

        result.Outcome.Should().Be(StepOutcome.Skipped);
        result.IsSuccess.Should().BeTrue();
        result.FailureStep.Should().BeNull();
        await _modelSelection.DidNotReceive().GetVideoChainAsync(Arg.Any<CancellationToken>());
        await _videoPort.DidNotReceive()
            .GenerateVideoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_BudgetExceeded_FailsWithoutCallingPort()
    {
        var step = CreateStep();
        // 0.50 USD/s x 5 s = 2.50 USD estimated per request.
        SetupChain(Model("expensive/video", pricePerSecondUsd: 0.50m));

        var context = NewContext();
        context.CostCeilingUsd = 0.10m;
        context.AccumulatedCostUsd = 0.10m;

        var result = await step.ExecuteAsync(context);

        result.IsSuccess.Should().BeFalse();
        result.FailureStep.Should().Be(FailureStep.VideoGeneration);
        result.ErrorCode.Should().Be("BUDGET_EXCEEDED");
        await _videoPort.DidNotReceive()
            .GenerateVideoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_AllModelsFail_ReturnsVideoGenerationFailure()
    {
        var step = CreateStep();
        SetupChain(Model("vid-a"), Model("vid-b"));

        _videoPort.GenerateVideoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<VideoGenerationResult>>(_ =>
                throw new OpenRouterModelException("(any)", 500, "boom"));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeFalse();
        result.FailureStep.Should().Be(FailureStep.VideoGeneration);
        result.ErrorCode.Should().Be("ALL_MODELS_FAILED");
        await _videoPort.Received(2)
            .GenerateVideoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_EmptyChain_ReturnsNoModelsAvailable()
    {
        var step = CreateStep();
        SetupChain();

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeFalse();
        result.FailureStep.Should().Be(FailureStep.VideoGeneration);
        result.ErrorCode.Should().Be("NO_MODELS_AVAILABLE");
    }

    [Fact]
    public async Task Execute_ModelError_FallsBackToNextModel_AndSucceeds()
    {
        var step = CreateStep();
        SetupChain(Model("vid-a"), Model("vid-b", pricePerSecondUsd: 0.02m));

        _videoPort.GenerateVideoAsync(Arg.Any<string>(), "vid-a", Arg.Any<CancellationToken>())
            .Returns<Task<VideoGenerationResult>>(_ =>
                throw new OpenRouterModelException("vid-a", 400, "bad model"));

        _videoPort.GenerateVideoAsync(Arg.Any<string>(), "vid-b", Arg.Any<CancellationToken>())
            .Returns(new VideoGenerationResult([9, 9, 9], "vid-b", 0.08m, 5));

        var context = NewContext();
        var result = await step.ExecuteAsync(context);

        result.IsSuccess.Should().BeTrue();
        context.AccumulatedCostUsd.Should().Be(0.08m);
        await _attemptRepository.Received().CreateAsync(
            Arg.Is<GenerationAttempt>(a => a.Status == AttemptStatus.Error && a.ModelId == "vid-a"),
            Arg.Any<CancellationToken>());
        await _attemptRepository.Received().CreateAsync(
            Arg.Is<GenerationAttempt>(a => a.Status == AttemptStatus.Success && a.ModelId == "vid-b" && a.PostId == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_Success_RecordsAttemptUpdatesPostAndFillsVideoContext()
    {
        var step = CreateStep();
        SetupChain(Model("vid-a", pricePerSecondUsd: 0.02m));

        _videoPort.GenerateVideoAsync("theme", "vid-a", Arg.Any<CancellationToken>())
            .Returns(new VideoGenerationResult([1, 2, 3, 4, 5], "vid-a", 0.08m, 5));

        var context = NewContext();
        var result = await step.ExecuteAsync(context);

        result.IsSuccess.Should().BeTrue();

        // Budget: real usage cost accumulated on the run.
        context.AccumulatedCostUsd.Should().Be(0.08m);

        // context.Video filled (RF-15 record shape).
        context.Video.Should().NotBeNull();
        context.Video!.Bytes.Should().Be(5);
        context.Video.DurationSeconds.Should().Be(5);
        context.Video.CostUsd.Should().Be(0.08m);
        context.Video.ModelId.Should().Be("vid-a");

        // Post updated with video metadata and the reused ImageProcessed status.
        _post.VideoObjectKey.Should().Be(context.Video.ObjectKey);
        _post.VideoBytes.Should().Be(5);
        _post.VideoDurationSeconds.Should().Be(5);
        _post.Status.Should().Be(PostStatus.ImageProcessed);

        // ...and the update is actually persisted (not just mutated in memory).
        await _postRepository.Received(1).UpdateAsync(_post, Arg.Any<CancellationToken>());

        // MinIO upload with video/mp4.
        await _objectStorage.Received(1).PutObjectAsync(
            context.Video.ObjectKey,
            Arg.Is<byte[]>(b => b.Length == 5),
            "video/mp4",
            Arg.Any<CancellationToken>());

        // GenerationAttempt recorded per attempt.
        await _attemptRepository.Received(1).CreateAsync(
            Arg.Is<GenerationAttempt>(a =>
                a.PostId == 42
                && a.AttemptNumber == 1
                && a.ModelId == "vid-a"
                && a.Status == AttemptStatus.Success
                && a.CostUsd == 0.08m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_QuotaExceeded_FailsWithVideoStorage()
    {
        var step = CreateStep();
        SetupChain(Model("vid-a"));

        _videoPort.GenerateVideoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new VideoGenerationResult([1, 2, 3], "vid-a", 0.01m, 5));

        _objectStorage.GetBucketUsageBytesAsync(Arg.Any<CancellationToken>())
            .Returns(StorageConstants.MinioDefaultQuotaBytes);

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeFalse();
        result.FailureStep.Should().Be(FailureStep.VideoStorage);
        result.ErrorCode.Should().Be("QUOTA_EXCEEDED");
        await _objectStorage.DidNotReceive()
            .PutObjectAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
