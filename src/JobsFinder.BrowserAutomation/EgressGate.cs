using System.Net;
using System.Net.Sockets;
using System.Text;
using JobsFinder.Domain.Applying;

namespace JobsFinder.BrowserAutomation;

/// <summary>
/// The browser's only way out over TCP: a proxy on loopback that tunnels https to Greenhouse and nothing else. UDP does not
/// go through a proxy; it is shut off by a launch switch (see <see cref="PlaywrightAutomationService"/>), which has its own
/// test. Once sealed, the gate cuts every open connection and accepts no new one, for good. It sits outside the browser, so it holds whatever a page does;
/// Chromium's own offline switch does not (a beacon on leaving a page, a shared worker and an open WebSocket got past it).
/// </summary>
internal sealed class EgressGate : IAsyncDisposable
{
    private const int HttpsPort = 443;
    private const int MaxRequestBytes = 8192;
    private const int MaxRefusedHosts = 100;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly byte[] Established = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");

    private readonly Func<string, CancellationToken, Task<Socket>> _dial;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _lock = new();
    private readonly HashSet<Socket> _open = [];
    private readonly HashSet<string> _refused = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Task> _serving = [];
    private readonly Task _accepting;
    private bool _sealed;

    private EgressGate(Func<string, CancellationToken, Task<Socket>> dial)
    {
        _dial = dial;
        _listener.Start();
        Endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
        _accepting = AcceptAsync();
    }

    public Uri Endpoint { get; }

    /// <summary>Hosts the page asked for and did not get. Host names only.</summary>
    public IReadOnlyCollection<string> RefusedHosts
    {
        get
        {
            lock (_lock)
            {
                return [.. _refused];
            }
        }
    }

    /// <param name="dial">Connects to an allowed host; see <see cref="PublicHostDialer"/>.</param>
    public static EgressGate Start(Func<string, CancellationToken, Task<Socket>> dial) => new(dial);

    /// <summary>Greenhouse and its subdomains, on the https port.</summary>
    public static bool IsAllowed(string host, int port) =>
        port == HttpsPort
        && (host.Equals(GreenhouseApplyUrl.AssetDomain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith($".{GreenhouseApplyUrl.AssetDomain}", StringComparison.OrdinalIgnoreCase));

    /// <summary>Cuts the browser off. When this returns, nothing it sends can leave any more.</summary>
    public void Seal()
    {
        List<Socket> open;
        lock (_lock)
        {
            _sealed = true;
            open = [.. _open];
            _open.Clear();
        }

        foreach (var socket in open)
        {
            socket.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Seal();
        await _stop.CancelAsync();
        _listener.Stop();
        await _accepting;

        // Nothing is added any more; every connection ends because its sockets and its token are gone.
        Task[] serving;
        lock (_lock)
        {
            serving = [.. _serving];
        }

        await Task.WhenAll(serving);
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            Socket browser;
            try
            {
                browser = await _listener.AcceptSocketAsync(_stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            Serve(browser);
        }
    }

    /// <summary>Runs beside the accept loop; <see cref="DisposeAsync"/> waits for it.</summary>
    private void Serve(Socket browser)
    {
        var serving = ServeAsync(browser);
        lock (_lock)
        {
            _serving.RemoveWhere(t => t.IsCompleted);
            _serving.Add(serving);
        }
    }

    /// <summary>Never throws: whatever goes wrong ends this one connection, and the browser sees a network error.</summary>
    private async Task ServeAsync(Socket browser)
    {
        Socket? upstream = null;
        try
        {
            if (!TryTrack(browser))
            {
                return;
            }

            var target = await ReadConnectTargetAsync(browser);
            if (target is not { } t)
            {
                return;
            }

            if (!IsAllowed(t.Host, t.Port))
            {
                Refuse(t.Host);
                return;
            }

            upstream = await _dial(t.Host, _stop.Token);

            // Drop, rather than deliver, what is still buffered when the gate is sealed.
            upstream.LingerState = new LingerOption(enable: true, seconds: 0);
            if (!TryTrack(upstream))
            {
                return;
            }

            await browser.SendAsync(Established, _stop.Token);
            var up = PumpAsync(browser, upstream);
            var down = PumpAsync(upstream, browser);
            await Task.WhenAny(up, down);

            // Closing both ends stops the other pump; its exception is observed here.
            Release(browser);
            Release(upstream);
            await Task.WhenAll(up, down);
        }
        catch (Exception)
        {
            // Not logged: it can hold a host name the page chose.
        }
        finally
        {
            Release(browser);
            Release(upstream);
        }
    }

    /// <summary>False once sealed: the caller must not use the socket.</summary>
    private bool TryTrack(Socket socket)
    {
        lock (_lock)
        {
            return !_sealed && _open.Add(socket);
        }
    }

    private void Release(Socket? socket)
    {
        if (socket is null)
        {
            return;
        }

        lock (_lock)
        {
            _open.Remove(socket);
        }

        socket.Dispose();
    }

    private void Refuse(string host)
    {
        lock (_lock)
        {
            if (_refused.Count < MaxRefusedHosts)
            {
                _refused.Add(host);
            }
        }
    }

    /// <summary>Only "CONNECT host:port" is understood; plain http and everything else is dropped.</summary>
    private async Task<(string Host, int Port)?> ReadConnectTargetAsync(Socket browser)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(RequestTimeout);

        var buffer = new byte[MaxRequestBytes];
        var length = 0;
        while (buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8) < 0)
        {
            var read = length == buffer.Length ? 0 : await browser.ReceiveAsync(buffer.AsMemory(length), timeout.Token);
            if (read == 0)
            {
                return null;
            }

            length += read;
        }

        var parts = Encoding.ASCII.GetString(buffer, 0, buffer.AsSpan(0, length).IndexOf((byte)'\r')).Split(' ');
        if (parts.Length != 3)
        {
            return null;
        }

        if (parts[0] != "CONNECT")
        {
            Refuse(Uri.TryCreate(parts[1], UriKind.Absolute, out var url) ? url.IdnHost : "unknown");
            return null;
        }

        var colon = parts[1].LastIndexOf(':');
        return colon > 0 && int.TryParse(parts[1].AsSpan(colon + 1), out var port)
            ? (parts[1][..colon], port)
            : null;
    }

    private async Task PumpAsync(Socket from, Socket to)
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await from.ReceiveAsync(buffer, _stop.Token);
            if (read == 0)
            {
                return;
            }

            await SendAllAsync(to, buffer.AsMemory(0, read));
        }
    }

    private async Task SendAllAsync(Socket to, ReadOnlyMemory<byte> data)
    {
        while (data.Length > 0)
        {
            data = data[await to.SendAsync(data, _stop.Token)..];
        }
    }
}
