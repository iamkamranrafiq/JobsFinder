using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using JobsFinder.BrowserAutomation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace JobsFinder.IntegrationTests.Applying;

/// <summary>
/// A local imitation of a Greenhouse board. The applier is given real board host names, but every connection the browser
/// makes is dialled to this server on loopback: there is no path to the real site, whatever a test does.
/// </summary>
internal sealed class FixtureSite : IAsyncDisposable
{
    public const string Host = "https://job-boards.greenhouse.io";

    private readonly WebApplication _app;
    private readonly ConcurrentQueue<Hit> _hits = new();
    private readonly ConcurrentQueue<string> _messages = new();
    private readonly ConcurrentQueue<string> _dialled = new();
    private readonly int _port;

    private FixtureSite(WebApplication app, int port)
    {
        _app = app;
        _port = port;
    }

    public sealed record Hit(string Method, string Path, string Query);

    /// <summary>Path to HTML; looked up for every request.</summary>
    public ConcurrentDictionary<string, string> Pages { get; } = new();

    /// <summary>Path to a status to answer with, for the first n requests (int.MaxValue for all).</summary>
    public ConcurrentDictionary<string, (int Status, int Times)> Failures { get; } = new();

    public ConcurrentDictionary<string, string> Redirects { get; } = new();

    public IReadOnlyList<Hit> Hits => [.. _hits];

    public IReadOnlyList<string> WebSocketMessages => [.. _messages];

    /// <summary>The host names the browser was connected for.</summary>
    public IReadOnlyList<string> DialledHosts => [.. _dialled];

    public int LoopbackPort => _port;

    public static async Task<FixtureSite> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        var certificate = CreateCertificate();
        builder.WebHost.UseKestrelCore().ConfigureKestrel(o =>
            o.Listen(IPAddress.Loopback, 0, l => l.UseHttps(certificate)));
        var app = builder.Build();
        app.UseWebSockets();

        FixtureSite? site = null;
        app.Run(context => site!.ServeAsync(context));
        await app.StartAsync();
        site = new FixtureSite(app, new Uri(app.Urls.Single()).Port);
        return site;
    }

    /// <summary>Settings that send every connection here and accept this server's certificate.</summary>
    public BrowserSettings Settings(Action? onDial = null) => new()
    {
        AcceptAnyCertificate = true,
        LoadTimeout = TimeSpan.FromSeconds(10),
        FormTimeout = TimeSpan.FromSeconds(3),
        SettleTimeout = TimeSpan.FromSeconds(2),
        ActionTimeout = TimeSpan.FromSeconds(3),
        ReactionTimeout = TimeSpan.FromSeconds(1),
        Dial = (host, ct) =>
        {
            onDial?.Invoke();
            return DialAsync(host, ct);
        },
    };

    public async Task<Socket> DialAsync(string host, CancellationToken cancellationToken)
    {
        _dialled.Enqueue(host);
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, _port, cancellationToken);
        return socket;
    }

    public void ClearHits() => _hits.Clear();

    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await _app.StopAsync(timeout.Token);
        await _app.DisposeAsync();
    }

    private async Task ServeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value!;
        // A WebSocket handshake is a GET in HTTP/1.1 and a CONNECT in HTTP/2; what is sent over it is in WebSocketMessages.
        var method = context.WebSockets.IsWebSocketRequest ? "WEBSOCKET" : context.Request.Method;
        _hits.Enqueue(new Hit(method, path, context.Request.QueryString.Value ?? string.Empty));

        if (context.WebSockets.IsWebSocketRequest)
        {
            await ReceiveAsync(await context.WebSockets.AcceptWebSocketAsync());
            return;
        }

        if (Failures.TryGetValue(path, out var failure) && Hits.Count(h => h.Path == path) <= failure.Times)
        {
            context.Response.StatusCode = failure.Status;
            return;
        }

        if (Redirects.TryGetValue(path, out var location))
        {
            context.Response.Redirect(location);
            return;
        }

        if (path == "/hang")
        {
            await Task.Delay(Timeout.Infinite, context.RequestAborted).ContinueWith(_ => { }, TaskScheduler.Default);
            return;
        }

        if (Pages.TryGetValue(path, out var body))
        {
            context.Response.ContentType = path.EndsWith(".js", StringComparison.Ordinal)
                ? "text/javascript"
                : "text/html; charset=utf-8";
            await context.Response.WriteAsync(body);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private async Task ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[1024];
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                _messages.Enqueue(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
        }
        catch (WebSocketException)
        {
            // The browser went away.
        }
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("*.greenhouse.io");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // Kestrel on Windows needs a certificate loaded from a PFX.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }
}
