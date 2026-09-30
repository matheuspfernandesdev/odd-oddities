using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OddOddities.Application.Pipeline;
using OddOddities.Application.Ports;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Interfaces;

namespace OddOddities.UnitTests;

public class PipelineOrchestratorAndRunnerTests
{
    private readonly ICategorySelectionPort _categorySelectionPort = Substitute.For<ICategorySelectionPort>();
    private readonly IPostRepository _postRepository = Substitute.For<IPostRepository>();
    private readonly ILogCorrelationPort _logCorrelation = Substitute.For<ILogCorrelationPort>();

    public PipelineOrchestratorAndRunnerTests()
    {
        _categorySelectionPort.SelectBalancedCategoryAsync(Arg.Any<CancellationToken>())
            .Returns((new Category { Id = 1, Name = "Science" }, new Subcategory { Id = 1, Name = "Physics" }));

        _logCorrelation.PushCorrelation(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>())
            .Returns(Substitute.For<IDisposable>());
    }

    [Fact]
    public async Task Orchestrator_AllStepsSucceed_ReturnsSuccessResult()
    {
        var step1 = Substitute.For<IPipelineStep>();
        step1.StepName.Returns("Step1");
        step1.ExecuteAsync(Arg.Any<PipelineContext>(), Arg.Any<CancellationToken>())
            .Returns(StepResult.Success());

        var step2 = Substitute.For<IPipelineStep>();
        step2.StepName.Returns("Step2");
        step2.ExecuteAsync(Arg.Any<PipelineContext>(), Arg.Any<CancellationToken>())
            .Returns(StepResult.Success());

        var orchestrator = new PipelineOrchestrator(
            new[] { step1, step2 },
            _categorySelectionPort,
            _postRepository,
            _logCorrelation,
            NullLogger<PipelineOrchestrator>.Instance);

        var result = await orchestrator.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        result.ExecutionId.Should().NotBeNullOrEmpty();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task Orchestrator_StepFails_ReturnsFailedResultAndStopsExecution()
    {
        var step1 = Substitute.For<IPipelineStep>();
        step1.StepName.Returns("Step1");
        step1.ExecuteAsync(Arg.Any<PipelineContext>(), Arg.Any<CancellationToken>())
            .Returns(StepResult.Failure(FailureStep.TextGeneration, "Step1 failed", "ERR01"));

        var step2 = Substitute.For<IPipelineStep>();
        step2.StepName.Returns("Step2");

        var orchestrator = new PipelineOrchestrator(
            new[] { step1, step2 },
            _categorySelectionPort,
            _postRepository,
            _logCorrelation,
            NullLogger<PipelineOrchestrator>.Instance);

        var result = await orchestrator.ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("Step1 failed");
        await step2.DidNotReceive().ExecuteAsync(Arg.Any<PipelineContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Orchestrator_StepThrowsException_ReturnsFailedResult()
    {
        var step1 = Substitute.For<IPipelineStep>();
        step1.StepName.Returns("Step1");
        step1.ExecuteAsync(Arg.Any<PipelineContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<StepResult>>(_ => throw new InvalidOperationException("Unexpected step error"));

        var orchestrator = new PipelineOrchestrator(
            new[] { step1 },
            _categorySelectionPort,
            _postRepository,
            _logCorrelation,
            NullLogger<PipelineOrchestrator>.Instance);

        var result = await orchestrator.ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("Unexpected step error");
    }

    [Fact]
    public async Task Runner_WhenOrchestratorSucceeds_ReturnsSuccessStatus()
    {
        var step = Substitute.For<IPipelineStep>();
        step.StepName.Returns("Step1");
        step.ExecuteAsync(Arg.Any<PipelineContext>(), Arg.Any<CancellationToken>())
            .Returns(StepResult.Success());

        var orchestrator = new PipelineOrchestrator(
            new[] { step },
            _categorySelectionPort,
            _postRepository,
            _logCorrelation,
            NullLogger<PipelineOrchestrator>.Instance);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(PipelineOrchestrator)).Returns(orchestrator);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        using var runner = new PipelineRunner(scopeFactory, NullLogger<PipelineRunner>.Instance);

        var result = await runner.RunAsync();

        result.Status.Should().Be(PipelineRunStatus.Success);
        result.ExecutionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Runner_WhenConcurrentExecutionAttempted_ReturnsAlreadyRunning()
    {
        var tcs = new TaskCompletionSource<StepResult>();

        var slowStep = Substitute.For<IPipelineStep>();
        slowStep.StepName.Returns("SlowStep");
        slowStep.ExecuteAsync(Arg.Any<PipelineContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => tcs.Task);

        var orchestrator = new PipelineOrchestrator(
            new[] { slowStep },
            _categorySelectionPort,
            _postRepository,
            _logCorrelation,
            NullLogger<PipelineOrchestrator>.Instance);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(PipelineOrchestrator)).Returns(orchestrator);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        using var runner = new PipelineRunner(scopeFactory, NullLogger<PipelineRunner>.Instance);

        // Start first run asynchronously
        var firstRunTask = runner.RunAsync();

        // Second run attempted immediately
        var secondRunResult = await runner.RunAsync();

        secondRunResult.Status.Should().Be(PipelineRunStatus.AlreadyRunning);

        // Complete the first run
        tcs.SetResult(StepResult.Success());
        var firstRunResult = await firstRunTask;

        firstRunResult.Status.Should().Be(PipelineRunStatus.Success);
    }
}
