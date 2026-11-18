using Android.Content;
using Android.Media;
using Indiko.Maui.Controls.VideoPlayer.Timeline;
using AndroidUri = Android.Net.Uri;

namespace Indiko.Maui.Controls.VideoPlayer.Platforms.Android;

public partial class VideoPlayerViewHandler
{
    /// <summary>
    /// Reads when every frame begins, clip by clip, and switches the clock
    /// over to it.
    /// </summary>
    /// <remarks>
    /// <c>MediaExtractor</c> walks the container's sample table without
    /// decoding anything, so the cost is one pass over the index rather than
    /// over the pictures. Each clip is read on its own and shifted onto the
    /// timeline the app sees, which is the same arithmetic the chapter
    /// timeline does for seeks.
    /// </remarks>
    private async Task BuildFrameIndexAsync(
        IReadOnlyList<AndroidUri> uris, ChapterTimeline timeline, double frameRate, int generation)
    {
        var context = Context;

        if (context is null || uris.Count == 0)
            return;

        var starts = new List<TimeSpan>(uris.Count);

        for (var chapter = 0; chapter < uris.Count && chapter < timeline.Count; chapter++)
            starts.Add(timeline.ChapterStart(chapter));

        FrameIndex? index;

        try
        {
            index = await Task.Run(() => ReadFrameIndex(context, uris, starts)).ConfigureAwait(true);
        }
        catch (Exception)
        {
            return;
        }

        // A source that was replaced while this ran must not have its frames
        // renumbered by the one before it.
        if (index is null || generation != _generation || VirtualView is null)
            return;

        _frames = FrameClock.FromRate(frameRate, timeline.Total).WithIndex(index);

        Report?.OnFrameCountChanged(_frames.Count);

        // The frame on screen may have had a different number a moment ago, and
        // the tracked one was counted the old way.
        _displayedFrame = null;

        if (_player is { } player)
            ReportPosition(TimeSpan.FromMilliseconds(player.CurrentPosition), force: true);
    }

    private static FrameIndex? ReadFrameIndex(
        Context context, IReadOnlyList<AndroidUri> uris, IReadOnlyList<TimeSpan> starts)
    {
        var parts = new List<FrameIndex>(uris.Count);

        for (var chapter = 0; chapter < uris.Count && chapter < starts.Count; chapter++)
        {
            if (ReadClip(context, uris[chapter], starts[chapter]) is not { } part)
                return null;

            parts.Add(part);
        }

        return parts.Count > 0 ? FrameIndex.Join(parts) : null;
    }

    private static FrameIndex? ReadClip(Context context, AndroidUri uri, TimeSpan offset)
    {
        MediaExtractor? extractor = null;

        try
        {
            extractor = new MediaExtractor();
            extractor.SetDataSource(context, uri, null);

            var track = VideoTrackOf(extractor);

            if (track < 0)
                return null;

            extractor.SelectTrack(track);

            var times = new List<TimeSpan>(4096);

            while (true)
            {
                var microseconds = extractor.SampleTime;

                if (microseconds < 0)
                    break;

                times.Add(TimeSpan.FromTicks(microseconds * (TimeSpan.TicksPerMillisecond / 1000)));

                if (!extractor.Advance())
                    break;
            }

            return times.Count > 0 ? new FrameIndex(times, offset) : null;
        }
        catch (Exception)
        {
            // A container the extractor cannot walk is a clip without an exact
            // index, not a clip that cannot be played.
            return null;
        }
        finally
        {
            extractor?.Release();
            extractor?.Dispose();
        }
    }

    private static int VideoTrackOf(MediaExtractor extractor)
    {
        for (var i = 0; i < extractor.TrackCount; i++)
        {
            var mime = extractor.GetTrackFormat(i)?.GetString(MediaFormat.KeyMime);

            if (mime?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true)
                return i;
        }

        return -1;
    }
}
