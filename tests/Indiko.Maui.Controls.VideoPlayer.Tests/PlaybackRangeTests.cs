using Xunit;
using Indiko.Maui.Controls.VideoPlayer.Timeline;

namespace Indiko.Maui.Controls.VideoPlayer.Tests;

public class PlaybackRangeTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromSeconds(60);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Without_either_point_there_is_no_range_to_honour()
    {
        var range = PlaybackRange.None;

        Assert.False(range.IsSet);
        Assert.False(range.IsUsable(Minute));
    }

    [Fact]
    public void An_in_point_alone_runs_to_the_end_of_the_media()
    {
        var range = new PlaybackRange(S(10), null);

        Assert.True(range.IsUsable(Minute));
        Assert.Equal(S(10), range.Start(Minute));
        Assert.Equal(Minute, range.End(Minute));
    }

    [Fact]
    public void An_out_point_alone_runs_from_the_beginning()
    {
        var range = new PlaybackRange(null, S(10));

        Assert.True(range.IsUsable(Minute));
        Assert.Equal(TimeSpan.Zero, range.Start(Minute));
        Assert.Equal(S(10), range.End(Minute));
    }

    [Theory]
    [InlineData(-5, 10, 0, 10)]
    [InlineData(10, 999, 10, 60)]
    [InlineData(-5, 999, 0, 60)]
    public void Points_outside_the_media_are_brought_inside_it(
        double inPoint, double outPoint, double expectedStart, double expectedEnd)
    {
        var range = new PlaybackRange(S(inPoint), S(outPoint));

        Assert.Equal(S(expectedStart), range.Start(Minute));
        Assert.Equal(S(expectedEnd), range.End(Minute));
    }

    [Theory]
    [InlineData(20, 10)]
    [InlineData(10, 10)]
    [InlineData(70, 80)]
    public void A_range_that_ends_before_it_starts_is_no_range(double inPoint, double outPoint)
    {
        // Rather than quietly swapping the two or pushing one of them along:
        // an app that computed them the wrong way round gets the whole media,
        // which is visible, instead of a range it did not ask for.
        var range = new PlaybackRange(S(inPoint), S(outPoint));

        Assert.True(range.IsSet);
        Assert.False(range.IsUsable(Minute));
    }

    [Fact]
    public void Nothing_is_usable_before_the_duration_is_known()
    {
        Assert.False(new PlaybackRange(S(1), S(2)).IsUsable(TimeSpan.Zero));
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(19.9, true)]
    [InlineData(9.9, false)]
    [InlineData(20, false)]
    [InlineData(30, false)]
    public void The_range_holds_its_start_but_not_its_end(double position, bool inside)
    {
        // The out point is where playback stops, so the instant at it is the
        // first one outside: a loop that included it would show a frame from
        // beyond the range on every pass.
        var range = new PlaybackRange(S(10), S(20));

        Assert.Equal(inside, range.Contains(S(position), Minute));
    }

    [Theory]
    [InlineData(15, 15)]
    [InlineData(5, 10)]
    [InlineData(25, 10)]
    [InlineData(20, 10)]
    public void Playing_from_outside_the_range_starts_at_its_beginning(
        double position, double expected)
    {
        var range = new PlaybackRange(S(10), S(20));

        Assert.Equal(S(expected), range.Resume(S(position), Minute));
    }

    [Fact]
    public void Playing_without_a_range_carries_on_from_where_it_is()
    {
        Assert.Equal(S(42), PlaybackRange.None.Resume(S(42), Minute));
    }

    [Theory]
    [InlineData(10, 1, 10)]
    [InlineData(15, 1, 5)]
    [InlineData(10, 2, 5)]
    [InlineData(10, 0.5, 20)]
    [InlineData(19, 0.25, 4)]
    public void The_time_left_in_the_range_follows_the_playback_speed(
        double position, double speed, double expectedSeconds)
    {
        var range = new PlaybackRange(S(10), S(20));

        Assert.Equal(
            expectedSeconds,
            range.TimeUntilEnd(S(position), Minute, speed).TotalSeconds,
            6);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(25)]
    [InlineData(100)]
    public void Past_the_end_there_is_no_time_left(double position)
    {
        var range = new PlaybackRange(S(10), S(20));

        Assert.Equal(TimeSpan.Zero, range.TimeUntilEnd(S(position), Minute, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void A_nonsense_speed_is_read_as_normal_play(double speed)
    {
        var range = new PlaybackRange(S(10), S(20));

        Assert.Equal(S(5), range.TimeUntilEnd(S(15), Minute, speed));
    }

    [Fact]
    public void Without_a_range_nothing_is_waiting_to_happen()
    {
        Assert.Equal(TimeSpan.Zero, PlaybackRange.None.TimeUntilEnd(S(5), Minute, 1));
    }
}
