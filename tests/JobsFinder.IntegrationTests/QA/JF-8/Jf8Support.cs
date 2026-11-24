using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JobsFinder.IntegrationTests.QA.JF7;

namespace JobsFinder.IntegrationTests.QA.JF8;

/// <summary>Helpers for the JF-8 QA tests: they drive the real HTTP API of a <see cref="QaHost"/>.</summary>
internal static class Jf8
{
    public static StringContent Json(string body = "{}") => new(body, Encoding.UTF8, "application/json");

    public static Task<HttpResponseMessage> PostFetchAsync(this QaHost host) =>
        host.Client.PostAsync("/api/sourcing/fetches", Json());

    public static async Task<JsonElement> GetJsonAsync(this QaHost host, string path)
    {
        var response = await host.Client.GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path} -> {(int)response.StatusCode}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static Task<JsonElement> StatusAsync(this QaHost host) => host.GetJsonAsync("/api/sourcing/status");

    /// <summary>Starts a fetch and returns its id. Fails the test unless the answer is 202 with a currentFetchId.</summary>
    public static async Task<Guid> StartAsync(this QaHost host)
    {
        var response = await host.PostFetchAsync();
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, $"POST /fetches -> {(int)response.StatusCode}: {text}");
        var id = JsonDocument.Parse(text).RootElement.GetProperty("currentFetchId").GetGuid();
        return id;
    }

    /// <summary>Waits until the status reports no running fetch.</summary>
    public static async Task<JsonElement> WaitIdleAsync(this QaHost host, int seconds = 60)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            var status = await host.StatusAsync();
            if (status.GetProperty("currentFetchId").ValueKind == JsonValueKind.Null)
            {
                return status;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"The fetch did not finish within {seconds} s.");
    }

    public static async Task<JsonElement> FetchAndWaitAsync(this QaHost host)
    {
        var id = await host.StartAsync();
        var status = await host.WaitIdleAsync();
        Assert.Equal(id, status.GetProperty("lastRun").GetProperty("id").GetGuid());
        return status;
    }

    public static async Task<JsonElement> ListAsync(this QaHost host, string query = "")
    {
        return await host.GetJsonAsync("/api/sourcing/postings" + query);
    }

    public static int RunStarts(this QaHost host) =>
        host.Apify.Requests.Count(r => r.Method == HttpMethod.Post && r.Uri.Contains("/acts/", StringComparison.Ordinal)
            && !r.Uri.Contains("/abort", StringComparison.Ordinal));

    public static int Aborts(this QaHost host) =>
        host.Apify.Requests.Count(r => r.Method == HttpMethod.Post && r.Uri.Contains("/abort", StringComparison.Ordinal));

    public static string[] Titles(this JsonElement list) =>
        [.. list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!)];
}
