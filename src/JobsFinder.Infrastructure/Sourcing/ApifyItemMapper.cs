using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobsFinder.Application.Sourcing;
using JobsFinder.Domain.Sourcing;

namespace JobsFinder.Infrastructure.Sourcing;

/// <summary>Maps one actor output item to a <see cref="SourcedPosting"/> using the configured field paths.</summary>
internal static partial class ApifyItemMapper
{
    // Larger epoch values are milliseconds; in seconds this is the year 5138.
    private const long MaxEpochSeconds = 100_000_000_000;

    /// <summary>
    /// Returns null for a malformed item: not an object, a required value missing, an unreadable date or undecodable text.
    /// </summary>
    public static SourcedPosting? Map(JsonElement item, ApifyFieldOptions fields)
    {
        try
        {
            return MapCore(item, fields);
        }
        catch (InvalidOperationException)
        {
            // System.Text.Json refuses to decode an escaped lone surrogate in a property name or a value.
            return null;
        }
    }

    private static SourcedPosting? MapCore(JsonElement item, ApifyFieldOptions fields)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var externalId = ReadText(item, fields.ExternalId);
        var company = ReadText(item, fields.Company);
        var title = ReadText(item, fields.Title);
        var description = ReadText(item, fields.Description);
        if (externalId is null || company is null || title is null || description is null)
        {
            return null;
        }

        if (!TryReadDate(item, fields.PostedAt, out var postedAt))
        {
            return null;
        }

        var location = ReadText(item, fields.Location);
        return new SourcedPosting(
            externalId,
            company,
            title,
            location,
            ReadRemote(item, fields.Remote, location),
            description,
            ReadText(item, fields.ApplyUrl),
            postedAt);
    }

    private static string? ReadText(JsonElement item, string? path) =>
        Find(item, path) switch
        {
            { ValueKind: JsonValueKind.String } value => PostingText.CleanOrNull(value.GetString()),
            { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
            _ => null,
        };

    private static bool ReadRemote(JsonElement item, string? path, string? location) =>
        Find(item, path) switch
        {
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            { ValueKind: JsonValueKind.String } value when PostingText.CleanOrNull(value.GetString()) is { } text =>
                RemoteFlagPattern().IsMatch(text),
            _ => location is not null && RemoteWordPattern().IsMatch(location),
        };

    /// <summary>False only when a date is present but unreadable. A missing date is allowed.</summary>
    private static bool TryReadDate(JsonElement item, string? path, out DateTimeOffset? date)
    {
        date = null;
        switch (Find(item, path))
        {
            case null or { ValueKind: JsonValueKind.Null }:
                return true;

            case { ValueKind: JsonValueKind.String } value:
                if (PostingText.CleanOrNull(value.GetString()) is not { } text)
                {
                    return true;
                }

                if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    return false;
                }

                date = parsed;
                return true;

            case { ValueKind: JsonValueKind.Number } value when value.TryGetInt64(out var epoch):
                try
                {
                    date = epoch is > MaxEpochSeconds or < -MaxEpochSeconds
                        ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                        : DateTimeOffset.FromUnixTimeSeconds(epoch);
                    return true;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }

            default:
                return false;
        }
    }

    private static JsonElement? Find(JsonElement item, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var current = item;
        foreach (var segment in path.Trim().Split('.'))
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out var property))
            {
                current = property;
            }
            else if (current.ValueKind == JsonValueKind.Array
                && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index < current.GetArrayLength())
            {
                current = current[index];
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    [GeneratedRegex(@"^(true|yes|remote|fully remote)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RemoteFlagPattern();

    [GeneratedRegex(@"\bremote\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RemoteWordPattern();
}
