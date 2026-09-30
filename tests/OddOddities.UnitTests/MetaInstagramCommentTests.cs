using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OddOddities.Domain.ValueObjects;
using OddOddities.Infrastructure.Adapters;

namespace OddOddities.UnitTests;

/// <summary>
/// RF-19: comment mapping, pagination by "after" cursor, reply payload and Meta error
/// propagation for IMediaCommentPort implemented by MetaInstagramPublishingAdapter.
/// </summary>
public class MetaInstagramCommentTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;
        private readonly HttpStatusCode _statusCode;

        public List<string> RequestUrls { get; } = new();
        public List<HttpMethod> RequestMethods { get; } = new();
        public List<string?> RequestBodies { get; } = new();

        public StubHandler(params string[] responses)
            : this(HttpStatusCode.OK, responses)
        {
        }

        public StubHandler(HttpStatusCode statusCode, params string[] responses)
        {
            _statusCode = statusCode;
            _responses = new Queue<string>(responses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUrls.Add(request.RequestUri!.ToString());
            RequestMethods.Add(request.Method);
            RequestBodies.Add(request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }
    }

    private static MetaInstagramPublishingAdapter CreateAdapter(StubHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.instagram.com")
        };

        return new MetaInstagramPublishingAdapter(
            httpClient,
            Options.Create(new AppConfiguration()),
            NullLogger<MetaInstagramPublishingAdapter>.Instance);
    }

    private const string SinglePageJson = """
    {
      "data": [
        {
          "id": "18099212345678901_1234567890",
          "text": "More ocean facts please!",
          "timestamp": "2026-09-29T10:56:33+0000",
          "from": { "username": "curious_fan" }
        }
      ]
    }
    """;

    [Fact]
    public async Task GetCommentsAsync_MapsCommentFields_AndRequestsExpectedQuery()
    {
        var handler = new StubHandler(SinglePageJson);
        var adapter = CreateAdapter(handler);

        var comments = await adapter.GetCommentsAsync("media-1");

        var comment = comments.Should().ContainSingle().Which;
        comment.CommentId.Should().Be("18099212345678901_1234567890");
        comment.Text.Should().Be("More ocean facts please!");
        comment.Timestamp.Should().Be(new DateTime(2026, 9, 29, 10, 56, 33, DateTimeKind.Utc));
        comment.AuthorUsername.Should().Be("curious_fan");

        handler.RequestUrls.Should().ContainSingle();
        handler.RequestUrls[0].Should().Contain("/v26.0/media-1/comments");
        handler.RequestUrls[0].Should().Contain("limit=50");
        handler.RequestUrls[0].Should().Contain("fields=id,text,timestamp,from.username");
        handler.RequestUrls[0].Should().NotContain("after=");
    }

    private const string FirstPageJson = """
    {
      "data": [
        { "id": "c_1", "text": "First", "timestamp": "2026-09-29T12:00:00+0000", "from": { "username": "user1" } },
        { "id": "c_2", "text": "Second", "timestamp": "2026-09-29T11:00:00+0000", "from": { "username": "user2" } }
      ],
      "paging": { "cursors": { "before": "QWJj", "after": "CURSOR_PAGE_2" } }
    }
    """;

    private const string SecondPageJson = """
    {
      "data": [
        { "id": "c_3", "text": "Third", "timestamp": "2026-09-29T10:00:00+0000", "from": { "username": "user3" } }
      ]
    }
    """;

    [Fact]
    public async Task GetCommentsAsync_FollowsAfterCursor_UntilExhausted()
    {
        var handler = new StubHandler(FirstPageJson, SecondPageJson);
        var adapter = CreateAdapter(handler);

        var comments = await adapter.GetCommentsAsync("media-1");

        comments.Select(c => c.CommentId).Should().Equal("c_1", "c_2", "c_3");

        handler.RequestUrls.Should().HaveCount(2);
        handler.RequestUrls[0].Should().NotContain("after=");
        handler.RequestUrls[1].Should().Contain("after=CURSOR_PAGE_2");
    }

    [Fact]
    public async Task GetCommentsAsync_EmptyPage_ReturnsEmptyListWithoutFurtherRequests()
    {
        var handler = new StubHandler("""{ "data": [] }""");
        var adapter = CreateAdapter(handler);

        var comments = await adapter.GetCommentsAsync("media-1");

        comments.Should().BeEmpty();
        handler.RequestUrls.Should().ContainSingle();
    }

    private const string ReplyJson = """{ "id": "reply_1" }""";

    [Fact]
    public async Task ReplyToCommentAsync_PostsMessageToRepliesEndpoint()
    {
        var handler = new StubHandler(ReplyJson);
        var adapter = CreateAdapter(handler);

        await adapter.ReplyToCommentAsync("comment_1", "Thanks for the suggestion!");

        handler.RequestUrls.Should().ContainSingle();
        handler.RequestUrls[0].Should().Contain("/v26.0/comment_1/replies");
        handler.RequestMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Post);

        var body = handler.RequestBodies.Should().ContainSingle().Which;
        Uri.UnescapeDataString(body!.Replace('+', ' '))
            .Should().Be("message=Thanks for the suggestion!");
    }

    [Fact]
    public async Task ReplyToCommentAsync_OnMetaError_ThrowsWithMetaErrorBodyPreserved()
    {
        // RF-20 inspects Meta error codes (10/190/3) inside the exception message,
        // so EnsureSuccessAsync must keep the response body.
        const string errorBody = """
        {"error":{"message":"Application does not have permission for this action","type":"OAuthException","code":10}}
        """;
        var handler = new StubHandler(HttpStatusCode.Forbidden, errorBody);
        var adapter = CreateAdapter(handler);

        var act = () => adapter.ReplyToCommentAsync("comment_1", "Hello");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.Message.Should().Contain("OAuthException").And.Contain("\"code\":10");
    }

    [Fact]
    public async Task GetCommentsAsync_EmptyMediaId_ThrowsArgumentException()
    {
        var handler = new StubHandler(SinglePageJson);
        var adapter = CreateAdapter(handler);

        var act = () => adapter.GetCommentsAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
