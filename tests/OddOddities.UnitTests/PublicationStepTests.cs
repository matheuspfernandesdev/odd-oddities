using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OddOddities.Application.Pipeline;
using OddOddities.Application.Steps;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Interfaces;

namespace OddOddities.UnitTests;

public class PublicationStepTests
{
    private const string ContainerId = "container-1";
    private const string MediaId = "media-9";
    private const string Permalink = "https://www.instagram.com/p/abc/";

    private readonly IPresignedUrlPort _presignedUrl = Substitute.For<IPresignedUrlPort>();
    private readonly IInstagramPublishingPort _instagram = Substitute.For<IInstagramPublishingPort>();
    private readonly IPostRepository _postRepository = Substitute.For<IPostRepository>();
    private readonly IPublicationRepository _publicationRepository = Substitute.For<IPublicationRepository>();

    private readonly Post _post = new() { Id = 42, Caption = "A caption", Status = PostStatus.ImageProcessed };
    private Publication? _created;

    private PublicationStep CreateStep()
    {
        _presignedUrl.GeneratePresignedUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://storage.example.com/image.png");

        _postRepository.GetByIdAsync(_post.Id, Arg.Any<CancellationToken>()).Returns(_post);

        _publicationRepository.GetByPostIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((Publication?)null);

        _publicationRepository.CreateAsync(Arg.Any<Publication>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                _created = ci.Arg<Publication>();
                return _created;
            });

