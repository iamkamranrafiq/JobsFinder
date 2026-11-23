using System.Text.Json;
using System.Text.Json.Serialization;
using JobsFinder.Domain.Cv;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobsFinder.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores <see cref="CandidateProfileData"/> as one jsonb document. A value converter is used instead of
/// EF's ToJson mapping because the profile is an immutable record graph with nested collections.
/// </summary>
public static class CandidateProfileDataJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ValueConverter<CandidateProfileData, string> Converter { get; } =
        new(data => Serialize(data), json => Deserialize(json));

    /// <summary>Profiles are immutable, so comparing their serialized form is enough for change tracking.</summary>
    public static ValueComparer<CandidateProfileData> Comparer { get; } = new(
        (a, b) => Serialize(a) == Serialize(b),
        data => Serialize(data).GetHashCode(StringComparison.Ordinal),
        data => Deserialize(Serialize(data)));

    public static string Serialize(CandidateProfileData? data) => JsonSerializer.Serialize(data, Options);

    public static CandidateProfileData Deserialize(string json) =>
        JsonSerializer.Deserialize<CandidateProfileData>(json, Options)
        ?? throw new JsonException("Stored candidate profile is null.");
}
