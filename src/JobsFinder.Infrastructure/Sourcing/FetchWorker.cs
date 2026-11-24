using System.Threading.Channels;
using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Sourcing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JobsFinder.Infrastructure.Sourcing;

/// <summary>
/// Runs the started fetch off the request thread (F0.2-AC1). On a graceful stop the fetch is cancelled and stored as
/// interrupted. A crash leaves no record: the durable queue is F1.3.
/// </summary>
internal sealed partial class FetchWorker(
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    ILogger<FetchWorker> logger) : BackgroundService, IFetchRunner
{
    private const string UnknownSource = "unknown";

    private static readonly TimeSpan RecordTimeout = TimeSpan.FromSeconds(5);

    private readonly Channel<Guid> _requests = Channel.CreateBounded<Guid>(1);
    private readonly Lock _lock = new();
    private Guid? _current;

    public Guid? CurrentFetchId
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public bool TryStart(Guid fetchId)
    {
        lock (_lock)
        {
            if (_current is not null || !_requests.Writer.TryWrite(fetchId))
            {
                return false;
            }

            _current = fetchId;
            return true;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _requests.Reader.WaitToReadAsync(stoppingToken))
            {
                await RunNextAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Started just before the stop: it ends as interrupted.
            await RunNextAsync(stoppingToken);
        }
    }

    private async Task RunNextAsync(CancellationToken stoppingToken)
    {
        if (!_requests.Reader.TryRead(out var fetchId))
        {
            return;
        }

        try
        {
            await RunAsync(fetchId, stoppingToken);
        }
        finally
        {
            lock (_lock)
            {
                _current = null;
            }
        }
    }

    private async Task RunAsync(Guid fetchId, CancellationToken stoppingToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        try
        {
            stoppingToken.ThrowIfCancellationRequested();
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<FetchJobPostingsHandler>().HandleAsync(fetchId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogInterrupted(logger, fetchId);
            await RecordFailureAsync(fetchId, startedAt, FetchFailureReason.Interrupted);
        }
        catch (Exception e)
        {
            LogUnexpected(logger, e, fetchId);
            await RecordFailureAsync(fetchId, startedAt, FetchFailureReason.Unexpected);
        }
    }

    /// <summary>In a new scope, because the failed one may hold unsaved changes. Never throws.</summary>
    private async Task RecordFailureAsync(Guid fetchId, DateTimeOffset startedAt, FetchFailureReason reason)
    {
        using var timeout = new CancellationTokenSource(RecordTimeout);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<IFetchRunRepository>().Add(
                FetchRun.Failed(SourceName(services), startedAt, timeProvider.GetUtcNow(), reason, fetchId));
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(timeout.Token);
        }
        catch (Exception e)
        {
            LogRecordFailed(logger, e, fetchId);
        }
    }

    private static string SourceName(IServiceProvider services)
    {
        try
        {
            return services.GetRequiredService<IJobSource>().Name;
        }
        catch (Exception)
        {
            // The source itself cannot be created (bad configuration); that error is already logged.
            return UnknownSource;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job fetch {FetchId} was interrupted because the application is stopping")]
    private static partial void LogInterrupted(ILogger logger, Guid fetchId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job fetch {FetchId} failed unexpectedly")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, Guid fetchId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The outcome of job fetch {FetchId} could not be stored")]
    private static partial void LogRecordFailed(ILogger logger, Exception exception, Guid fetchId);
}
