using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Sourcing;
using JobsFinder.Infrastructure;
using JobsFinder.Infrastructure.Sourcing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobsFinder.IntegrationTests.Sourcing;

/// <summary>The Apify adapter, tested with scripted HTTP and a made-up token. No network or Docker needed.</summary>
public sealed class ApifyJobSourceTests : IDisposable
{
    private const string FakeToken = "fake-token-for-tests";

    private const string StartedRun = """{"data":{"id":"run1","status":"RUNNING","defaultDatasetId":"ds1"}}""";
    private const string SucceededRun = """{"data":{"id":"run1","status":"SUCCEEDED","defaultDatasetId":"ds1"}}""";

    private const string Items = """
        [
          {"id": 42, "company": {"name": "Acme"}, "title": "Engineer", "location": "Remote - UK",
           "content": "<p>Build</p>", "url": "https://boards.greenhouse.io/acme/jobs/42", "updated": "2026-11-20T10:00:00+05:00"},
          "not an object",
          {"id": 43, "title": "No company", "content": "x"}
        ]
        """;

    private readonly ScriptedHandler _http = new();
    private readonly CapturingLogger _logger = new();

    public void Dispose() => _http.Dispose();

    private static ApifyOptions Options(Action<ApifyOptions>? change = null)
    {
        var options = new ApifyOptions
        {
            Token = FakeToken,
            ActorId = "someone/some-actor",
            InputJson = """{"boardToken":"acme"}""",
            Fields = Fields(),
        };
        change?.Invoke(options);
        return options;
    }

    private static ApifyFieldOptions Fields() => new()
    {
        ExternalId = "id",
        Company = "company.name",
        Title = "title",
        Description = "content",
        ApplyUrl = "url",
        Location = "location",
        PostedAt = "updated",
    };

    private ApifyJobSource CreateSut(ApifyOptions options, TimeSpan? grace = null) =>
        new(new HttpClient(_http) { BaseAddress = options.BaseUrl }, options, TimeProvider.System, _logger)
        {
            PollDelay = TimeSpan.FromMilliseconds(5),
            DeadlineGrace = grace ?? TimeSpan.FromSeconds(5),
        };

    private async Task<JobSourceException> FailsAsync(ApifyOptions options, TimeSpan? grace = null) =>
        await Assert.ThrowsAsync<JobSourceException>(() => CreateSut(options, grace).FetchAsync(CancellationToken.None));

    [Fact]
    public async Task FetchAsync_SuccessfulRun_StartsPollsAndMapsItems_F0_2_BR2()
    {
        _http.Returns(StartedRun).Returns(StartedRun).Returns(SucceededRun).Returns(Items);

        var result = await CreateSut(Options()).FetchAsync(CancellationToken.None);

        var posting = Assert.Single(result.Postings);
        Assert.Equal(("42", "Acme", "Engineer", "Remote - UK", true), (posting.ExternalId, posting.Company, posting.Title, posting.Location, posting.IsRemote));
        Assert.Equal(2, result.MalformedCount);
        Assert.Equal(
            [
                "POST https://api.apify.com/v2/acts/someone~some-actor/runs?timeout=300",
                "GET https://api.apify.com/v2/actor-runs/run1?waitForFinish=30",
                "GET https://api.apify.com/v2/actor-runs/run1?waitForFinish=30",
                "GET https://api.apify.com/v2/datasets/ds1/items?format=json&clean=true&limit=500",
            ],
            _http.Requests.Select(r => r.Line));
        Assert.Equal("""{"boardToken":"acme"}""", _http.Requests[0].Body);
    }

    public static TheoryData<string> IncompleteConfigurations => new("no token", "blank token", "token with newline", "no actor", "no mapping", "partial mapping", "http", "bad limit", "bad timeout");

