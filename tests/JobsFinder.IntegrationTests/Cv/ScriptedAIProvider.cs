using System.Collections.Concurrent;
using System.Text.Json;
using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Cv;

namespace JobsFinder.IntegrationTests.Cv;

/// <summary>
/// Test double for the model: returns queued answers in order, then a valid default profile.
/// Never calls a real provider.
/// </summary>
public sealed class ScriptedAIProvider : IAIProvider
{
    private readonly ConcurrentQueue<string> _answers = new();
    private int _calls;

    public static string ValidProfileJson { get; } = JsonSerializer.Serialize(
        CandidateProfileDataDto.Empty with
        {
            Identity = new ProfileIdentityDto("Ada Lovelace", "Senior .NET Engineer"),
            Contact = new ProfileContactDto("ada@example.com", null, "London, UK"),
            CurrentEmployer = "Analytical Engines Ltd",
        },
        CandidateProfileSchema.SerializerOptions);

    public int Calls => _calls;

    /// <summary>Clears queued answers and the call count.</summary>
    public void Reset()
    {
        _answers.Clear();
        Interlocked.Exchange(ref _calls, 0);
    }

    public void Enqueue(params string[] answers)
    {
        foreach (var answer in answers)
        {
            _answers.Enqueue(answer);
        }
    }

    public Task<string> CompleteJsonAsync(AIJsonRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(_answers.TryDequeue(out var answer) ? answer : ValidProfileJson);
    }
}
