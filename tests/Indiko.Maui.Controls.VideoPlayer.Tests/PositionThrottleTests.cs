using Xunit;
using Indiko.Maui.Controls.VideoPlayer.Timeline;

namespace Indiko.Maui.Controls.VideoPlayer.Tests;

public class PositionThrottleTests
{
    // Both platforms observe the player roughly every 1/30 s — iOS through a
    // periodic time observer, Android through a main-looper handler — and the
    // throttle is what turns that into the rate the app asked for.
    private static int EmitsInOneSecond(PositionThrottle throttle)
    {
        var emitted = 0;
        for (var tick = 1; tick <= 30; tick++)
        {
            if (throttle.ShouldEmit(TimeSpan.FromSeconds(tick / 30.0)))
                emitted++;
        }
        return emitted;
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(15, 15)]
    [InlineData(10, 10)]
    public void The_requested_rate_is_the_number_of_events_per_second(int rateHz, int expected)
    {
        Assert.Equal(expected, EmitsInOneSecond(new PositionThrottle(rateHz)));
    }

    [Fact]
    public void The_rate_is_clamped_to_the_supported_range()
    {
        Assert.Equal(10, new PositionThrottle(1).RateHz);
        Assert.Equal(30, new PositionThrottle(240).RateHz);
        Assert.Equal(15, new PositionThrottle(15).RateHz);
    }

    [Fact]
    public void The_first_observation_always_emits()
    {
        Assert.True(new PositionThrottle(10).ShouldEmit(TimeSpan.Zero));
    }

    [Fact]
    public void An_observation_inside_the_interval_is_dropped()
    {
        var throttle = new PositionThrottle(10);
        Assert.True(throttle.ShouldEmit(TimeSpan.Zero));
        Assert.False(throttle.ShouldEmit(TimeSpan.FromMilliseconds(50)));
        Assert.True(throttle.ShouldEmit(TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void A_finished_seek_emits_even_inside_the_interval()
    {
        // After a seek or a frame step the position has moved somewhere the app
        // did not ask for; waiting up to 100 ms to say so would show the map
        // playhead lagging behind the picture.
        var throttle = new PositionThrottle(10);
        Assert.True(throttle.ShouldEmit(TimeSpan.Zero));
        Assert.False(throttle.ShouldEmit(TimeSpan.FromMilliseconds(10)));

        throttle.Force();

        Assert.True(throttle.ShouldEmit(TimeSpan.FromMilliseconds(11)));
        Assert.False(throttle.ShouldEmit(TimeSpan.FromMilliseconds(12)));
    }

    [Fact]
    public void A_forced_emit_restarts_the_interval()
    {
        var throttle = new PositionThrottle(10);
        throttle.Force();
        Assert.True(throttle.ShouldEmit(TimeSpan.FromMilliseconds(500)));
        Assert.False(throttle.ShouldEmit(TimeSpan.FromMilliseconds(550)));
        Assert.True(throttle.ShouldEmit(TimeSpan.FromMilliseconds(600)));
    }

    [Fact]
    public void Reset_makes_the_next_observation_the_first_one_again()
    {
        var throttle = new PositionThrottle(10);
        Assert.True(throttle.ShouldEmit(TimeSpan.FromSeconds(5)));

        throttle.Reset();

        Assert.True(throttle.ShouldEmit(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_clock_that_jumps_backwards_does_not_stall_the_events()
    {
        // Source changes restart the player clock at zero while the throttle
        // still remembers the old position.
        var throttle = new PositionThrottle(10);
        Assert.True(throttle.ShouldEmit(TimeSpan.FromSeconds(30)));
        Assert.True(throttle.ShouldEmit(TimeSpan.Zero));
    }
}
