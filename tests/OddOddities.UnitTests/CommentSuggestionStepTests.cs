using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OddOddities.Application.DependencyInjection;
using OddOddities.Application.Pipeline;
using OddOddities.Application.Services;
using OddOddities.Application.Steps;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Exceptions;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.UnitTests;

/// <summary>
/// Covers RF-20: short-circuits (flag off, video run, no published posts, no new
/// comments), the single classification call, Accepted/NotSuggestion persistence, the
/// one-suggestion-per-run limit, the Meta permission fallback (R14) and the DI order
/// (CommentSuggestionStep registered before TextGenerationStep).
/// </summary>
public class CommentSuggestionStepTests
{
    private readonly IPostRepository _postRepository = Substitute.For<IPostRepository>();
    private readonly IMediaCommentPort _mediaCommentPort = Substitute.For<IMediaCommentPort>();
    private readonly ICommentSuggestionRepository _commentSuggestionRepository = Substitute.For<ICommentSuggestionRepository>();
    private readonly ICommentClassificationPort _classificationPort = Substitute.For<ICommentClassificationPort>();
    private readonly IModelSelectionService _modelSelection = Substitute.For<IModelSelectionService>();
    private readonly AppConfiguration _config = new();
    private readonly CapturingLogger _logger = new();

    private CommentSuggestionStep CreateStep(bool commentsEnabled = true)
    {
        _config.Comments.Enabled = commentsEnabled;

        _postRepository.GetLatestPublishedMediaIdsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)new[] { "media-1" });

        _commentSuggestionRepository.ExistsByCommentIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        _commentSuggestionRepository.CreateAsync(Arg.Any<CommentSuggestion>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<CommentSuggestion>());

        _modelSelection.GetTextChainAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ModelDescriptor>)new[] { TextModel("free/classifier") });

        return new CommentSuggestionStep(
            _postRepository,
            _mediaCommentPort,
            _commentSuggestionRepository,
            _classificationPort,
            _modelSelection,
            Options.Create(_config),
            _logger);
    }

    private static PipelineContext NewContext(bool isVideoRun = false)
    {
        var context = PipelineContext.Create(
            "exec-1",
            new Category { Id = 1, Name = "Science" },
            new Subcategory { Id = 1, Name = "Space" });
        context.IsVideoRun = isVideoRun;
        return context;
    }

    private static ModelDescriptor TextModel(string id)
        => new(id, id, 0m, 0m, null, IsFree: true, 128_000, 0);

    private static MediaComment Comment(
        string id,
        string text = "You should cover deep sea vents!",
        string author = "curious_fan")
        => new(id, text, new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc), author);

    private static CommentClassificationResult Classified(
        string id,
        bool isSuggestion,
        string theme = "",
        string summary = "")
        => new(id, isSuggestion, theme, summary);

    private void SetupComments(params MediaComment[] comments)
        => _mediaCommentPort.GetCommentsAsync("media-1", Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MediaComment>)comments);

    private void SetupClassification(params CommentClassificationResult[] results)
        => _classificationPort.ClassifyCommentsAsync(
                Arg.Any<IReadOnlyList<MediaComment>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<CommentClassificationResult>)results);

    private bool HasWarning(string text)
        => _logger.Entries.Any(entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains(text, StringComparison.Ordinal));

    [Fact]
    public void ApplicationServices_RegistersCommentSuggestionStepFirst()
    {
        var services = new ServiceCollection();

        services.AddApplicationServices();

        var stepTypes = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineStep))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();

        stepTypes.Should().Equal(
            typeof(CommentSuggestionStep),
            typeof(TextGenerationStep),
            typeof(ImageGenerationStep),
            typeof(VideoGenerationStep),
            typeof(PublicationStep));
    }

    [Fact]
    public async Task Execute_FlagOff_SkipsWithoutTouchingAnything()
    {
        var step = CreateStep(commentsEnabled: false);

        var result = await step.ExecuteAsync(NewContext());

        result.Outcome.Should().Be(StepOutcome.Skipped);
        result.IsSuccess.Should().BeTrue();
        result.FailureStep.Should().BeNull();
        await _postRepository.DidNotReceive()
            .GetLatestPublishedMediaIdsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _mediaCommentPort.DidNotReceive()
            .GetCommentsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_VideoRun_SkipsWithoutTouchingAnything()
    {
        var step = CreateStep();

        var result = await step.ExecuteAsync(NewContext(isVideoRun: true));

        result.Outcome.Should().Be(StepOutcome.Skipped);
        await _postRepository.DidNotReceive()
            .GetLatestPublishedMediaIdsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _classificationPort.DidNotReceive()
            .ClassifyCommentsAsync(Arg.Any<IReadOnlyList<MediaComment>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_NoPublishedPostsWithMediaId_Skips()
    {
        var step = CreateStep();
        _postRepository.GetLatestPublishedMediaIdsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)Array.Empty<string>());

        var result = await step.ExecuteAsync(NewContext());

        result.Outcome.Should().Be(StepOutcome.Skipped);
        await _mediaCommentPort.DidNotReceive()
            .GetCommentsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_NoNewComments_SkipsWithoutClassifying()
    {
        var step = CreateStep();
        SetupComments(Comment("c1"));
        _commentSuggestionRepository.ExistsByCommentIdAsync("c1", Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await step.ExecuteAsync(NewContext());

        result.Outcome.Should().Be(StepOutcome.Skipped);
        await _commentSuggestionRepository.Received(1)
            .ExistsByCommentIdAsync("c1", Arg.Any<CancellationToken>());
        await _classificationPort.DidNotReceive()
            .ClassifyCommentsAsync(Arg.Any<IReadOnlyList<MediaComment>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _commentSuggestionRepository.DidNotReceive()
            .CreateAsync(Arg.Any<CommentSuggestion>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ClassifiedAsNotSuggestion_PersistsNotSuggestionAndSucceeds()
    {
        var step = CreateStep();
        SetupComments(Comment("c1", text: "Nice account!"));
        SetupClassification(Classified("c1", isSuggestion: false));

        var context = NewContext();
        var result = await step.ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Success);
        context.Suggestion.Should().BeNull();

        await _classificationPort.Received(1).ClassifyCommentsAsync(
            Arg.Is<IReadOnlyList<MediaComment>>(comments => comments.Count == 1 && comments[0].CommentId == "c1"),
            "free/classifier",
            Arg.Any<CancellationToken>());

        await _commentSuggestionRepository.Received(1).CreateAsync(
            Arg.Is<CommentSuggestion>(s =>
                s.CommentId == "c1"
                && s.MediaId == "media-1"
                && s.AuthorUsername == "curious_fan"
                && s.Classification == CommentClassification.NotSuggestion
                && s.RejectionReason == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_SuggestionAccepted_FillsSuggestionContextAndPersistsAccepted()
    {
        var step = CreateStep();
        SetupComments(Comment("c1", text: "Please cover hydrothermal vents", author: "ocean_fan"));
        SetupClassification(Classified(
            "c1",
            isSuggestion: true,
            theme: "Hydrothermal vents",
            summary: "How animals live around superheated deep sea vents."));

        var context = NewContext();
        var result = await step.ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Success);

        context.Suggestion.Should().NotBeNull();
        context.Suggestion!.Theme.Should().Be("Hydrothermal vents");
        context.Suggestion.Summary.Should().Be("How animals live around superheated deep sea vents.");
        context.Suggestion.AuthorUsername.Should().Be("ocean_fan");
        context.Suggestion.CommentId.Should().Be("c1");
        context.Suggestion.SourceCommentText.Should().Be("Please cover hydrothermal vents");

        await _commentSuggestionRepository.Received(1).CreateAsync(
            Arg.Is<CommentSuggestion>(s =>
                s.CommentId == "c1"
                && s.MediaId == "media-1"
                && s.CommentText == "Please cover hydrothermal vents"
                && s.AuthorUsername == "ocean_fan"
                && s.Classification == CommentClassification.Accepted
                && s.RejectionReason == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_TwoSuggestions_AcceptsOnlyFirst_AndPersistsLimitReasonOnSecond()
    {
        var step = CreateStep();
        SetupComments(Comment("c1"), Comment("c2"));
        SetupClassification(
            Classified("c1", isSuggestion: true, theme: "Theme one", summary: "Summary one"),
            Classified("c2", isSuggestion: true, theme: "Theme two", summary: "Summary two"));

        var context = NewContext();
        var result = await step.ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Success);

        // Only the first suggestion becomes the run's input.
        context.Suggestion.Should().NotBeNull();
        context.Suggestion!.CommentId.Should().Be("c1");
        context.Suggestion.Theme.Should().Be("Theme one");

        await _commentSuggestionRepository.Received(1).CreateAsync(
            Arg.Is<CommentSuggestion>(s =>
                s.CommentId == "c1" && s.Classification == CommentClassification.Accepted),
            Arg.Any<CancellationToken>());

        // The second one is audited as NotSuggestion with the mandatory reason, so it is
        // never reprocessed.
        await _commentSuggestionRepository.Received(1).CreateAsync(
            Arg.Is<CommentSuggestion>(s =>
                s.CommentId == "c2"
                && s.Classification == CommentClassification.NotSuggestion
                && s.RejectionReason == CommentSuggestionStep.LimitOneSuggestionReason),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_PermissionError403_SkipsWithPermissionWarning()
    {
        var step = CreateStep();
        _mediaCommentPort.GetCommentsAsync("media-1", Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<MediaComment>>>(_ =>
                throw new HttpRequestException(
                    "Meta comments failed with 403 (Forbidden): {\"error\":{\"message\":\"Insufficient scope\",\"type\":\"OAuthException\",\"code\":10}}",
                    null,
                    HttpStatusCode.Forbidden));

        var result = await step.ExecuteAsync(NewContext());

        result.Outcome.Should().Be(StepOutcome.Skipped);
        HasWarning(CommentSuggestionStep.PermissionMissingWarning).Should().BeTrue();
        await _classificationPort.DidNotReceive()
            .ClassifyCommentsAsync(Arg.Any<IReadOnlyList<MediaComment>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _commentSuggestionRepository.DidNotReceive()
            .CreateAsync(Arg.Any<CommentSuggestion>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_PermissionErrorBodyCode190_SkipsWithPermissionWarning()
    {
        var step = CreateStep();
        _mediaCommentPort.GetCommentsAsync("media-1", Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<MediaComment>>>(_ =>
                throw new HttpRequestException(
                    "Meta comments failed with 400 (Bad Request): {\"error\":{\"message\":\"Error validating access token\",\"type\":\"OAuthException\",\"code\": 190}}",
                    null,
                    HttpStatusCode.BadRequest));

        var result = await step.ExecuteAsync(NewContext());

        result.Outcome.Should().Be(StepOutcome.Skipped);
        HasWarning(CommentSuggestionStep.PermissionMissingWarning).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_OtherMetaError_SkipsWithGenericWarning()
    {
        var step = CreateStep();
        _mediaCommentPort.GetCommentsAsync("media-1", Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<MediaComment>>>(_ =>
                throw new HttpRequestException(
                    "Meta comments failed with 500 (Internal Server Error): upstream error",
                    null,
                    HttpStatusCode.InternalServerError));

        var result = await step.ExecuteAsync(NewContext());

        result.Outcome.Should().Be(StepOutcome.Skipped);
        HasWarning("Meta comment read failed").Should().BeTrue();
        HasWarning(CommentSuggestionStep.PermissionMissingWarning).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_ClassificationFailure_SkipsWithoutPersistingAnything()
    {
        var step = CreateStep();
        SetupComments(Comment("c1"));
        _classificationPort.ClassifyCommentsAsync(
                Arg.Any<IReadOnlyList<MediaComment>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<CommentClassificationResult>>>(_ =>
                throw new OpenRouterModelException("free/classifier", 500, "boom"));

        var context = NewContext();
        var result = await step.ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Skipped);
        context.Suggestion.Should().BeNull();
        HasWarning("Comment classification failed").Should().BeTrue();
        // Not persisted: the comment stays "new" and is retried on the next run.
        await _commentSuggestionRepository.DidNotReceive()
            .CreateAsync(Arg.Any<CommentSuggestion>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_PostWithoutMediaId_IsIgnored_AndOtherMediaStillRead()
    {
        var step = CreateStep();
        _postRepository.GetLatestPublishedMediaIdsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)new[] { "", "media-1" });
        SetupComments(Comment("c1"));
        SetupClassification(Classified("c1", isSuggestion: false));

        var result = await step.ExecuteAsync(NewContext());

        // The empty id must never reach the port (it would throw ArgumentException and,
        // before RF-20 review, skip the whole step for the day).
        result.Outcome.Should().Be(StepOutcome.Success);
        await _mediaCommentPort.DidNotReceive()
            .GetCommentsAsync("", Arg.Any<CancellationToken>());
        await _mediaCommentPort.Received(1)
            .GetCommentsAsync("media-1", Arg.Any<CancellationToken>());
        await _commentSuggestionRepository.Received(1).CreateAsync(
            Arg.Is<CommentSuggestion>(s => s.CommentId == "c1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_OnlyPostsWithoutMediaId_SkipsWithoutCallingCommentApi()
    {
        var step = CreateStep();
        _postRepository.GetLatestPublishedMediaIdsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)new[] { "", "   " });

        var result = await step.ExecuteAsync(NewContext());

        result.Outcome.Should().Be(StepOutcome.Skipped);
        await _mediaCommentPort.DidNotReceive()
            .GetCommentsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _classificationPort.DidNotReceive()
            .ClassifyCommentsAsync(Arg.Any<IReadOnlyList<MediaComment>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_EmptyCommentId_IsFilteredBeforeClassification()
    {
        var step = CreateStep();
        SetupComments(Comment(string.Empty), Comment("c2"));

        var result = await step.ExecuteAsync(NewContext());

        result.Outcome.Should().Be(StepOutcome.Success);
        await _classificationPort.Received(1).ClassifyCommentsAsync(
            Arg.Is<IReadOnlyList<MediaComment>>(comments =>
                comments.Count == 1 && comments[0].CommentId == "c2"),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _commentSuggestionRepository.Received(1).CreateAsync(
            Arg.Is<CommentSuggestion>(s => s.CommentId == "c2"),
            Arg.Any<CancellationToken>());
    }

    private sealed class CapturingLogger : ILogger<CommentSuggestionStep>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
