using Xunit;
using Indiko.Maui.Controls.VideoPlayer.Timeline;

namespace Indiko.Maui.Controls.VideoPlayer.Tests;

public class FrameClockTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static readonly TimeSpan Minute = TimeSpan.FromSeconds(60);

    [Fact]
    public void Without_an_index_it_counts_by_the_frame_rate()
    {
        var clock = FrameClock.FromRate(30, Minute);

        Assert.False(clock.IsExact);
        Assert.Equal(0, clock.Count);
        Assert.Equal(1350, clock.FrameAt(S(45)));
        Assert.Equal(S(45), clock.FrameStart(1350));
    }

    [Fact]
    public void Without_an_index_it_seeks_as_the_frame_math_says()
    {
        var clock = FrameClock.FromRate(30, Minute);

        Assert.Equal(
            FrameMath.SeekTargetForFrame(1350, 30, Minute, FrameSeekSemantics.AtOrAfter),
            clock.SeekTarget(1350, FrameSeekSemantics.AtOrAfter));
    }

    [Fact]
    public void With_an_index_it_counts_the_frames_that_are_really_there()
    {
        // 15 fps for two frames, then 60 fps: a rate of 30 describes neither.
        var clock = FrameClock.FromRate(30, S(1)).WithIndex(
            new FrameIndex([S(0), S(1.0 / 15), S(0.2), S(0.2 + 1.0 / 60)]));

        Assert.True(clock.IsExact);
        Assert.Equal(4, clock.Count);
        Assert.Equal(1, clock.FrameAt(S(0.1)));
        Assert.Equal(3, clock.FrameAt(S(0.25)));
        Assert.Equal(S(0.2), clock.FrameStart(2));
    }

    [Fact]
    public void With_an_index_a_step_lands_on_the_next_frame_however_long_it_is()
    {
        var clock = FrameClock.FromRate(30, S(1)).WithIndex(
            new FrameIndex([S(0), S(1.0 / 15), S(0.2), S(0.2 + 1.0 / 60)]));

        var target = clock.StepTarget(S(0.07), 1, FrameSeekSemantics.Containing);

        // Frame 1 begins at 1/15 s and lasts until 0.2 s; stepping forward from
        // inside it has to land in frame 2, not a 30th of a second later.
        Assert.Equal(2, clock.FrameAt(target));
    }

    [Fact]
    public void Stepping_back_from_the_first_frame_stays_there()
    {
        var clock = FrameClock.FromRate(30, S(1)).WithIndex(
            new FrameIndex([S(0), S(0.5)]));

        Assert.Equal(0, clock.FrameAt(clock.StepTarget(S(0.1), -5, FrameSeekSemantics.Containing)));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(999999, 3)]
    public void A_frame_number_outside_the_media_is_brought_inside_it(long asked, long expected)
    {
        // Asking for a frame past the end seeks to the last one, so reporting
        // the number that was asked for would put a frame on the readout that
        // nobody can see and no step can reach.
        var clock = FrameClock.FromRate(30, S(1)).WithIndex(
            new FrameIndex([S(0), S(0.1), S(0.2), S(0.3)]));

        Assert.Equal(expected, clock.ClampFrame(asked));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(1799, 1799)]
    [InlineData(1800, 1799)]
    [InlineData(999999, 1799)]
    public void Without_an_index_the_frame_rate_decides_where_the_last_frame_is(
        long asked, long expected)
    {
        var clock = FrameClock.FromRate(30, Minute);

        Assert.Equal(expected, clock.ClampFrame(asked));
    }

    [Fact]
    public void A_rate_of_nothing_still_counts_something()
    {
        // The container did not say, so the fallback rate decides rather than
        // a division by zero.
        var clock = FrameClock.FromRate(0, Minute);

        Assert.Equal(FrameMath.FrameIndexAt(S(1), FrameMath.FallbackFrameRate), clock.FrameAt(S(1)));
    }
}