        _instagram.CreateMediaContainerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ContainerId);

        return new PublicationStep(
            _presignedUrl,
            _instagram,
            _postRepository,
            _publicationRepository,
            NullLogger<PublicationStep>.Instance);
    }

    private static PipelineContext NewContext(long postId = 42)
    {
        var context = new PipelineContext
        {
            Text = new TextContext(postId, "content", "summary", "theme", "hash", "source", "caption"),
            Image = new ImageContext("image.png", 1080, 1080, 1000)
        };

        return context;
    }

    [Fact]
    public async Task Execute_ContainerFinishedAndPublished_MarksPostPublished()
    {
        var step = CreateStep();
        _instagram.GetContainerStatusAsync(ContainerId, Arg.Any<CancellationToken>()).Returns("FINISHED");
        _instagram.PublishMediaAsync(ContainerId, Arg.Any<CancellationToken>()).Returns(MediaId);
        _instagram.GetMediaStatusAsync(MediaId, Arg.Any<CancellationToken>())
            .Returns(("PUBLISHED", "PUBLISHED", Permalink));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeTrue();
        _post.Status.Should().Be(PostStatus.Published);
        _post.PublishedAt.Should().NotBeNull();

        _created.Should().NotBeNull();
        _created!.MetaMediaId.Should().Be(MediaId);
        _created.MetaMediaStatus.Should().Be("PUBLISHED");
        _created.MetaPermalink.Should().Be(Permalink);
    }

    [Fact]
    public async Task Execute_ContainerStillProcessing_KeepsPollingUntilFinished()
    {
        var step = CreateStep();
        _instagram.GetContainerStatusAsync(ContainerId, Arg.Any<CancellationToken>())
            .Returns("PROCESSING", "FINISHED");
        _instagram.PublishMediaAsync(ContainerId, Arg.Any<CancellationToken>()).Returns(MediaId);
        _instagram.GetMediaStatusAsync(MediaId, Arg.Any<CancellationToken>())
            .Returns(("PUBLISHED", "PUBLISHED", Permalink));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeTrue();
        await _instagram.Received(2).GetContainerStatusAsync(ContainerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ContainerError_FailsWithoutPublishing()
    {
        var step = CreateStep();
        _instagram.GetContainerStatusAsync(ContainerId, Arg.Any<CancellationToken>()).Returns("ERROR");

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("CONTAINER_FAILED");

        await _instagram.DidNotReceive().PublishMediaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _post.Status.Should().Be(PostStatus.ImageProcessed);

        _created.Should().NotBeNull();
        _created!.MetaMediaId.Should().Be(ContainerId);
        _created.MetaMediaStatus.Should().Be("ERROR");
        _created.MetaMediaStatusCode.Should().Be("ERROR");
    }

    [Fact]
    public async Task Execute_PublishApiError_FailsAndRecordsContainerId()
    {
        var step = CreateStep();
        _instagram.GetContainerStatusAsync(ContainerId, Arg.Any<CancellationToken>()).Returns("FINISHED");
        _instagram.PublishMediaAsync(ContainerId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Meta media_publish failed with 400 (Bad Request)"));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeFalse();
        result.FailureStep.Should().Be(FailureStep.InstagramApi);
        result.ErrorCode.Should().Be(nameof(HttpRequestException));
        result.FailureReason.Should().Contain("media_publish failed");

        _created.Should().NotBeNull();
        _created!.MetaMediaId.Should().Be(ContainerId);
        _created.MetaMediaStatus.Should().Be("ERROR");
        _post.Status.Should().Be(PostStatus.ImageProcessed);
    }

    [Fact]
    public async Task Execute_MediaReportedAsError_FailsWithPublicationFailed()
    {
        var step = CreateStep();
        _instagram.GetContainerStatusAsync(ContainerId, Arg.Any<CancellationToken>()).Returns("FINISHED");
        _instagram.PublishMediaAsync(ContainerId, Arg.Any<CancellationToken>()).Returns(MediaId);
        _instagram.GetMediaStatusAsync(MediaId, Arg.Any<CancellationToken>())
            .Returns(("ERROR", "ERROR", null));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("PUBLICATION_FAILED");
        _created!.MetaMediaStatusCode.Should().Be("ERROR");
    }

    [Fact]
    public async Task Execute_ExistingPublication_IsUpdatedInsteadOfDuplicated()
    {
        var existing = new Publication
        {
            Id = 7,
            PostId = _post.Id,
            MetaMediaId = "old-media",
            MetaMediaStatus = "TIMEOUT",
            MetaMediaStatusCode = "TIMEOUT",
            AttemptCount = 1
        };

        var step = CreateStep();
        _publicationRepository.GetByPostIdAsync(_post.Id, Arg.Any<CancellationToken>()).Returns(existing);
        _instagram.GetContainerStatusAsync(ContainerId, Arg.Any<CancellationToken>()).Returns("FINISHED");
        _instagram.PublishMediaAsync(ContainerId, Arg.Any<CancellationToken>()).Returns(MediaId);
        _instagram.GetMediaStatusAsync(MediaId, Arg.Any<CancellationToken>())
            .Returns(("PUBLISHED", "PUBLISHED", Permalink));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeTrue();
        await _publicationRepository.DidNotReceive().CreateAsync(Arg.Any<Publication>(), Arg.Any<CancellationToken>());
        await _publicationRepository.Received(1).UpdateAsync(existing, Arg.Any<CancellationToken>());

        existing.MetaMediaId.Should().Be(MediaId);
        existing.MetaMediaStatus.Should().Be("PUBLISHED");
        existing.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task Execute_PermalinkUnavailable_StillSucceeds()
    {
        var step = CreateStep();
        _instagram.GetContainerStatusAsync(ContainerId, Arg.Any<CancellationToken>()).Returns("FINISHED");
        _instagram.PublishMediaAsync(ContainerId, Arg.Any<CancellationToken>()).Returns(MediaId);
        _instagram.GetMediaStatusAsync(MediaId, Arg.Any<CancellationToken>())
            .Returns(("PENDING", "PENDING", null));

        var result = await step.ExecuteAsync(NewContext());

        result.IsSuccess.Should().BeTrue();
        _post.Status.Should().Be(PostStatus.Published);
        _created!.MetaPermalink.Should().BeNull();
    }

    [Fact]
    public async Task Execute_HostShutdown_PropagatesCancellationAndLeavesPostUntouched()
    {
        var step = CreateStep();
        _instagram.GetContainerStatusAsync(ContainerId, Arg.Any<CancellationToken>())
            .Returns("PROCESSING");

        using var shutdown = new CancellationTokenSource();
        shutdown.Cancel();

        var act = () => step.ExecuteAsync(NewContext(), shutdown.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _postRepository.DidNotReceive().UpdateAsync(Arg.Any<Post>(), Arg.Any<CancellationToken>());
        _post.Status.Should().Be(PostStatus.ImageProcessed);
    }
}
