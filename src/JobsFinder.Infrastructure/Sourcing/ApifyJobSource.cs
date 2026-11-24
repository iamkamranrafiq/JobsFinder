using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Sourcing;
using Microsoft.Extensions.Logging;

namespace JobsFinder.Infrastructure.Sourcing;

/// <summary>
/// Runs the configured Apify actor and reads its dataset (Apify API v2). The token travels only in the
/// Authorization header: never in a URL, log or exception. No user credentials or cookies are sent (F0.2-BR4).
/// </summary>
internal sealed partial class ApifyJobSource(
    HttpClient http,
    ApifyOptions options,
    TimeProvider timeProvider,
    ILogger<ApifyJobSource> logger) : IJobSource
{
    public const string SourceName = "apify";

    /// <summary>Apify holds a status request open for at most this long.</summary>
    private const int MaxWaitForFinishSeconds = 30;

    private const int MaxRunTimeoutSeconds = 3600;

    /// <summary>Pause between status requests. Settable so tests run fast.</summary>
    internal TimeSpan PollDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Time for Apify to report its own timeout before we give up on the run.</summary>
    internal TimeSpan DeadlineGrace { get; init; } = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan AbortTimeout = TimeSpan.FromSeconds(5);

    public string Name => SourceName;

    public async Task<JobSourceResult> FetchAsync(CancellationToken cancellationToken)
    {
        var (token, actorId, input) = ReadConfiguration();

        var run = await StartRunAsync(token, actorId, input, cancellationToken);
        var datasetId = await WaitForDatasetAsync(token, run, cancellationToken);
        return await ReadItemsAsync(token, datasetId, cancellationToken);
    }

    private (string Token, string ActorId, string Input) ReadConfiguration()
    {
        // Header values must be printable ASCII; HttpClient would otherwise throw with the value in the message.
        var token = options.Token?.Trim();
        if (string.IsNullOrEmpty(token) || token.Any(c => c is <= ' ' or > '~'))
        {
            throw NotConfigured("Apify:Token is missing or malformed.");
        }

        if (string.IsNullOrWhiteSpace(options.ActorId))
        {
            throw NotConfigured("Apify:ActorId is not set.");
        }

        if (!options.Fields.HasRequiredPaths)
        {
            throw NotConfigured("Apify:Fields must map ExternalId, Company, Title, Description and ApplyUrl.");
        }

        if (options.BaseUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw NotConfigured("Apify:BaseUrl must use https, so the token is never sent in clear text.");
        }

        if (options.RunTimeoutSeconds is < 1 or > MaxRunTimeoutSeconds || options.MaxItems < 1)
        {
            throw NotConfigured("Apify:RunTimeoutSeconds (1 to 3600) or Apify:MaxItems (at least 1) is out of range.");
        }

        var input = ApifyInput.Validate(options.InputJson)
            ?? throw NotConfigured("Apify:InputJson must be a JSON object without credentials or cookies.");

        return (token, options.ActorId.Trim().Replace('/', '~'), input);
    }

    private async Task<ActorRun> StartRunAsync(string token, string actorId, string input, CancellationToken cancellationToken)
    {
        var path = $"v2/acts/{Uri.EscapeDataString(actorId)}/runs?timeout={options.RunTimeoutSeconds}";
        using var request = CreateRequest(HttpMethod.Post, path, token);
        request.Content = new StringContent(input, Encoding.UTF8, "application/json");

        using var document = await SendAsync(request, isStart: true, cancellationToken);
        return ReadRun(document.RootElement);
    }

    private async Task<string> WaitForDatasetAsync(string token, ActorRun run, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(
            TimeSpan.FromSeconds(options.RunTimeoutSeconds) + DeadlineGrace, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            while (run.Status is "READY" or "RUNNING" or "TIMING-OUT" or "ABORTING")
            {
                await Task.Delay(PollDelay, timeProvider, linked.Token);
                run = await PollAsync(token, run.Id, linked.Token);
            }
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            await TryAbortAsync(token, run.Id);
            throw new JobSourceException(FetchFailureReason.TimedOut, "The Apify run did not finish in time.", e);
        }
        catch (Exception e) when (e is JobSourceException or OperationCanceledException)
        {
            // A poll failed or the caller cancelled (e.g. shutdown): nobody will read the result of this run.
            await TryAbortAsync(token, run.Id);
            throw;
        }

        return run.Status switch
        {
            "SUCCEEDED" when !string.IsNullOrEmpty(run.DatasetId) => run.DatasetId,
            "SUCCEEDED" => throw Unreadable(),
            "TIMED-OUT" => throw new JobSourceException(FetchFailureReason.TimedOut, "The Apify run timed out."),
            _ => throw new JobSourceException(FetchFailureReason.ActorFailed, "The Apify run did not succeed."),
        };
    }

    private async Task<ActorRun> PollAsync(string token, string runId, CancellationToken cancellationToken)
    {
        var wait = Math.Min(MaxWaitForFinishSeconds, options.RunTimeoutSeconds);
        using var request = CreateRequest(
            HttpMethod.Get, $"v2/actor-runs/{Uri.EscapeDataString(runId)}?waitForFinish={wait}", token);

        using var document = await SendAsync(request, isStart: false, cancellationToken);
        return ReadRun(document.RootElement);
    }

    private async Task<JobSourceResult> ReadItemsAsync(string token, string datasetId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"v2/datasets/{Uri.EscapeDataString(datasetId)}/items?format=json&clean=true&limit={options.MaxItems}",
            token);

        using var document = await SendAsync(request, isStart: false, cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw Unreadable();
        }

        var postings = new List<SourcedPosting>();
        var malformed = 0;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (ApifyItemMapper.Map(item, options.Fields) is { } posting)
            {
                postings.Add(posting);
            }
            else
            {
                malformed++;
            }
        }

        return new JobSourceResult(postings, malformed);
    }

    /// <summary>Best effort, so a run we gave up on stops using credit. Never throws: the caller reports why.</summary>
    private async Task TryAbortAsync(string token, string runId)
    {
        using var timeout = new CancellationTokenSource(AbortTimeout);
        try
        {
            using var request = CreateRequest(HttpMethod.Post, $"v2/actor-runs/{Uri.EscapeDataString(runId)}/abort", token);
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                LogAbortFailed(logger, runId);
            }
        }
        catch (Exception)
        {
            LogAbortFailed(logger, runId);
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, bool isStart, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogHttpError(logger, (int)response.StatusCode);
                throw new JobSourceException(
                    ToReason(response.StatusCode, isStart),
                    $"Apify returned HTTP {(int)response.StatusCode}.");
            }

            return await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            throw new JobSourceException(FetchFailureReason.Unreachable, "Apify could not be reached.", e);
        }
        catch (JsonException e)
        {
            throw Unreadable(e);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout elapsed (the caller did not cancel).
            throw new JobSourceException(FetchFailureReason.Unreachable, "Apify did not answer in time.", e);
        }
    }

    private static FetchFailureReason ToReason(HttpStatusCode status, bool isStart) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => FetchFailureReason.Unauthorized,
        // Starting a run: the actor does not exist or rejects the configured input.
        HttpStatusCode.NotFound or HttpStatusCode.BadRequest when isStart => FetchFailureReason.NotConfigured,
        _ => FetchFailureReason.Unreachable,
    };

    private static ActorRun ReadRun(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && ReadString(data, "id") is { Length: > 0 } id
            && ReadString(data, "status") is { Length: > 0 } status)
        {
            return new ActorRun(id, status, ReadString(data, "defaultDatasetId"));
        }

        throw Unreadable();
    }

    private static string? ReadString(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static JobSourceException NotConfigured(string message) => new(FetchFailureReason.NotConfigured, message);

    private static JobSourceException Unreadable(Exception? inner = null) =>
        new(FetchFailureReason.Unreachable, "Apify returned an unreadable answer.", inner);

    private sealed record ActorRun(string Id, string Status, string? DatasetId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Apify API returned HTTP {StatusCode}")]
    private static partial void LogHttpError(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Apify run {RunId} could not be aborted")]
    private static partial void LogAbortFailed(ILogger logger, string runId);
}
