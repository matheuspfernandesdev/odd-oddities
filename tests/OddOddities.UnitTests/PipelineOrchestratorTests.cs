using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OddOddities.Application.Pipeline;
using OddOddities.Application.Ports;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Interfaces;

namespace OddOddities.UnitTests;

/// <summary>
/// Covers RF-13: skipped steps must not fail the pipeline, must be logged with
/// outcome "Skipped" (distinct from "Success") and must never mark the Post as Failed.
/// </summary>
public class PipelineOrchestratorTests
{
    private readonly ICategorySelectionPort _categorySelection = Substitute.For<ICategorySelectionPort>();
    private readonly IPostRepository _postRepository = Substitute.For<IPostRepository>();
    private readonly ILogCorrelationPort _logCorrelation = Substitute.For<ILogCorrelationPort>();

    private PipelineOrchestrator CreateOrchestrator(params IPipelineStep[] steps)
    {
        _categorySelection.SelectBalancedCategoryAsync(Arg.Any<CancellationToken>())
            .Returns((
                new Category { Id = 1, Name = "Science" },
                new Subcategory { Id = 1, Name = "Space" }));

        return new PipelineOrchestrator(
            steps,
            _categorySelection,
            _postRepository,
            _logCorrelation,
            NullLogger<PipelineOrchestrator>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStepReturnsSkipped_LogsSkippedOutcomeAndContinuesToNextStep()
    {
        var skipped = new RecordingStep("VideoGeneration", _ => StepResult.Skipped());
        var after = new RecordingStep("Publication", _ => StepResult.Success());
        var sut = CreateOrchestrator(skipped, after);

        await sut.ExecuteAsync();

        skipped.Executions.Should().Be(1);
        after.Executions.Should().Be(1, "the orchestrator must continue to the next step after a skip");

        _logCorrelation.Received(1)
            .PushCorrelation(Arg.Any<string>(), "VideoGeneration", "Skipped", Arg.Any<long>());
        _logCorrelation.DidNotReceive()
            .PushCorrelation(Arg.Any<string>(), "VideoGeneration", "Success", Arg.Any<long>());
        _logCorrelation.DidNotReceive()
            .PushCorrelation(Arg.Any<string>(), "VideoGeneration", "Failed", Arg.Any<long>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenStepIsSkipped_DoesNotMarkPostAsFailed()
    {
        var post = new Post { Id = 42, Status = PostStatus.Generated };
        _postRepository.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(post);

        // The skipped step simulates a real run: it fills the PostId so that a wrongly
        // triggered "mark as Failed" would actually reach the repository.
        var skipped = new RecordingStep("VideoGeneration", context =>
        {
            context.Text = new TextContext(42, "text", "summary", "theme", "hash", "url", "caption");
            return StepResult.Skipped();
        });
        var after = new RecordingStep("Publication", _ => StepResult.Success());
        var sut = CreateOrchestrator(skipped, after);

        await sut.ExecuteAsync();

        await _postRepository.DidNotReceive()
            .GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _postRepository.DidNotReceive()
            .UpdateAsync(Arg.Any<Post>(), Arg.Any<CancellationToken>());
        post.Status.Should().NotBe(PostStatus.Failed);
        post.FailureStep.Should().BeNull();
        after.Executions.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStepFails_StillMarksPostAsFailedAndStopsPipeline()
    {
        var post = new Post { Id = 42, Status = PostStatus.Generated };
        _postRepository.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(post);

        var failing = new RecordingStep("TextGeneration", context =>
        {
            context.Text = new TextContext(42, "text", "summary", "theme", "hash", "url", "caption");
            return StepResult.Failure(FailureStep.TextGeneration, "boom");
        });
        var after = new RecordingStep("Publication", _ => StepResult.Success());
        var sut = CreateOrchestrator(failing, after);

        await sut.ExecuteAsync();

        await _postRepository.Received(1)
            .UpdateAsync(Arg.Is<Post>(p => p.Status == PostStatus.Failed), Arg.Any<CancellationToken>());
        after.Executions.Should().Be(0, "a failed step must stop the pipeline");
    }

    /// <summary>
    /// Minimal pipeline step that records how many times it ran.
    /// </summary>
    private sealed class RecordingStep : IPipelineStep
    {
        private readonly Func<PipelineContext, StepResult> _handler;

        public RecordingStep(string stepName, Func<PipelineContext, StepResult> handler)
        {
            StepName = stepName;
            _handler = handler;
        }

        public string StepName { get; }

        public int Executions { get; private set; }

        public Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
        {
            Executions++;
            return Task.FromResult(_handler(context));
        }
    }
}
