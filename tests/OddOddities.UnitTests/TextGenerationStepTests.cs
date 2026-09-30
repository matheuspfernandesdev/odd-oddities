using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OddOddities.Application.Pipeline;
using OddOddities.Application.Services;
using OddOddities.Application.Steps;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Exceptions;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.UnitTests;

public class TextGenerationStepTests
{
    private readonly ITextGenerationPort _textPort = Substitute.For<ITextGenerationPort>();
    private readonly ISimilarityCheckPort _similarity = Substitute.For<ISimilarityCheckPort>();
    private readonly IPostRepository _postRepository = Substitute.For<IPostRepository>();
    private readonly IGenerationAttemptRepository _attemptRepository = Substitute.For<IGenerationAttemptRepository>();
    private readonly IModelSelectionService _modelSelection = Substitute.For<IModelSelectionService>();
    private readonly ICommentSuggestionRepository _commentSuggestionRepository = Substitute.For<ICommentSuggestionRepository>();
    private readonly AppConfiguration _config = new();

    private TextGenerationStep CreateStep()
    {
        _similarity.ComputeContentHash(Arg.Any<string>()).Returns("hash-1");
        _similarity.IsContentHashDuplicateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _similarity.IsSummarySimilarAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<CancellationToken>()).Returns(false);

        _attemptRepository.CreateAsync(Arg.Any<GenerationAttempt>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<GenerationAttempt>());

