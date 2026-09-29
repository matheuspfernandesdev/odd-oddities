using FluentAssertions;
using OddOddities.Application.Pipeline;
using OddOddities.Domain.Enums;

namespace OddOddities.UnitTests;

/// <summary>
/// Covers RF-13 acceptance criterion 1: StepResult.Skipped() yields
/// IsSuccess = true, FailureStep = null and a distinct Skipped outcome.
/// </summary>
public class StepResultTests
{
    [Fact]
    public void Skipped_HasSuccessSemanticsAndSkippedOutcome()
    {
        var result = StepResult.Skipped();

        result.Outcome.Should().Be(StepOutcome.Skipped);
        result.IsSuccess.Should().BeTrue();
        result.FailureStep.Should().BeNull();
        result.FailureReason.Should().BeNull();
        result.ErrorCode.Should().BeNull();
        result.FailureStepName.Should().BeNull();
    }

    [Fact]
    public void Success_HasSuccessOutcome()
    {
        var result = StepResult.Success();

        result.Outcome.Should().Be(StepOutcome.Success);
        result.IsSuccess.Should().BeTrue();
        result.FailureStep.Should().BeNull();
    }

    [Fact]
    public void Failure_HasFailedOutcomeAndIsNotSuccess()
    {
        var result = StepResult.Failure(FailureStep.ImageGeneration, "image failed", "ModelUnavailable");

        result.Outcome.Should().Be(StepOutcome.Failed);
        result.IsSuccess.Should().BeFalse();
        result.FailureStep.Should().Be(FailureStep.ImageGeneration);
        result.FailureReason.Should().Be("image failed");
        result.ErrorCode.Should().Be("ModelUnavailable");
        result.FailureStepName.Should().Be(nameof(FailureStep.ImageGeneration));
    }

    [Fact]
    public void SkippedOutcome_IsDistinctFromSuccessAndFailed()
    {
        StepResult.Skipped().Outcome.Should().NotBe(StepResult.Success().Outcome);
        StepResult.Skipped().Outcome.Should().NotBe(StepResult.Failure(FailureStep.TextGeneration, "x").Outcome);
    }
}
