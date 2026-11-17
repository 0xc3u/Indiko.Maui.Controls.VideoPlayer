using Xunit;
using Indiko.Maui.Controls.VideoPlayer.Timeline;

namespace Indiko.Maui.Controls.VideoPlayer.Tests;

public class ChapterTimelineTests
{
    private static ChapterTimeline Of(params double[] seconds) =>
        new(seconds.Select(TimeSpan.FromSeconds));

    [Fact]
    public void Total_is_the_sum_of_the_chapters()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), Of(20, 20, 20).Total);
    }

    [Fact]
    public void Empty_chapter_list_is_rejected_at_construction()
    {
        Assert.Throws<ArgumentException>(() => new ChapterTimeline([]));
    }

    [Fact]
    public void Negative_duration_is_rejected_at_construction()
    {
        Assert.Throws<ArgumentException>(() => new ChapterTimeline([TimeSpan.FromSeconds(-1)]));
    }

    [Fact]
    public void A_single_chapter_maps_one_to_one()
    {
        var t = Of(20);
        Assert.Equal(0, t.ToLocal(TimeSpan.FromSeconds(5)).Index);
        Assert.Equal(TimeSpan.FromSeconds(5), t.ToLocal(TimeSpan.FromSeconds(5)).Position);
        Assert.Equal(TimeSpan.FromSeconds(5), t.ToGlobal(0, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ToGlobal_offsets_by_the_preceding_chapters()
    {
        var t = Of(20, 30, 10);
        Assert.Equal(TimeSpan.FromSeconds(25), t.ToGlobal(1, TimeSpan.FromSeconds(5)));
        Assert.Equal(TimeSpan.FromSeconds(55), t.ToGlobal(2, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ToLocal_finds_the_chapter_a_global_position_falls_into()
    {
        var t = Of(20, 30, 10);
        var (index, position) = t.ToLocal(TimeSpan.FromSeconds(25));
        Assert.Equal(1, index);
        Assert.Equal(TimeSpan.FromSeconds(5), position);
    }

    [Fact]
    public void A_position_exactly_on_a_boundary_belongs_to_the_next_chapter()
    {
        // 20 s is the last instant of chapter 0 and the first of chapter 1.
        // Resolving it to (0, 20s) would show the final frame of the previous
        // chapter while the player has already moved on.
        var (index, position) = Of(20, 30).ToLocal(TimeSpan.FromSeconds(20));
        Assert.Equal(1, index);
        Assert.Equal(TimeSpan.Zero, position);
    }

    [Fact]
    public void A_position_before_the_start_clamps_to_the_first_chapter()
    {
        var (index, position) = Of(20, 30).ToLocal(TimeSpan.FromSeconds(-5));
        Assert.Equal(0, index);
        Assert.Equal(TimeSpan.Zero, position);
    }

    [Fact]
    public void A_position_at_or_past_the_end_clamps_to_the_end_of_the_last_chapter()
    {
        var t = Of(20, 30);
        foreach (var global in new[] { TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(500) })
        {
            var (index, position) = t.ToLocal(global);
            Assert.Equal(1, index);
            Assert.Equal(TimeSpan.FromSeconds(30), position);
        }
    }

    [Fact]
    public void Zero_length_chapters_are_never_resolved_to()
    {
        // A chapter whose file reported a duration of zero still occupies a
        // slot in the clip list, but seeking into it is meaningless.
        var t = Of(10, 0, 20);
        Assert.Equal(TimeSpan.FromSeconds(30), t.Total);

        var boundary = t.ToLocal(TimeSpan.FromSeconds(10));
        Assert.Equal(2, boundary.Index);
        Assert.Equal(TimeSpan.Zero, boundary.Position);

        var inside = t.ToLocal(TimeSpan.FromSeconds(15));
        Assert.Equal(2, inside.Index);
        Assert.Equal(TimeSpan.FromSeconds(5), inside.Position);
    }

    [Fact]
    public void A_trailing_zero_length_chapter_is_not_the_end_position()
    {
        var t = Of(10, 0);
        var (index, position) = t.ToLocal(TimeSpan.FromSeconds(10));
        Assert.Equal(0, index);
        Assert.Equal(TimeSpan.FromSeconds(10), position);
    }

    [Fact]
    public void All_zero_length_chapters_still_resolve_to_the_first_chapter()
    {
        var t = Of(0, 0);
        Assert.Equal(TimeSpan.Zero, t.Total);
        var (index, position) = t.ToLocal(TimeSpan.FromSeconds(1));
        Assert.Equal(0, index);
        Assert.Equal(TimeSpan.Zero, position);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(19_999)]
    [InlineData(20_000)]
    [InlineData(20_001)]
    [InlineData(49_999)]
    [InlineData(50_000)]
    public void Global_to_local_and_back_returns_the_same_position(int milliseconds)
    {
        var t = Of(20, 30);
        var global = TimeSpan.FromMilliseconds(milliseconds);
        var (index, local) = t.ToLocal(global);
        Assert.InRange((t.ToGlobal(index, local) - global).Ticks, -1, 1);
    }

    [Fact]
    public void ToGlobal_rejects_an_index_outside_the_chapter_list()
    {
        var t = Of(20, 30);
        Assert.Throws<ArgumentOutOfRangeException>(() => t.ToGlobal(2, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.ToGlobal(-1, TimeSpan.Zero));
    }

    [Fact]
    public void ToGlobal_clamps_a_local_position_to_its_chapter()
    {
        var t = Of(20, 30);
        Assert.Equal(TimeSpan.FromSeconds(20), t.ToGlobal(0, TimeSpan.FromSeconds(99)));
        Assert.Equal(TimeSpan.FromSeconds(20), t.ToGlobal(1, TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void Chapter_durations_are_exposed_in_order()
    {
        Assert.Equal([TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)], Of(20, 30).ChapterDurations);
    }
}
