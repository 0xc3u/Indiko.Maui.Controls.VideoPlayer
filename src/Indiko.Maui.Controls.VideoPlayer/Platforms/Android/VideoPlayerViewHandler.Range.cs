using Indiko.Maui.Controls.VideoPlayer.Timeline;

namespace Indiko.Maui.Controls.VideoPlayer.Platforms.Android;

public partial class VideoPlayerViewHandler
{
    private Action? _rangeEnd;

    private PlaybackRange Range => VirtualView?.Range ?? PlaybackRange.None;

    private TimeSpan Duration => _timeline?.Total ?? TimeSpan.Zero;

    private static void MapRange(VideoPlayerViewHandler handler, VideoPlayerView view) =>
        handler.ScheduleRangeEnd();

    /// <summary>
    /// Books the moment playback is due to reach the out point.
    /// </summary>
    /// <remarks>
    /// Media3 has nothing like AVFoundation's <c>ForwardPlaybackEndTime</c>, so
    /// the moment is worked out and waited for. Watching the position tick
    /// would do it too, but at 15 Hz the picture runs up to 66 ms past the out
    /// point before the loop notices, and a loop that overshoots by two frames
    /// every pass is exactly what this control exists not to do. The tick stays
    /// on as a safety net for a wake-up that never comes.
    /// </remarks>
    /// <param name="from">
    /// Where playback is about to be, for a caller that has just moved it. The
    /// player reports the position it is still leaving for a moment after a
    /// seek, and booking the next end from that stale value books it for now,
    /// which ends the range again immediately and seeks again — a loop that
    /// seeks in a tight circle instead of playing.
    /// </param>
    private void ScheduleRangeEnd(TimeSpan? from = null)
    {
        CancelRangeEnd();

        if (_player is not { } player || _ticker is null || !player.IsPlaying)
            return;

        var range = Range;
        var duration = Duration;

        if (!range.IsUsable(duration))
            return;

        var position = from ?? TimeSpan.FromMilliseconds(player.CurrentPosition);
        var remaining = range.TimeUntilEnd(position, duration, ValueClamp.Speed(VirtualView.Speed));

        _rangeEnd = OnRangeEnd;
        _ticker.PostDelayed(_rangeEnd, Math.Max(0, (long)remaining.TotalMilliseconds));
    }

    private void CancelRangeEnd()
    {
        if (_ticker is not null && _rangeEnd is not null)
            _ticker.RemoveCallbacks(_rangeEnd);

        _rangeEnd = null;
    }

    private void OnRangeEnd()
    {
        _rangeEnd = null;
        ReachedRangeEnd();
    }

    /// <summary>
    /// Loops back to the in point, or stops on the out point.
    /// </summary>
    /// <remarks>
    /// Called both by the booked moment and by the ticker. A wake-up that
    /// arrives early — a decoder that stalled, a speed that changed under it —
    /// books the rest of the wait again rather than cutting the range short.
    /// </remarks>
    private void ReachedRangeEnd()
    {
        if (_player is not { } player)
            return;

        var range = Range;
        var duration = Duration;

        if (!range.IsUsable(duration))
            return;

        var position = TimeSpan.FromMilliseconds(player.CurrentPosition);
        var end = range.End(duration);

        if (position < end)
        {
            ScheduleRangeEnd();
            return;
        }

        // The tracked frame belongs to where the picture was, not to where it
        // is about to be.
        _displayedFrame = null;

        if (VirtualView.IsLooping)
        {
            var start = range.Start(duration);

            player.SeekTo((long)start.TotalMilliseconds);
            ReportPosition(start, force: true);
            ScheduleRangeEnd(from: start);
            return;
        }

        // Stopping leaves the picture on the frame that begins at the out
        // point: it is where AVFoundation stops of its own accord, and a
        // control whose two platforms stop on different frames would be
        // useless for counting them.
        player.Pause();
        player.SeekTo((long)end.TotalMilliseconds);
        ReportPosition(end, force: true);
    }

    /// <summary>
    /// Moves to the start of the range before playing, when the picture is
    /// outside it. Pressing play on a range is a request to play the range.
    /// </summary>
    private void StartInsideRange()
    {
        if (_player is not { } player)
            return;

        var range = Range;
        var duration = Duration;

        if (!range.IsUsable(duration))
            return;

        var position = TimeSpan.FromMilliseconds(player.CurrentPosition);
        var resume = range.Resume(position, duration);

        if (resume != position)
            player.SeekTo((long)resume.TotalMilliseconds);
    }
}
