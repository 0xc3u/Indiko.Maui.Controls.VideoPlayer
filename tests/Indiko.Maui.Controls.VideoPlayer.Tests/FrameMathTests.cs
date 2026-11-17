using Xunit;
using Indiko.Maui.Controls.VideoPlayer.Timeline;

namespace Indiko.Maui.Controls.VideoPlayer.Tests;

public class FrameMathTests
{
    [Theory]
    [InlineData(23.976, 41.7083)]
    [InlineData(24, 41.6667)]
    [InlineData(25, 40)]
    [InlineData(29.97, 33.3667)]
    [InlineData(30, 33.3333)]
    [InlineData(50, 20)]
    [InlineData(59.94, 16.6833)]
    [InlineData(60, 16.6667)]
    [InlineData(120, 8.3333)]
    [InlineData(240, 4.1667)]
    public void FrameDuration_matches_the_frame_rate(double fps, double expectedMilliseconds)
    {
        var actual = FrameMath.FrameDuration(fps).TotalMilliseconds;
        Assert.InRange(actual, expectedMilliseconds - 0.001, expectedMilliseconds + 0.001);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void An_unknown_frame_rate_falls_back_to_thirty(double fps)
    {
        Assert.Equal(FrameMath.FrameDuration(30), FrameMath.FrameDuration(fps));
        Assert.Equal(30d, FrameMath.EffectiveFrameRate(fps));
    }

    [Fact]
    public void FrameIndexAt_counts_whole_frames_from_zero()
    {
        Assert.Equal(0, FrameMath.FrameIndexAt(TimeSpan.Zero, 30));
        Assert.Equal(0, FrameMath.FrameIndexAt(TimeSpan.FromMilliseconds(20), 30));
        Assert.Equal(1, FrameMath.FrameIndexAt(TimeSpan.FromMilliseconds(40), 30));
        Assert.Equal(30, FrameMath.FrameIndexAt(TimeSpan.FromSeconds(1), 30));
    }

    [Fact]
    public void FrameIndexAt_is_not_thrown_off_by_a_boundary_a_tick_early()
    {
        // 29.97 fps boundaries are not representable exactly, so a position
        // computed as "frame 300" arrives a few ticks below the boundary. Without
        // a tolerance it would read as frame 299 and every step would drift.
        var frame300 = TimeSpan.FromTicks(FrameMath.FrameDuration(29.97).Ticks * 300 - 3);
        Assert.Equal(300, FrameMath.FrameIndexAt(frame300, 29.97));
    }

    [Fact]
    public void FrameIndexAt_clamps_a_negative_position_to_the_first_frame()
    {
        Assert.Equal(0, FrameMath.FrameIndexAt(TimeSpan.FromSeconds(-5), 30));
    }

    [Fact]
    public void StepTarget_lands_in_the_middle_of_the_requested_frame()
    {
        // Seeking to a frame boundary is a coin flip between two frames once
        // the decoder rounds; the middle of the frame is unambiguous.
        var duration = TimeSpan.FromSeconds(60);
        var target = FrameMath.StepTarget(TimeSpan.Zero, 30, 1, duration);
        Assert.Equal(1, FrameMath.FrameIndexAt(target, 30));
        Assert.InRange(target.TotalMilliseconds, 49.9, 50.1);
    }

    [Fact]
    public void StepTarget_is_stable_when_repeated()
    {
        var duration = TimeSpan.FromSeconds(60);
        var position = TimeSpan.FromSeconds(10);

        var forward = FrameMath.StepTarget(position, 30, 1, duration);
        var back = FrameMath.StepTarget(forward, 30, -1, duration);

        Assert.Equal(FrameMath.FrameIndexAt(position, 30), FrameMath.FrameIndexAt(back, 30));
    }

    [Fact]
    public void Stepping_forward_thirty_times_advances_exactly_thirty_frames()
    {
        var duration = TimeSpan.FromSeconds(60);
        var position = TimeSpan.FromSeconds(5);
        var start = FrameMath.FrameIndexAt(position, 29.97);

        for (var i = 0; i < 30; i++)
            position = FrameMath.StepTarget(position, 29.97, 1, duration);

        Assert.Equal(start + 30, FrameMath.FrameIndexAt(position, 29.97));
    }

    [Fact]
    public void StepTarget_clamps_at_the_start()
    {
        var target = FrameMath.StepTarget(TimeSpan.Zero, 30, -5, TimeSpan.FromSeconds(60));
        Assert.Equal(TimeSpan.Zero, target);
    }

    [Fact]
    public void StepTarget_clamps_a_frame_short_of_the_end()
    {
        // The instant at Duration is past the last frame: seeking there ends
        // playback instead of showing a picture.
        var duration = TimeSpan.FromSeconds(10);
        var target = FrameMath.StepTarget(duration, 30, 5, duration);
        Assert.True(target <= duration - FrameMath.FrameDuration(30));
        Assert.True(target > TimeSpan.Zero);
    }

    [Fact]
    public void StepTarget_of_zero_frames_snaps_onto_the_frame_grid()
    {
        var target = FrameMath.StepTarget(TimeSpan.FromMilliseconds(41), 30, 0, TimeSpan.FromSeconds(60));
        Assert.Equal(1, FrameMath.FrameIndexAt(target, 30));
    }

    [Fact]
    public void StepTarget_on_a_duration_shorter_than_one_frame_stays_at_zero()
    {
        var target = FrameMath.StepTarget(TimeSpan.Zero, 30, 1, TimeSpan.FromMilliseconds(10));
        Assert.Equal(TimeSpan.Zero, target);
    }

    [Fact]
    public void A_step_across_a_chapter_boundary_lands_on_the_first_frame_of_the_next_chapter()
    {
        // 20 s at 30 fps is 600 frames: frames 0..599 are chapter 0, and frame
        // 600 is the first frame of chapter 1.
        var timeline = new ChapterTimeline([TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20)]);
        var lastFrameOfFirstChapter = FrameMath.StepTarget(
            TimeSpan.FromSeconds(20), 30, -1, timeline.Total);

        Assert.Equal(0, timeline.ToLocal(lastFrameOfFirstChapter).Index);
        Assert.Equal(599, FrameMath.FrameIndexAt(lastFrameOfFirstChapter, 30));

        var next = FrameMath.StepTarget(lastFrameOfFirstChapter, 30, 1, timeline.Total);
        var (index, local) = timeline.ToLocal(next);

        Assert.Equal(1, index);
        Assert.Equal(600, FrameMath.FrameIndexAt(next, 30));
        Assert.InRange(local.TotalMilliseconds, 0, FrameMath.FrameDuration(30).TotalMilliseconds);
    }

    [Fact]
    public void A_backward_step_across_a_chapter_boundary_returns_to_the_previous_chapter()
    {
        var timeline = new ChapterTimeline([TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20)]);
        var firstFrameOfSecond = FrameMath.StepTarget(TimeSpan.FromSeconds(20), 30, 0, timeline.Total);

        var back = FrameMath.StepTarget(firstFrameOfSecond, 30, -1, timeline.Total);

        Assert.Equal(0, timeline.ToLocal(back).Index);
        Assert.Equal(599, FrameMath.FrameIndexAt(back, 30));
    }
}
