using Microsoft.Playwright;

namespace JobsFinder.BrowserAutomation;

/// <summary>Everything one session holds. The gate closes last, so the browser is never without it.</summary>
internal sealed class BrowserResources(EgressGate gate, SemaphoreSlim slot) : IAsyncDisposable
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);

    private int _disposed;

    public EgressGate Gate => gate;

    /// <summary>Set once, when the gate is sealed. Nothing may be filled in before.</summary>
    public bool IsSealed { get; set; }

    public IPlaywright? Playwright { get; set; }

    public IBrowser? Browser { get; set; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            // Also on the way out of a failed load: from now on the browser is only being closed.
            gate.Seal();
            if (Browser is not null)
            {
                await Browser.CloseAsync().WaitAsync(CloseTimeout);
            }
        }
        catch (Exception e) when (e is PlaywrightException or TimeoutException)
        {
            // Disposing Playwright below ends its process, and the browser with it.
        }
        finally
        {
            Playwright?.Dispose();
            await gate.DisposeAsync();
            slot.Release();
        }
    }
}

internal static class Scripts
{
    public static string Load(string name)
    {
        using var stream = typeof(Scripts).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Script '{name}' is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
