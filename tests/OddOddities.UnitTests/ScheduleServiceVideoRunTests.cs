using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OddOddities.Application.UseCases;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.UnitTests;

/// <summary>
/// Covers RF-15: ISchedulerPort.IsVideoRunToday() drives the "one video every N days"
/// cadence — never published a video => video run; inside the interval => image run;
/// interval elapsed => video run; failed lookup => image run (current behavior).
/// </summary>
public class ScheduleServiceVideoRunTests
{
    private readonly IPostRepository _postRepository = Substitute.For<IPostRepository>();

    private ScheduleService CreateService(Action<AppConfiguration>? configure = null)
    {
        var config = new AppConfiguration();
        configure?.Invoke(config);

        // ScheduleService is a singleton and resolves the scoped repository through
        // IServiceScopeFactory — mirror that wiring here.
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IPostRepository)).Returns(_postRepository);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return new ScheduleService(
            Options.Create(config),
            scopeFactory,
            NullLogger<ScheduleService>.Instance);
    }

    private void SetupLatestVideoPublishedAt(DateTime? publishedAt)
        => _postRepository.GetLatestVideoPublishedAtAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(publishedAt));

    private void SetupFailingVideoLookup()
        => _postRepository.GetLatestVideoPublishedAtAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DateTime?>(new InvalidOperationException("database unavailable")));

    [Fact]
    public void IsVideoRunToday_WhenNoVideoWasEverPublished_ReturnsTrue()
    {
        SetupLatestVideoPublishedAt(null);
        var sut = CreateService();

        sut.IsVideoRunToday().Should().BeTrue();
    }

    [Fact]
    public void IsVideoRunToday_WhenLastVideoIsInsideTheInterval_ReturnsFalse()
    {
        SetupLatestVideoPublishedAt(DateTime.UtcNow.AddDays(-14));
        var sut = CreateService(config => config.Video.IntervalDays = 15);

        sut.IsVideoRunToday().Should().BeFalse();
    }

    [Fact]
    public void IsVideoRunToday_WhenIntervalIsCompleted_ReturnsTrue()
    {
        SetupLatestVideoPublishedAt(DateTime.UtcNow.AddDays(-15));
        var sut = CreateService(config => config.Video.IntervalDays = 15);

        sut.IsVideoRunToday().Should().BeTrue();
    }

    [Fact]
    public void IsVideoRunToday_WhenLookupFails_ReturnsFalse()
    {
        SetupFailingVideoLookup();
        var sut = CreateService();

        sut.IsVideoRunToday().Should().BeFalse();
    }

    [Fact]
    public void VideoConfiguration_DefaultsMatchThePlannedCadence()
    {
        var video = new AppConfiguration().Video;

        video.IntervalDays.Should().Be(15);
        video.DurationSeconds.Should().Be(5);
        video.Resolution.Should().Be("480p");
        video.AspectRatio.Should().Be("9:16");
        video.GenerateAudio.Should().BeTrue();
    }

    [Fact]
    public void ModelSelectionConfiguration_VideoBudgetDefaultsMatchThePlannedCeilings()
    {
        var modelSelection = new AppConfiguration().ModelSelection;

        modelSelection.MaxVideoModelAttempts.Should().Be(3);
        modelSelection.MaxVideoCostPerRequestUsd.Should().Be(0.15m);
        modelSelection.MaxCostPerVideoRunUsd.Should().Be(0.20m);
        modelSelection.MaxCostPerRunUsd.Should().Be(0.05m, "the image-run ceiling stays untouched by RF-15");
    }
}
