using System.Net.Sockets;

namespace JobsFinder.BrowserAutomation;

/// <summary>Not bound from configuration, so configuration cannot weaken it. Only tests change the defaults.</summary>
internal sealed class BrowserSettings
{
    public Func<string, CancellationToken, Task<Socket>> Dial { get; init; } = PublicHostDialer.DialAsync;

    /// <summary>Tests only: the fixture server has a certificate of its own.</summary>
    public bool AcceptAnyCertificate { get; init; }

    public TimeSpan LoadTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How long to wait for the form after the page has loaded; the boards draw it with script.</summary>
    public TimeSpan FormTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long to let the page finish loading its parts before the network is cut.</summary>
    public TimeSpan SettleTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ActionTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a list or the next step may take to appear.</summary>
    public TimeSpan ReactionTimeout { get; init; } = TimeSpan.FromSeconds(3);
}
