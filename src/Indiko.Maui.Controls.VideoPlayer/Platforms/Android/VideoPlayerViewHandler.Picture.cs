using Android.Graphics;
using Indiko.Maui.Controls.VideoPlayer.Timeline;
using MediaMetadataRetriever = Android.Media.MediaMetadataRetriever;
using Point = Microsoft.Maui.Graphics.Point;

namespace Indiko.Maui.Controls.VideoPlayer.Platforms.Android;

public partial class VideoPlayerViewHandler
{
    public Point? ToPicturePoint(Point viewPoint)
    {
        var projection = Project();

        return projection.ToVideo(viewPoint.X, viewPoint.Y) is { } point
            ? new Point(point.X, point.Y)
            : null;
    }

    public Point? ToViewPoint(Point picturePoint)
    {
        var projection = Project();

        return projection.ToView(picturePoint.X, picturePoint.Y) is { } point
            ? new Point(point.X, point.Y)
            : null;
    }

    /// <summary>
    /// How the frame is laid out on the control at this moment.
    /// </summary>
    /// <remarks>
    /// Media3's content frame is already the rectangle the resize mode asked
    /// for — letterboxed under Fit, the whole control under Stretch — so it is
    /// measured rather than computed. Its coordinates are pixels and the
    /// control's are device-independent units, which is the only conversion
    /// this platform needs.
    /// </remarks>
    private VideoProjection Project()
    {
        if (PlatformView is not { } platformView || VirtualView is not { } view)
            return default;

        if (ContentFrame is not { } frame)
            return default;

        var density = Density;

        return new VideoProjection(
            new ViewRect(
                frame.Left / density, frame.Top / density,
                frame.Width / density, frame.Height / density),
            new ViewRect(0, 0, platformView.Width / density, platformView.Height / density),
            new ZoomState(view.ZoomFactor, view.ZoomOffset.X, view.ZoomOffset.Y),
            view.NaturalSize.Width,
            view.NaturalSize.Height);
    }

    public async Task<byte[]?> CaptureFrameAsync(CancellationToken cancellationToken)
    {
        if (_player is not { } player || _timeline is null || _chapterUris.Count == 0)
            return null;

        var position = TimeSpan.FromMilliseconds(player.CurrentPosition);
        var frame = _displayedFrame ?? FrameMath.FrameIndexAt(position, _frameRate);

        // The middle of the wanted frame rather than its start: the retriever
        // returns the frame closest to the time it is given, and a frame
        // boundary is a coin flip between two of them.
        var target = FrameMath.FrameStartTime(frame, _frameRate)
            + (FrameMath.FrameDuration(_frameRate) / 2);

        var (chapter, local) = _timeline.ToLocal(target);

        if (chapter < 0 || chapter >= _chapterUris.Count)
            return null;

        var uri = _chapterUris[chapter];
        var context = Context;
        var microseconds = (long)local.TotalMicroseconds;

        try
        {
            return await Task.Run(
                () =>
                {
                    MediaMetadataRetriever? retriever = null;

                    try
                    {
                        retriever = new MediaMetadataRetriever();
                        retriever.SetDataSource(context, uri);

                        // Closest, not ClosestSync: the sync options answer
                        // with a keyframe, which for long-GOP footage can be
                        // seconds away from the frame on screen.
                        using var bitmap = retriever.GetFrameAtTime(
                            microseconds, global::Android.Media.Option.Closest);

                        if (bitmap is null)
                            return null;

                        using var stream = new MemoryStream();
                        bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
                        return stream.ToArray();
                    }
                    catch (Exception)
                    {
                        // A clip that cannot be read a second time is a missing
                        // picture, not a reason to take the app down.
                        return null;
                    }
                    finally
                    {
                        retriever?.Release();
                        retriever?.Dispose();
                    }
                },
                cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
