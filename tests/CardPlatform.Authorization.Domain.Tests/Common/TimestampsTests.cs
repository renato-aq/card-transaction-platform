using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Domain.Tests.Common;

public class TimestampsTests
{
    private static readonly DateTimeOffset WholeMicrosecond =
        new DateTimeOffset(2026, 11, 25, 20, 33, 45, TimeSpan.Zero).AddTicks(6_168_890);

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void Truncate_to_microseconds_drops_the_sub_microsecond_ticks(int ticks)
    {
        DateTimeOffset truncated = WholeMicrosecond.AddTicks(ticks).TruncateToMicroseconds();

        Assert.Equal(WholeMicrosecond, truncated);
    }

    [Fact]
    public void Truncate_to_microseconds_keeps_a_whole_microsecond_unchanged()
    {
        Assert.Equal(WholeMicrosecond, WholeMicrosecond.TruncateToMicroseconds());
    }

    [Fact]
    public void Truncate_to_microseconds_keeps_the_offset()
    {
        DateTimeOffset local = new DateTimeOffset(2026, 11, 25, 17, 33, 45, TimeSpan.FromHours(-3)).AddTicks(6_168_895);

        DateTimeOffset truncated = local.TruncateToMicroseconds();

        Assert.Equal(TimeSpan.FromHours(-3), truncated.Offset);
        Assert.Equal(local.AddTicks(-5), truncated);
    }
}
