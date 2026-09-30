using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Domain.Constants;
using OddOddities.Domain.Exceptions;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// OpenRouter implementation of IVideoGenerationPort (RF-17).
/// OpenRouter video generation is asynchronous and runs in three HTTP phases:
/// 1. POST videos - 202 { id, polling_url, status: "pending" };
/// 2. GET videos/{jobId} every VideoJobPollingIntervalSeconds until "completed"/"failed",
///    at most MaxVideoJobPollingAttempts times, so the whole job is bounded by
///    MaxVideoJobPollingAttempts x VideoJobPollingIntervalSeconds (~15 min);
/// 3. GET videos/{jobId}/content?index=0 with the Bearer token - MP4 bytes (the content
///    URLs returned by the API are not pre-signed, so Authorization is mandatory).
/// The real usage cost reported by the poll response (usage.cost) becomes CostUsd.
/// Every submit/poll/download failure becomes an OpenRouterModelException - the same
/// model-level semantics as OpenRouterImageGenerationAdapter - and caller cancellation
/// always propagates.
/// </summary>
public sealed class OpenRouterVideoGenerationAdapter : IVideoGenerationPort
{
    private readonly HttpClient _httpClient;
    private readonly OpenRouterConfiguration _openRouter;
    private readonly VideoConfiguration _video;
    private readonly ILogger<OpenRouterVideoGenerationAdapter> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenRouterVideoGenerationAdapter(
        HttpClient httpClient,
        IOptions<AppConfiguration> options,
        ILogger<OpenRouterVideoGenerationAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        var config = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _openRouter = config.OpenRouter;
        _video = config.Video;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<VideoGenerationResult> GenerateVideoAsync(
        string prompt,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt cannot be null or empty.", nameof(prompt));

        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("ModelId cannot be null or empty.", nameof(modelId));

        var durationSeconds = _video.GetEffectiveDurationSeconds();

        _logger.LogInformation(
            "Generating video using model {ModelId}, duration={DurationSeconds}s, prompt length={PromptLength}",
            modelId,
            durationSeconds,
            prompt.Length);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var jobId = await SubmitJobAsync(prompt, modelId, durationSeconds, cancellationToken);
            var costUsd = await WaitForJobAsync(jobId, modelId, cancellationToken);
            var videoBytes = await DownloadVideoAsync(jobId, modelId, cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Video generated: ModelId={ModelId}, JobId={JobId}, SizeBytes={SizeBytes}, CostUsd={CostUsd}, ElapsedMs={ElapsedMs}",
                modelId,
                jobId,
                videoBytes.Length,
                costUsd,
                stopwatch.ElapsedMilliseconds);

            return new VideoGenerationResult(
                VideoBytes: videoBytes,
                ModelId: modelId,
                CostUsd: costUsd,
                DurationSeconds: durationSeconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown / step cancellation: propagate untouched (RF-17 AC3).
            throw;
        }
        catch (OpenRouterModelException)
        {
            // Already a model-level failure; keep its status code and message.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Transport/timeout/malformed payload failures carry the same model-level
            // semantics as the image adapter: they advance the fallback chain.
            throw new OpenRouterModelException(
                modelId,
                statusCode: null,
                $"OpenRouter video generation failed for model {modelId}: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Phase 1: submits the video job (POST videos) and returns its job id.
    /// </summary>
    private async Task<string> SubmitJobAsync(
        string prompt,
        string modelId,
        int durationSeconds,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            model = modelId,
            prompt = BuildPrompt(prompt),
            duration = durationSeconds,
            resolution = _video.Resolution,
            aspect_ratio = _video.AspectRatio,
            generate_audio = _video.GenerateAudio
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "videos")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        AddAuthHeaders(httpRequest);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter video submit failed for model {modelId}: {(int)response.StatusCode} {Truncate(errorBody, 300)}");
        }

        VideoJobSubmitResponse? responseBody;
        try
        {
            responseBody = await response.Content.ReadFromJsonAsync<VideoJobSubmitResponse>(
                JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter video submit response for model {modelId} was not valid JSON.",
                ex);
        }

        if (string.IsNullOrWhiteSpace(responseBody?.Id))
        {
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter video submit for model {modelId} returned no job id.");
        }

        // The 202 payload also carries polling_url; RF-17 AC2 polls the documented
        // GET videos/{jobId} path instead, so the URL is only logged for diagnostics.
        _logger.LogInformation(
            "Video job submitted: JobId={JobId}, PollingUrl={PollingUrl}, ModelId={ModelId}",
            responseBody.Id,
            responseBody.PollingUrl,
            modelId);

        return responseBody.Id;
    }

    /// <summary>
    /// Phase 2: polls GET videos/{jobId} until the job reports "completed" (returning the
    /// real usage cost) or "failed". At most MaxVideoJobPollingAttempts polls spaced
    /// VideoJobPollingIntervalSeconds apart, which is the global job timeout.
    /// </summary>
    private async Task<decimal?> WaitForJobAsync(string jobId, string modelId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= PipelineConstants.MaxVideoJobPollingAttempts; attempt++)
        {
            if (attempt > 1)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(PipelineConstants.VideoJobPollingIntervalSeconds),
                    cancellationToken);
            }

            var status = await PollJobAsync(jobId, modelId, cancellationToken);

            _logger.LogInformation(
                "Video job status attempt {Attempt}/{MaxAttempts}: JobId={JobId}, status={Status}",
                attempt,
                PipelineConstants.MaxVideoJobPollingAttempts,
                jobId,
                status.Status);

            if (string.Equals(status.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Video job completed: JobId={JobId}, CostUsd={CostUsd}",
                    jobId,
                    status.CostUsd);

                return status.CostUsd;
            }

            if (string.Equals(status.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                throw new OpenRouterModelException(
                    modelId,
                    statusCode: null,
                    $"OpenRouter video job {jobId} failed for model {modelId}: {status.ErrorDetail}");
            }

            // "pending" / "in_progress": keep polling.
        }

        // Global job timeout: MaxVideoJobPollingAttempts x VideoJobPollingIntervalSeconds.
        throw new OpenRouterModelException(
            modelId,
            statusCode: null,
            $"OpenRouter video job {jobId} did not complete after {PipelineConstants.MaxVideoJobPollingAttempts} attempts " +
            $"({PipelineConstants.MaxVideoJobPollingAttempts * PipelineConstants.VideoJobPollingIntervalSeconds}s) for model {modelId}");
    }

