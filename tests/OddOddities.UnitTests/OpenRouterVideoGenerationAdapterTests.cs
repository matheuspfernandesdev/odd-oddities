using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OddOddities.Domain.Exceptions;
using OddOddities.Domain.ValueObjects;
using OddOddities.Infrastructure.Adapters;

namespace OddOddities.UnitTests;

/// <summary>
/// Covers RF-17 AC2/AC3 for the asynchronous OpenRouter video flow:
/// submit (202 + job id) -> poll (usage.cost) -> download (Bearer), and the
/// OpenRouterModelException semantics for submit/poll/download failures plus
/// cancellation propagation.
/// </summary>
public class OpenRouterVideoGenerationAdapterTests
{
    private sealed record RecordedRequest(string Method, string PathAndQuery, string? Authorization, string? Body);

    /// <summary>
    /// Records every outgoing request and answers with the injected responder.
    /// Requests are snapshotted at send time because the adapter disposes them.
    /// </summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<RecordedRequest> Requests { get; } = new();

        public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(
                request.Method.Method,
                request.RequestUri!.PathAndQuery,
                request.Headers.TryGetValues("Authorization", out var values)
                    ? string.Join(",", values)
                    : null,
                body));

            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static OpenRouterVideoGenerationAdapter CreateAdapter(
        HttpMessageHandler handler,
        AppConfiguration? config = null)
    {
        config ??= new AppConfiguration();
        config.OpenRouter.ApiKey = "test-key";

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/")
        };

        return new OpenRouterVideoGenerationAdapter(
            httpClient,
            Options.Create(config),
            NullLogger<OpenRouterVideoGenerationAdapter>.Instance);
    }

    private static RoutingHandler HappyPathHandler()
        => new(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json(
                    """{"id":"job-1","polling_url":"https://openrouter.ai/api/v1/videos/job-1","status":"pending"}""",
                    HttpStatusCode.Accepted);
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/content", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3, 4])
                };
            }

            return Json("""{"id":"job-1","status":"completed","usage":{"cost":0.083}}""");
        });

    [Fact]
    public async Task GenerateVideoAsync_HappyPath_SubmitsPollsAndDownloadsWithBearer()
    {
        var handler = HappyPathHandler();
        var adapter = CreateAdapter(handler);

        var result = await adapter.GenerateVideoAsync("haunted lighthouse", "vid/model");

        result.VideoBytes.Should().Equal(1, 2, 3, 4);
        result.ModelId.Should().Be("vid/model");
        result.CostUsd.Should().Be(0.083m);
        result.DurationSeconds.Should().Be(5); // AppConfiguration.Video.DurationSeconds default

        handler.Requests.Should().HaveCount(3);

        // Phase 1: POST videos with the RF-15 video shape parameters.
        var submit = handler.Requests[0];
        submit.Method.Should().Be("POST");
        submit.PathAndQuery.Should().Be("/api/v1/videos");
        submit.Body.Should().Contain("\"model\":\"vid/model\"");
        submit.Body.Should().Contain("\"duration\":5");
        submit.Body.Should().Contain("\"resolution\":\"480p\"");
        submit.Body.Should().Contain("\"aspect_ratio\":\"9:16\"");
        submit.Body.Should().Contain("\"generate_audio\":true");

        // Phase 2: GET videos/{jobId}.
        handler.Requests[1].PathAndQuery.Should().Be("/api/v1/videos/job-1");

        // Phase 3: GET videos/{jobId}/content?index=0 WITH the Bearer token.
        var download = handler.Requests[2];
        download.PathAndQuery.Should().Be("/api/v1/videos/job-1/content?index=0");
        download.Authorization.Should().Be("Bearer test-key");
    }

    [Fact]
    public async Task GenerateVideoAsync_UsageCostAsString_ParsesCost()
    {
        var handler = new RoutingHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
                return Json("""{"id":"job-1","status":"pending"}""", HttpStatusCode.Accepted);

            if (request.RequestUri!.AbsolutePath.EndsWith("/content", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([7]) };

            // usage.cost may arrive as a JSON string (same tolerance as the model catalog).
            return Json("""{"id":"job-1","status":"completed","usage":{"cost":"0.05"}}""");
        });

        var adapter = CreateAdapter(handler);

        var result = await adapter.GenerateVideoAsync("theme", "vid/model");

        result.CostUsd.Should().Be(0.05m);
        result.VideoBytes.Should().Equal(7);
    }

    [Fact]
    public async Task GenerateVideoAsync_SubmitFailure_ThrowsOpenRouterModelException()
    {
        var handler = new RoutingHandler(_ => Json("""{"error":"bad request"}""", HttpStatusCode.InternalServerError));
        var adapter = CreateAdapter(handler);

        var act = () => adapter.GenerateVideoAsync("theme", "vid/model");

        var ex = await act.Should().ThrowAsync<OpenRouterModelException>();
        ex.Which.ModelId.Should().Be("vid/model");
        ex.Which.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GenerateVideoAsync_PollFailure_ThrowsOpenRouterModelException()
    {
        // AC3: poll failures (non-success GET videos/{jobId}) are model-level failures,
        // with the failing status code preserved and no download attempted.
        var handler = new RoutingHandler(request =>
            request.Method == HttpMethod.Post
                ? Json("""{"id":"job-1","status":"pending"}""", HttpStatusCode.Accepted)
                : Json("""{"error":"upstream unavailable"}""", HttpStatusCode.BadGateway));

        var adapter = CreateAdapter(handler);

        var act = () => adapter.GenerateVideoAsync("theme", "vid/model");

        var ex = await act.Should().ThrowAsync<OpenRouterModelException>();
        ex.Which.ModelId.Should().Be("vid/model");
        ex.Which.StatusCode.Should().Be(502);
        ex.Which.Message.Should().Contain("poll failed");
        // Only submit + failed poll: the job never reaches the download phase.
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task GenerateVideoAsync_JobFailed_ThrowsOpenRouterModelExceptionWithErrorDetail()
    {
        var handler = new RoutingHandler(request =>
            request.Method == HttpMethod.Post
                ? Json("""{"id":"job-1","status":"pending"}""", HttpStatusCode.Accepted)
                : Json("""{"id":"job-1","status":"failed","error":{"message":"content policy violation"}}"""));

        var adapter = CreateAdapter(handler);

        var act = () => adapter.GenerateVideoAsync("theme", "vid/model");

        var ex = await act.Should().ThrowAsync<OpenRouterModelException>();
        ex.Which.Message.Should().Contain("content policy violation");
        // Only submit + first poll: the failure is terminal, no download happens.
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task GenerateVideoAsync_DownloadFailure_ThrowsOpenRouterModelException()
    {
        var handler = new RoutingHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
                return Json("""{"id":"job-1","status":"pending"}""", HttpStatusCode.Accepted);

            if (request.RequestUri!.AbsolutePath.EndsWith("/content", StringComparison.Ordinal))
                return Json("""{"error":"forbidden"}""", HttpStatusCode.Forbidden);

            return Json("""{"id":"job-1","status":"completed","usage":{"cost":0.01}}""");
        });

        var adapter = CreateAdapter(handler);

        var act = () => adapter.GenerateVideoAsync("theme", "vid/model");

        var ex = await act.Should().ThrowAsync<OpenRouterModelException>();
        ex.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GenerateVideoAsync_WhileJobPending_CancellationPropagates()
    {
        using var cts = new CancellationTokenSource();

        var handler = new RoutingHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
                return Json("""{"id":"job-1","status":"pending"}""", HttpStatusCode.Accepted);

            // Cancel while the job is still in progress: the adapter must propagate
            // the cancellation instead of wrapping it into a model failure.
            cts.Cancel();
            return Json("""{"id":"job-1","status":"in_progress"}""");
        });

        var adapter = CreateAdapter(handler);

        var act = () => adapter.GenerateVideoAsync("theme", "vid/model", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().HaveCount(2, "submit + first poll, then cancellation");
    }

    [Fact]
    public async Task GenerateVideoAsync_EmptyPrompt_ThrowsArgumentException()
    {
        var adapter = CreateAdapter(HappyPathHandler());

        var act = () => adapter.GenerateVideoAsync(" ", "vid/model");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
