using System.Text.Json;
using System.Text.Json.Serialization;
using JobsFinder.Domain.Applying;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobsFinder.Infrastructure.Persistence.Converters;

/// <summary>Stores the field results of a run as one jsonb document; they are only ever read together.</summary>
public static class ApplyFieldResultsJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ValueConverter<IReadOnlyList<ApplyFieldResult>, string> Converter { get; } =
        new(fields => Serialize(fields), json => Deserialize(json));

    /// <summary>Results are immutable, so comparing the items is enough for change tracking.</summary>
    public static ValueComparer<IReadOnlyList<ApplyFieldResult>> Comparer { get; } = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        fields => fields.Aggregate(0, (hash, field) => HashCode.Combine(hash, field)),
        fields => fields.ToList());

    public static string Serialize(IReadOnlyList<ApplyFieldResult> fields) => JsonSerializer.Serialize(fields, Options);

    public static IReadOnlyList<ApplyFieldResult> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<ApplyFieldResult>>(json, Options)
        ?? throw new JsonException("Stored field results are null.");
}