    [Theory]
    [MemberData(nameof(IncompleteConfigurations))]
    public async Task FetchAsync_NotConfigured_ThrowsWithoutHttpCall_F0_2_AC4(string problem)
    {
        var options = Options(o =>
        {
            switch (problem)
            {
                case "no token": o.Token = null; break;
                case "blank token": o.Token = "  "; break;
                case "token with newline": o.Token = $"{FakeToken}\nX-Evil: 1"; break;
                case "no actor": o.ActorId = ""; break;
                case "no mapping": o.Fields = new ApifyFieldOptions(); break;
                case "partial mapping": o.Fields.ApplyUrl = null; break;
                case "http": o.BaseUrl = new Uri("http://api.apify.com/"); break;
                case "bad limit": o.MaxItems = 0; break;
                case "bad timeout": o.RunTimeoutSeconds = int.MaxValue; break;
                default: throw new InvalidOperationException(problem);
            }
        });

        var error = await FailsAsync(options);

        Assert.Equal(FetchFailureReason.NotConfigured, error.Reason);
        Assert.Empty(_http.Requests);
        Assert.DoesNotContain(FakeToken, error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, FetchFailureReason.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, FetchFailureReason.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, FetchFailureReason.NotConfigured)]
    [InlineData(HttpStatusCode.BadRequest, FetchFailureReason.NotConfigured)]
    [InlineData(HttpStatusCode.PaymentRequired, FetchFailureReason.Unreachable)]
    [InlineData(HttpStatusCode.TooManyRequests, FetchFailureReason.Unreachable)]
    [InlineData(HttpStatusCode.InternalServerError, FetchFailureReason.Unreachable)]
    public async Task FetchAsync_StartRejected_ThrowsReason_F0_2_AC4(HttpStatusCode status, FetchFailureReason expected)
    {
        _http.Returns("""{"error":{"type":"x"}}""", status);

        Assert.Equal(expected, (await FailsAsync(Options())).Reason);
    }

