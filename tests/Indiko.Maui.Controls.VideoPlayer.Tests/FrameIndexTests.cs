using Xunit;
using Indiko.Maui.Controls.VideoPlayer.Timeline;

namespace Indiko.Maui.Controls.VideoPlayer.Tests;

public class FrameIndexTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    // Three frames at 15 fps, then three at 60: the shape of a clip whose
    // camera changed its mind, and the shape no single frame rate describes.
    private static FrameIndex Uneven() => new(
    [
        S(0), S(1.0 / 15), S(2.0 / 15),
        S(0.2), S(0.2 + 1.0 / 60), S(0.2 + 2.0 / 60),
    ]);

    [Fact]
    public void An_index_needs_at_least_one_frame()
    {
        Assert.Throws<ArgumentException>(() => new FrameIndex([]));
    }

    [Fact]
    public void Frames_are_counted_from_zero_in_time_order()
    {
        var index = Uneven();

        Assert.Equal(6, index.Count);
        Assert.Equal(S(0), index.StartTime(0));
        Assert.Equal(S(0.2), index.StartTime(3));
        Assert.Equal(S(0.2 + 2.0 / 60), index.StartTime(5));
    }

    [Fact]
    public void Timestamps_out_of_order_are_put_in_order()
    {
        // A container hands out samples in decode order, which with B-frames is
        // not the order they are shown in.
        var index = new FrameIndex([S(0.2), S(0), S(0.1)]);

        Assert.Equal(S(0), index.StartTime(0));
        Assert.Equal(S(0.1), index.StartTime(1));
        Assert.Equal(S(0.2), index.StartTime(2));
    }

    [Fact]
    public void A_repeated_timestamp_counts_once()
    {
        var index = new FrameIndex([S(0), S(0.1), S(0.1), S(0.2)]);

        Assert.Equal(3, index.Count);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.01, 0)]
    [InlineData(1.0 / 15, 1)]
    [InlineData(0.1, 1)]
    [InlineData(0.2, 3)]
    [InlineData(0.21, 3)]
    [InlineData(0.2 + 1.0 / 60, 4)]
    [InlineData(99, 5)]
    public void The_frame_at_a_position_is_the_last_one_that_began(
        double seconds, long expected)
    {
        Assert.Equal(expected, Uneven().IndexAt(S(seconds)));
    }

    [Fact]
    public void A_position_before_the_first_frame_is_the_first_frame()
    {
        Assert.Equal(0, Uneven().IndexAt(S(-5)));
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(99, 5)]
    public void A_frame_number_outside_the_index_is_brought_inside_it(long asked, long expected)
    {
        Assert.Equal(Uneven().StartTime(expected), Uneven().StartTime(asked));
    }

    [Fact]
    public void Every_frame_reads_back_as_itself()
    {
        var index = Uneven();

        for (var i = 0; i < index.Count; i++)
            Assert.Equal(i, index.IndexAt(index.StartTime(i)));
    }

    [Fact]
    public void A_seek_target_lands_inside_the_frame_it_asks_for()
    {
        var index = Uneven();
        var duration = S(0.25);

        for (var i = 0; i < index.Count; i++)
        {
            var target = index.SeekTarget(i, FrameSeekSemantics.Containing, duration);
            Assert.Equal(i, index.IndexAt(target));
        }
    }

    [Fact]
    public void A_seek_target_for_a_player_that_rounds_forward_lands_in_the_frame_before()
    {
        // Media3 shows the first frame at or after the position, so the target
        // has to sit short of the wanted frame — inside its predecessor.
        var index = Uneven();
        var duration = S(0.25);

        for (var i = 1; i < index.Count; i++)
        {
            var target = index.SeekTarget(i, FrameSeekSemantics.AtOrAfter, duration);

            Assert.Equal(i - 1, index.IndexAt(target));
            Assert.True(target < index.StartTime(i));
        }
    }

    [Fact]
    public void The_first_frame_is_reached_from_the_very_beginning()
    {
        var index = Uneven();

        Assert.Equal(
            TimeSpan.Zero,
            index.SeekTarget(0, FrameSeekSemantics.AtOrAfter, S(0.25)));
    }

    [Fact]
    public void A_seek_target_stays_inside_the_media()
    {
        var index = Uneven();
        var duration = S(0.25);

        var target = index.SeekTarget(index.Count - 1, FrameSeekSemantics.Containing, duration);

        Assert.True(target < duration);
        Assert.True(target >= index.StartTime(index.Count - 1));
    }

    [Fact]
    public void Frames_shifted_by_a_chapter_start_stay_in_step()
    {
        // Chapters are concatenated, so an index for the second file has to be
        // offset onto the timeline the app sees.
        var index = new FrameIndex([S(0), S(0.1)], offset: S(20));

        Assert.Equal(S(20), index.StartTime(0));
        Assert.Equal(S(20.1), index.StartTime(1));
        Assert.Equal(1, index.IndexAt(S(20.15)));
    }

    [Fact]
    public void Two_indexes_can_be_joined_into_one_timeline()
    {
        var first = new FrameIndex([S(0), S(0.1)]);
        var second = new FrameIndex([S(0), S(0.1)], offset: S(0.2));

        var joined = FrameIndex.Join([first, second]);

        Assert.Equal(4, joined.Count);
        Assert.Equal(S(0.2), joined.StartTime(2));
        Assert.Equal(3, joined.IndexAt(S(0.31)));
    }
}
