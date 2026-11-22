namespace JobsFinder.Domain.Common;

/// <summary>
/// PostgreSQL stores timestamps with microsecond precision, while .NET clocks have 100 ns ticks.
/// Entities truncate their timestamps on creation so the in-memory value equals the value read back.
/// </summary>
public static class StoragePrecision
{
    public static DateTimeOffset ToMicroseconds(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
}