    [Theory]
    [InlineData("FAILED", FetchFailureReason.ActorFailed)]
    [InlineData("ABORTED", FetchFailureReason.ActorFailed)]
    [InlineData("TIMED-OUT", FetchFailureReason.TimedOut)]
    public async Task FetchAsync_RunEndsBadly_ThrowsAndReadsNoItems_F0_2_AC4(string status, FetchFailureReason expected)
    {
        _http.Returns(StartedRun).Returns(SucceededRun.Replace("SUCCEEDED", status, StringComparison.Ordinal));

        Assert.Equal(expected, (await FailsAsync(Options())).Reason);
        Assert.DoesNotContain(_http.Requests, r => r.Line.Contains("/datasets/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_DeadlineReached_AbortsRunAndThrowsTimedOut_F0_2_AC4()
    {
        _http.Returns(StartedRun).Always(StartedRun);

        var error = await FailsAsync(Options(o => o.RunTimeoutSeconds = 1), grace: TimeSpan.Zero);

        Assert.Equal(FetchFailureReason.TimedOut, error.Reason);
        Assert.Equal("POST https://api.apify.com/v2/actor-runs/run1/abort", _http.Requests[^1].Line);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"data":{"id":"run1"}}""")]
    public async Task FetchAsync_UnreadableRun_ThrowsUnreachable_F0_2_AC4(string body)
    {
        _http.Returns(body);

        Assert.Equal(FetchFailureReason.Unreachable, (await FailsAsync(Options())).Reason);
    }

    [Fact]
    public async Task FetchAsync_DatasetIsNotAnArray_ThrowsUnreachable_F0_2_AC4()
    {
        _http.Returns(SucceededRun).Returns("""{"error":"x"}""");

        Assert.Equal(FetchFailureReason.Unreachable, (await FailsAsync(Options())).Reason);
    }

    [Fact]
    public async Task FetchAsync_NetworkError_ThrowsUnreachable_F0_2_AC4()
    {
        _http.Throws(new HttpRequestException("no route"));

        Assert.Equal(FetchFailureReason.Unreachable, (await FailsAsync(Options())).Reason);
    }

    [Fact]
    public async Task FetchAsync_CallerCancels_PropagatesCancellation_F0_2_AC4()
    {
        _http.Returns(StartedRun).Always(StartedRun);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateSut(Options()).FetchAsync(cancellation.Token));
    }

    [Fact]
    public async Task FetchAsync_TokenOnlyInAuthorizationHeader_NotInUrlBodyLogsOrExceptions_F0_2_BR1()
    {
        _http.Returns(StartedRun).Returns("""{"error":"x"}""", HttpStatusCode.Unauthorized);

        var error = await FailsAsync(Options());

        Assert.All(_http.Requests, r =>
        {
            Assert.Equal($"Bearer {FakeToken}", r.Authorization);
            Assert.DoesNotContain(FakeToken, r.Line + r.Body, StringComparison.Ordinal);
        });
        Assert.NotEmpty(_logger.Messages);
        Assert.DoesNotContain(_logger.Messages, m => m.Contains(FakeToken, StringComparison.Ordinal));
        Assert.DoesNotContain(FakeToken, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAsync_SendsNoCookies_F0_2_BR4()
    {
        _http.Returns(StartedRun).Returns(SucceededRun).Returns(Items);

        await CreateSut(Options()).FetchAsync(CancellationToken.None);

        Assert.All(_http.Requests, r => Assert.False(r.HasCookie));
        Assert.False(RegisteredPrimaryHandler().UseCookies);
    }

    [Theory]
    [InlineData("""{"cookies":[{"name":"li_at","value":"x"}]}""")]
    [InlineData("""{"nested":{"list":[{"sessionCookie":"x"}]}}""")]
    [InlineData("""{"userName":"x","board":"acme"}""")]
    [InlineData("""{"login_password":"x"}""")]
    [InlineData("""{"li_at":"x"}""")]
    [InlineData("""{"headers":{"Authorization":"x"}}""")]
    [InlineData("""{"credentials":{}}""")]
    [InlineData("[1]")]
    [InlineData("not json")]
    public async Task FetchAsync_InputWithCredentialsOrNotAnObject_Refused_F0_2_BR4(string input)
    {
        var error = await FailsAsync(Options(o => o.InputJson = input));

        Assert.Equal(FetchFailureReason.NotConfigured, error.Reason);
        Assert.Empty(_http.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{"boardToken":"acme","sessionPoolName":"p","proxyConfiguration":{"useApifyProxy":true}}""")]
    public async Task FetchAsync_OrdinaryInput_Allowed_F0_2_BR4(string? input)
    {
        _http.Returns(SucceededRun).Returns("[]");

        await CreateSut(Options(o => o.InputJson = input)).FetchAsync(CancellationToken.None);

        Assert.Equal(input ?? "{}", _http.Requests[0].Body);
    }

    [Fact]
    public void Configuration_TokenFromEnvironmentStyleKey_IsBound_F0_2_BR1()
    {
        using var provider = BuildProvider(("Apify:Token", FakeToken));

        Assert.Equal(FakeToken, provider.GetRequiredService<ApifyOptions>().Token);
        Assert.IsType<ApifyJobSource>(provider.GetRequiredService<IJobSource>());
    }

    [Fact]
    public void Configuration_NoApifySection_AppStillResolvesSource_F0_2_AC4()
    {
        using var provider = BuildProvider();

        Assert.IsType<ApifyJobSource>(provider.GetRequiredService<IJobSource>());
        Assert.Null(provider.GetRequiredService<ApifyOptions>().Token);
    }

    [Fact]
    public void Api_HasUserSecretsId_F0_2_BR1() =>
        Assert.NotNull(typeof(Program).Assembly.GetCustomAttribute<UserSecretsIdAttribute>());

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void CommittedSettings_ContainNoApifyToken_F0_2_BR1(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JobsFinder.slnx")))
        {
            dir = dir.Parent;
        }

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "src", "JobsFinder.Api", file)));
        var hasToken = settings.RootElement.TryGetProperty("Apify", out var apify)
            && apify.EnumerateObject().Any(p => p.Name.Contains("token", StringComparison.OrdinalIgnoreCase));

        Assert.False(hasToken);
    }

    private static ServiceProvider BuildProvider(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings
                .Append((Key: "ConnectionStrings:JobsFinder", Value: "Host=unused"))
                .Select(s => KeyValuePair.Create(s.Key, (string?)s.Value)))
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
    }

    private static SocketsHttpHandler RegisteredPrimaryHandler()
    {
        using var provider = BuildProvider();
        HttpMessageHandler handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(IJobSource));
        while (handler is DelegatingHandler { InnerHandler: { } inner })
        {
            handler = inner;
        }

        return Assert.IsType<SocketsHttpHandler>(handler);
    }

    private sealed record SentRequest(string Line, string Body, string? Authorization, bool HasCookie);

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        private Func<HttpResponseMessage>? _always;

        public List<SentRequest> Requests { get; } = [];

        public ScriptedHandler Returns(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _responses.Enqueue(() => Response(body, status));
            return this;
        }

        public void Always(string body) => _always = () => Response(body, HttpStatusCode.OK);

        public void Throws(Exception exception) => _responses.Enqueue(() => throw exception);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new SentRequest(
                $"{request.Method} {request.RequestUri}",
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString(),
                request.Headers.Contains("Cookie")));

            return _responses.TryDequeue(out var next)
                ? next()
                : _always?.Invoke() ?? throw new InvalidOperationException("No scripted response left.");
        }

        private static HttpResponseMessage Response(string body, HttpStatusCode status) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class CapturingLogger : ILogger<ApifyJobSource>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add($"{formatter(state, exception)} {exception}");
    }
}