    private async Task<VideoJobStatus> PollJobAsync(string jobId, string modelId, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"videos/{jobId}");
        AddAuthHeaders(httpRequest);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter video poll failed for job {jobId}: {(int)response.StatusCode} {Truncate(errorBody, 300)}");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter video poll response for job {jobId} was not valid JSON.",
                ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new OpenRouterModelException(
                    modelId,
                    (int)response.StatusCode,
                    $"OpenRouter video poll response for job {jobId} was not a JSON object.");
            }

            var status = GetStringProperty(root, "status");
            if (string.IsNullOrWhiteSpace(status))
            {
                throw new OpenRouterModelException(
                    modelId,
                    (int)response.StatusCode,
                    $"OpenRouter video poll for job {jobId} returned no status.");
            }

            decimal? costUsd = root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
                ? GetDecimalProperty(usage, "cost")
                : null;

            var errorDetail = root.TryGetProperty("error", out var error)
                ? Truncate(GetRawValue(error), 300)
                : "(no error detail)";

            return new VideoJobStatus(status, costUsd, errorDetail);
        }
    }

    /// <summary>
    /// Phase 3: downloads the generated MP4 (GET videos/{jobId}/content?index=0).
    /// The Authorization header is mandatory: the content URLs exposed by the API are
    /// not pre-signed (RF-17 AC2).
    /// </summary>
    private async Task<byte[]> DownloadVideoAsync(string jobId, string modelId, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"videos/{jobId}/content?index=0");
        AddAuthHeaders(httpRequest);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter video download failed for job {jobId}: {(int)response.StatusCode} {Truncate(errorBody, 300)}");
        }

        var videoBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (videoBytes.Length == 0)
        {
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter video download for job {jobId} returned empty content.");
        }

        return videoBytes;
    }

    private void AddAuthHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("Authorization", $"Bearer {_openRouter.ApiKey}");
        request.Headers.Add("HTTP-Referer", "https://odd-oddities.com");
        request.Headers.Add("X-Title", "Odd Oddities");
    }

    /// <summary>
    /// Same surrealistic creative direction as the image adapter, adapted for a short
    /// vertical clip (RF-17 AC4).
    /// </summary>
    private static string BuildPrompt(string prompt)
        => $"A poetic surreal short video about {prompt}. " +
           "Vertical 9:16, artistic dreamlike quality, suitable for Instagram Reels. " +
           "No text or watermarks in the video.";

    private static string? GetStringProperty(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? GetRawValue(value) : null;

    private static decimal? GetDecimalProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var parsed))
            return parsed;

        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var fromString))
            return fromString;

        return null;
    }

    /// <summary>Tolerant JSON scalar/structure to string: works for both string and object payloads.</summary>
    private static string GetRawValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => element.ToString()
        };

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private sealed class VideoJobSubmitResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("polling_url")]
        public string? PollingUrl { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private sealed record VideoJobStatus(string Status, decimal? CostUsd, string ErrorDetail);
}
