namespace CardPlatform.Authorization.Api.Tests.Infrastructure;

/// <summary>
/// The system clock, except that every instant it reads has sub-microsecond ticks, as on Linux (100 ns resolution),
/// whatever the machine the tests run on (macOS reads whole microseconds). Timers and timestamps are the system's.
/// </summary>
public sealed class SubMicrosecondTimeProvider : TimeProvider
{
    /// <summary>The ticks below one microsecond added to every instant.</summary>
    public const long ExtraTicks = 7;

    public override DateTimeOffset GetUtcNow()
    {
        DateTimeOffset now = System.GetUtcNow();
        return now.AddTicks(ExtraTicks - (now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