public sealed class ApifyItemMapperTests
{
    private static readonly ApifyFieldOptions Fields = new()
    {
        ExternalId = "id",
        Company = "org.name",
        Title = "title",
        Description = "text",
        ApplyUrl = "links.0.href",
        Location = "where",
        Remote = "remote",
        PostedAt = "date",
    };

    private static SourcedPosting? Map(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ApifyItemMapper.Map(document.RootElement, Fields);
    }

    private static string Item(string extra = "") =>
        $$"""{"id":"a1","org":{"name":"Acme"},"title":"Engineer","text":"Build"{{extra}}}""";

    [Fact]
    public void Map_ConfiguredPaths_F0_2_BR2()
    {
        var posting = Map(Item(""","links":[{"href":"https://boards.greenhouse.io/a/jobs/1"}],"where":"London","remote":true,"date":"2026-11-20" """))!;

        Assert.Equal(
            new SourcedPosting(
                "a1", "Acme", "Engineer", "London", true, "Build", "https://boards.greenhouse.io/a/jobs/1",
                new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero)),
            posting);
    }

    [Fact]
    public void Map_OptionalValuesMissing_ReturnsPosting_F0_2_BR2()
    {
        var posting = Map(Item())!;

        Assert.Null(posting.Location);
        Assert.Null(posting.ApplyLink);
        Assert.Null(posting.PostedAt);
        Assert.False(posting.IsRemote);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"org":{"name":"Acme"},"title":"Engineer","text":"Build"}""")]
    [InlineData("""{"id":"a1","org":"Acme","title":"Engineer","text":"Build"}""")]
    [InlineData("""{"id":"a1","org":{"name":"Acme"},"title":" ","text":"Build"}""")]
    [InlineData("""{"id":"a1","org":{"name":"Acme"},"title":"Engineer","text":null}""")]
    [InlineData("""{"id":"a1","org":{"name":"Acme"},"title":"Engineer","text":"Build","date":"soon"}""")]
    [InlineData("""{"id":"a1","org":{"name":"Acme"},"title":"Engineer","text":"Build","date":9223372036854775807}""")]
    [InlineData("""{"id":"a1","org":{"name":"Acme"},"title":"Engineer","text":"Build","date":-9223372036854775808}""")]
    [InlineData("""{"id":"a1","org":{"name":"Acme"},"title":"Engineer","text":"Build","date":{}}""")]
    [InlineData("""{"id":"a1","org":{"name":"Acme"},"title":"\ud800","text":"Build"}""")]
    public void Map_MalformedItem_ReturnsNull_F0_2_AC2(string json) => Assert.Null(Map(json));

    [Fact]
    public void Map_PropertyNameWithLoneSurrogate_ReturnsNull_F0_2_AC2() =>
        Assert.Null(Map(Item(""","\ud800":1""")));

    [Fact]
    public void Map_NullCharacter_Stripped_F0_2_AC2() =>
        Assert.Equal("Engineer", Map("""{"id":"a1","org":{"name":"Acme"},"title":"Eng\u0000ineer","text":"Build"}""")!.Title);

    [Theory]
    [InlineData(""","remote":"Yes" """, true)]
    [InlineData(""","remote":"Fully remote" """, true)]
    [InlineData(""","remote":"hybrid","where":"Remote" """, false)]
    [InlineData(""","remote":false,"where":"Remote" """, false)]
    [InlineData(""","where":"Remote - Europe" """, true)]
    [InlineData(""","where":"Remoteville" """, false)]
    public void Map_RemoteFlag_F0_2_BR2(string extra, bool expected) => Assert.Equal(expected, Map(Item(extra))!.IsRemote);

    [Theory]
    [InlineData("1763632800")]
    [InlineData("1763632800000")]
    [InlineData("\"2025-11-20T15:00:00+05:00\"")]
    public void Map_DateFormats_F0_2_BR2(string date) =>
        Assert.Equal(new DateTimeOffset(2025, 11, 20, 10, 0, 0, TimeSpan.Zero), Map(Item($",\"date\":{date}"))!.PostedAt);
}