        _postRepository.CreateAsync(Arg.Any<Post>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var post = ci.Arg<Post>();
                post.Id = 42;
                return post;
            });

        return new TextGenerationStep(
            _textPort,
            _similarity,
            _postRepository,
            _attemptRepository,
            _modelSelection,
            _commentSuggestionRepository,
            Options.Create(_config),
            NullLogger<TextGenerationStep>.Instance);
    }

    private static PipelineContext NewContext()
        => PipelineContext.Create(
            "exec-1",
            new Category { Id = 1, Name = "Science" },
            new Subcategory { Id = 1, Name = "Space" });

    private static ModelDescriptor Model(string id, bool isFree = true, decimal? prompt = null, decimal? completion = null)
        => new(id, id, prompt, completion, null, isFree, 262_144, 0);

    private static TextGenerationResult Ok(string modelId, string textContent = "A short curiosity.")
        => new(
            TextContent: textContent,
            Summary: "A summary",
            Theme: "theme",
            SourceUrl: "https://example.com",
            Category: "Science",
            Subcategory: "Space",
            ModelId: modelId,
            CostUsd: null,
            TokensIn: 100,
            TokensOut: 50,
            DurationMs: 10);

    private void SetupChain(params ModelDescriptor[] models)
        => _modelSelection.GetTextChainAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ModelDescriptor>)models);

    [Fact]
    public async Task Execute_ModelError_FallsBackToNextModel_AndSucceeds()
    {
        var step = CreateStep();
        SetupChain(Model("model-a"), Model("model-b"));

        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>())
            .Returns<Task<TextGenerationResult>>(_ =>
                throw new OpenRouterModelException("model-a", 404, "model not found"));

        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-b", Arg.Any<CancellationToken>())
            .Returns(Ok("model-b"));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeTrue();
        await _textPort.Received(1).GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-b", Arg.Any<CancellationToken>());
        await _attemptRepository.Received().CreateAsync(
            Arg.Is<GenerationAttempt>(a => a.Status == AttemptStatus.Error && a.ModelId == "model-a" && a.PostId == null),
            Arg.Any<CancellationToken>());
        await _attemptRepository.Received().CreateAsync(
            Arg.Is<GenerationAttempt>(a => a.Status == AttemptStatus.Success && a.ModelId == "model-b" && a.PostId == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_AllModelsFail_ReturnsAllModelsFailed()
    {
        var step = CreateStep();
        SetupChain(Model("model-a"), Model("model-b"));

        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<TextGenerationResult>>(_ =>
                throw new OpenRouterModelException("(any)", 404, "model not found"));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ALL_MODELS_FAILED");
        await _textPort.Received(2).GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_BudgetExceeded_FailsWithoutCallingPort()
    {
        var step = CreateStep();
        SetupChain(Model("paid/model", isFree: false, prompt: 0.00001m, completion: 0.00001m));

        var context = NewContext();
        context.CostCeilingUsd = 0.001m;

        var result = await step.ExecuteAsync(context);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("BUDGET_EXCEEDED");
        await _textPort.DidNotReceive().GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ContentTooLong_RetriesSameModelUpToMaxAttempts_ThenFails()
    {
        var step = CreateStep();
        SetupChain(Model("model-a"));

        var longText = new string('x', 900);
        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>())
            .Returns(Ok("model-a", longText));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("TEXT_TOO_LONG");
        await _textPort.Received(3).GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>());
        await _attemptRepository.Received(3).CreateAsync(
            Arg.Is<GenerationAttempt>(a => a.Status == AttemptStatus.Rejected && a.RejectionReason == "TEXT_TOO_LONG"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_Success_AccumulatesActualCostOnContext()
    {
        var step = CreateStep();
        SetupChain(Model("model-a"));

        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>())
            .Returns(Ok("model-a") with { CostUsd = 0.0042m });

        var context = NewContext();
        var result = await step.ExecuteAsync(context);

        result.IsSuccess.Should().BeTrue();
        context.AccumulatedCostUsd.Should().Be(0.0042m);
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
    public async Task Execute_Transient429_RetriesSameModelWithBackoff_AndSucceeds()
    {
        var step = CreateStep();
        SetupChain(Model("model-a"));

        var calls = 0;
        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                if (calls == 1)
                    throw new OpenRouterModelException("model-a", 429, "rate limited");

                return Ok("model-a");
            });

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeTrue();
        await _textPort.Received(2).GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>());
        await _attemptRepository.Received().CreateAsync(
            Arg.Is<GenerationAttempt>(a => a.Status == AttemptStatus.Error && a.ModelId == "model-a" && a.RejectionReason!.Contains("rate limited")),
            Arg.Any<CancellationToken>());
        await _attemptRepository.Received().CreateAsync(
            Arg.Is<GenerationAttempt>(a => a.Status == AttemptStatus.Success && a.ModelId == "model-a" && a.PostId == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_Transient429Always_ExhaustsAttempts_ReturnsTransientApiError()
    {
        var step = CreateStep();
        SetupChain(Model("model-a"));

        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>())
            .Returns<Task<TextGenerationResult>>(_ =>
                throw new OpenRouterModelException("model-a", 429, "rate limited"));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("TRANSIENT_API_ERROR");
        await _textPort.Received(3).GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>());
        await _attemptRepository.Received(3).CreateAsync(
            Arg.Is<GenerationAttempt>(a => a.Status == AttemptStatus.Error && a.ModelId == "model-a"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_NonTransient4xx_AdvancesToNextModel_WithoutRetrying()
    {
        var step = CreateStep();
        SetupChain(Model("model-a"), Model("model-b"));

        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>())
            .Returns<Task<TextGenerationResult>>(_ =>
                throw new OpenRouterModelException("model-a", 401, "unauthorized"));

        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-b", Arg.Any<CancellationToken>())
            .Returns(Ok("model-b"));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeTrue();
        await _textPort.Received(1).GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>());
        await _textPort.Received(1).GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-b", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ModelErrorWithLongMessage_TruncatesRejectionReasonTo255()
    {
        var step = CreateStep();
        SetupChain(Model("model-a"), Model("model-b"));

        var longMessage = new string('e', 500);
        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>())
            .Returns<Task<TextGenerationResult>>(_ =>
                throw new OpenRouterModelException("model-a", 404, longMessage));

        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-b", Arg.Any<CancellationToken>())
            .Returns(Ok("model-b"));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeTrue();
        await _attemptRepository.Received().CreateAsync(
            Arg.Is<GenerationAttempt>(a =>
                a.Status == AttemptStatus.Error
                && a.ModelId == "model-a"
                && a.RejectionReason != null
                && a.RejectionReason.Length <= 255),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_TransientTimeout_TaskCanceledException_RetriesSameModel()
    {
        var step = CreateStep();
        SetupChain(Model("model-a"));

        var calls = 0;
        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                if (calls == 1)
                    throw new TaskCanceledException("timeout");

                return Ok("model-a");
            });

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeTrue();
        await _textPort.Received(2).GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>());
    }

    // --- RF-21: suggested theme + author credit -------------------------------------

    private static PipelineContext ContextWithSuggestion(
        string author = "curious_fan",
        string commentId = "comment-1")
    {
        var context = NewContext();
        context.Suggestion = new SuggestionContext(
            Theme: "Bioluminescence",
            Summary: "Why do deep-sea creatures glow?",
            AuthorUsername: author,
            CommentId: commentId,
            SourceCommentText: "You should post about glowing fish!");
        return context;
    }

    [Fact]
    public async Task Execute_WithSuggestion_UsesThemeAndSummaryAsGenerationInput()
    {
        var step = CreateStep();
        SetupChain(Model("model-a"));
        _commentSuggestionRepository
            .GetByCommentIdAsync("comment-1", Arg.Any<CancellationToken>())
            .Returns(new CommentSuggestion { Id = 7, CommentId = "comment-1" });
        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>())
            .Returns(Ok("model-a"));

        var result = await step.ExecuteAsync(ContextWithSuggestion());

        result.IsSuccess.Should().BeTrue();
        await _textPort.Received(1).GenerateCuriosityAsync(
            "Bioluminescence",
            "Why do deep-sea creatures glow?",
            "model-a",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithAcceptedSuggestion_SetsCreditCaptionAndSourceSuggestionId()
    {
        var step = CreateStep();
        SetupChain(Model("model-a"));
        _commentSuggestionRepository
            .GetByCommentIdAsync("comment-1", Arg.Any<CancellationToken>())
            .Returns(new CommentSuggestion { Id = 7, CommentId = "comment-1" });
        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>())
            .Returns(Ok("model-a", "A short curiosity."));

        var context = ContextWithSuggestion();
        var result = await step.ExecuteAsync(context);

        result.IsSuccess.Should().BeTrue();
        await _postRepository.Received(1).CreateAsync(
            Arg.Is<Post>(p =>
                p.SourceCommentSuggestionId == 7
                && p.Caption == "A short curiosity.\n\nSuggested by @curious_fan\n\nSource: https://example.com"),
            Arg.Any<CancellationToken>());
        context.Text.Caption.Should().Be(
            "A short curiosity.\n\nSuggested by @curious_fan\n\nSource: https://example.com");
    }

    [Fact]
    public async Task Execute_SuggestionRejectedByHash_MarksRejected_AndFallsBackToCategory()
    {
        var step = CreateStep();
        SetupChain(Model("model-a"));

        var entity = new CommentSuggestion
        {
            Id = 7,
            CommentId = "comment-1",
            Classification = CommentClassification.Accepted
        };
        _commentSuggestionRepository
            .GetByCommentIdAsync("comment-1", Arg.Any<CancellationToken>())
            .Returns(entity);

        // First call (suggestion input) produces a hash-duplicate; second call (category
        // fallback, suggestion cleared) produces a clean result.
        var calls = 0;
        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>())
            .Returns(_ => { calls++; return Ok("model-a"); });
        _similarity.IsContentHashDuplicateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => calls == 1);

        var context = ContextWithSuggestion();
        var result = await step.ExecuteAsync(context);

        result.IsSuccess.Should().BeTrue();
        // Suggestion marked Rejected with the hash reason.
        entity.Classification.Should().Be(CommentClassification.Rejected);
        entity.RejectionReason.Should().Be("HASH_DUPLICATE");
        await _commentSuggestionRepository.Received(1).UpdateAsync(
            Arg.Is<CommentSuggestion>(cs => cs.Id == 7 && cs.Classification == CommentClassification.Rejected),
            Arg.Any<CancellationToken>());
        // Suggestion cleared -> fallback flow used the category/subcategory.
        context.Suggestion.Should().BeNull();
        await _textPort.Received(1).GenerateCuriosityAsync(
            "Science", "Space", "model-a", Arg.Any<CancellationToken>());
        // A post was produced by the fallback with no suggestion credit.
        await _postRepository.Received(1).CreateAsync(
            Arg.Is<Post>(p => p.SourceCommentSuggestionId == null
                && p.Caption == "A short curiosity.\n\nSource: https://example.com"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_SuggestionRejected_DoesNotConsumeExtraAttempt()
    {
        var step = CreateStep();
        SetupChain(Model("model-a"));
        _commentSuggestionRepository
            .GetByCommentIdAsync("comment-1", Arg.Any<CancellationToken>())
            .Returns(new CommentSuggestion { Id = 7, CommentId = "comment-1" });

        // Suggestion attempt rejected by hash; then all category attempts also hash-duplicate.
        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>())
            .Returns(Ok("model-a"));
        _similarity.IsContentHashDuplicateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var context = ContextWithSuggestion();
        var result = await step.ExecuteAsync(context);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("HASH_DUPLICATE");
        // BR-006: suggestion rejection does not consume an attempt, so the category flow
        // still gets the full MaxGenerationAttempts (3). 1 suggestion + 3 category = 4 calls.
        await _textPort.Received(4).GenerateCuriosityAsync(
            Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_NoSuggestion_NormalCaption_Unchanged()
    {
        var step = CreateStep();
        SetupChain(Model("model-a"));
        _textPort.GenerateCuriosityAsync(Arg.Any<string>(), Arg.Any<string>(), "model-a", Arg.Any<CancellationToken>())
            .Returns(Ok("model-a", "A short curiosity."));

        var context = NewContext();
        var result = await step.ExecuteAsync(context);

        result.IsSuccess.Should().BeTrue();
        await _postRepository.Received(1).CreateAsync(
            Arg.Is<Post>(p =>
                p.SourceCommentSuggestionId == null
                && p.Caption == "A short curiosity.\n\nSource: https://example.com"),
            Arg.Any<CancellationToken>());
        await _commentSuggestionRepository.DidNotReceive().GetByCommentIdAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _commentSuggestionRepository.DidNotReceive().UpdateAsync(
            Arg.Any<CommentSuggestion>(), Arg.Any<CancellationToken>());
        await _textPort.Received(1).GenerateCuriosityAsync(
            "Science", "Space", "model-a", Arg.Any<CancellationToken>());
    }
}
