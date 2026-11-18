using Android.Content;
using Android.OS;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Source;
using AndroidX.Media3.UI;
using Indiko.Maui.Controls.VideoPlayer.Models;
using Indiko.Maui.Controls.VideoPlayer.Timeline;
using Microsoft.Maui.Handlers;
using AndroidUri = Android.Net.Uri;
using MediaMetadataRetriever = Android.Media.MediaMetadataRetriever;
using MetadataKey = Android.Media.MetadataKey;
using Size = Microsoft.Maui.Graphics.Size;

namespace Indiko.Maui.Controls.VideoPlayer.Platforms.Android;

/// <summary>
/// Drives a Media3 <c>ExoPlayer</c> inside a controller-less <c>PlayerView</c>.
/// </summary>
public partial class VideoPlayerViewHandler
    : ViewHandler<VideoPlayerView, PlayerView>, IVideoPlayerController
{
    public static readonly IPropertyMapper<VideoPlayerView, VideoPlayerViewHandler> Mapper =
        new PropertyMapper<VideoPlayerView, VideoPlayerViewHandler>(ViewMapper)
        {
            [nameof(VideoPlayerView.Source)] = MapSource,
            [nameof(VideoPlayerView.Speed)] = MapSpeed,
            [nameof(VideoPlayerView.IsMuted)] = MapVolume,
            [nameof(VideoPlayerView.Volume)] = MapVolume,
            [nameof(VideoPlayerView.IsLooping)] = MapLooping,
            [nameof(VideoPlayerView.InPoint)] = MapRange,
            [nameof(VideoPlayerView.OutPoint)] = MapRange,
            [nameof(VideoPlayerView.ScalingMode)] = MapScalingMode,
            [nameof(VideoPlayerView.ZoomFactor)] = MapZoom,
            [nameof(VideoPlayerView.ZoomOffset)] = MapZoom,
            [nameof(VideoPlayerView.MaximumZoom)] = MapZoom,
            [nameof(VideoPlayerView.IsPinchToZoomEnabled)] = MapZoomGestures,
            [nameof(VideoPlayerView.PositionUpdateRate)] = MapPositionUpdateRate,
        };

    private readonly SeekCoordinator _seeks = new();

    // Media3 decodes to a surface and hands out no frames, so grabbing one
    // means opening the clip a second time. These are the URIs that were
    // already resolved and permission-checked at load, by chapter index.
    private readonly List<AndroidUri> _chapterUris = [];

    private IExoPlayer? _player;
    private PlayerListener? _listener;
    private Handler? _ticker;
    private Action? _tick;

    private ChapterTimeline? _timeline;
    private PositionThrottle _throttle = new(15);
    private FrameClock _frames = FrameClock.FromRate(0, TimeSpan.Zero);
    private double _frameRate;
    private bool _isScrubbing;
    private int _currentSeekGeneration;

    // Which frame is on screen, when that is known more precisely than the
    // player's own position. Media3 reports the position a seek asked for, and
    // with AtOrAfter semantics that sits half a frame below the frame it then
    // shows. Deriving the next step from it would compute the same target again
    // and the picture would stop advancing.
    private long? _displayedFrame;
    private TimeSpan _scrubTarget;
    private int _generation;

    public VideoPlayerViewHandler() : base(Mapper) { }

    public VideoPlayerViewHandler(IPropertyMapper? mapper) : base(mapper ?? Mapper) { }

    private IVideoPlayerViewController? Report => VirtualView;

    protected override PlayerView CreatePlatformView()
    {
        // Inflated rather than constructed because the surface type can only be
        // set from a layout attribute, and a TextureView is what makes the zoom
        // behave the same on every device. See the layout for the detail.
        var view = (PlayerView)global::Android.Views.LayoutInflater.From(Context)!
            .Inflate(Resource.Layout.indiko_videoplayer_surface, null)!;

        // The app builds its own controls; Media3's would fight with them and
        // cover the picture the user is trying to read frame numbers off.
        view.UseController = false;

        view.SetShutterBackgroundColor(global::Android.Graphics.Color.Black);
        return view;
    }

    protected override void ConnectHandler(PlayerView platformView)
    {
        base.ConnectHandler(platformView);

        var player = new ExoPlayerBuilder(Context).Build()
            ?? throw new InvalidOperationException("Media3 did not return a player.");

        // Media3 defaults to exact seeking, but this control's whole purpose
        // rests on it, so it is stated rather than inherited.
        player.SeekParameters = SeekParameters.Exact;

        _listener = new PlayerListener(this);
        player.AddListener(_listener);

        _player = player;
        platformView.Player = player;

        _ticker = new Handler(Looper.MainLooper!);
        _tick = OnTick;

        // A new size means a new picture rectangle, and a pan that was at the
        // edge of the old one is now either short of the new edge or past it.
        // The content frame rather than the player view: it is laid out
        // whenever the player view is, and also on its own when a clip of a
        // different shape changes the letterboxing.
        _zoomLayoutSource = ZoomLayoutSource(platformView);
        _zoomLayoutSource.LayoutChange += OnPlatformLayout;

        // Leaving the app pauses, and nothing resumes by itself: the control
        // runs no media session and no foreground service, so continuing would
        // be audio the user cannot see or stop.
        Microsoft.Maui.ApplicationModel.Platform.ActivityStateChanged += OnActivityStateChanged;
    }

    protected override void DisconnectHandler(PlayerView platformView)
    {
        Microsoft.Maui.ApplicationModel.Platform.ActivityStateChanged -= OnActivityStateChanged;

        if (_zoomLayoutSource is not null)
        {
            _zoomLayoutSource.LayoutChange -= OnPlatformLayout;
            _zoomLayoutSource = null;
        }

        ReleaseZoomGestures();

        StopTicker();

        _seeks.CancelPending();

        if (_player is not null)
        {
            if (_listener is not null)
                _player.RemoveListener(_listener);

            _player.Stop();
            _player.ClearMediaItems();
            _player.Release();
            _player = null;
        }

        _listener?.Dispose();
        _listener = null;

        platformView.Player = null;

        _ticker = null;
        _tick = null;
        _timeline = null;

        base.DisconnectHandler(platformView);
    }

    private void OnActivityStateChanged(
        object? sender, Microsoft.Maui.ApplicationModel.ActivityStateChangedEventArgs e)
    {
        if (e.State == Microsoft.Maui.ApplicationModel.ActivityState.Paused
            && VirtualView?.PauseWhenInBackground == true)
        {
            _player?.Pause();
        }
    }

    private static void MapSource(VideoPlayerViewHandler handler, VideoPlayerView view) =>
        handler.LoadSource(view.Source);

    private static void MapSpeed(VideoPlayerViewHandler handler, VideoPlayerView view)
    {
        handler._player?.SetPlaybackSpeed((float)ValueClamp.Speed(view.Speed));

        // Half speed is twice as long to the out point.
        handler.ScheduleRangeEnd();
    }

    private static void MapVolume(VideoPlayerViewHandler handler, VideoPlayerView view)
    {
        if (handler._player is { } player)
            player.Volume = view.IsMuted ? 0f : (float)ValueClamp.Volume(view.Volume);
    }

    private static void MapLooping(VideoPlayerViewHandler handler, VideoPlayerView view)
    {
        if (handler._player is { } player)
            // Media3's constants live on the Java interface, which the .NET
            // binding copies onto each implementing class rather than onto
            // IPlayer; BasePlayer is the neutral one to read them from.
            player.RepeatMode = view.IsLooping
                ? BasePlayer.InterfaceConsts.RepeatModeOne
                : BasePlayer.InterfaceConsts.RepeatModeOff;
    }

    private static void MapScalingMode(VideoPlayerViewHandler handler, VideoPlayerView view)
    {
        if (handler.PlatformView is not { } platformView)
            return;

        platformView.ResizeMode = view.ScalingMode switch
        {
            VideoScalingMode.Fill => AspectRatioFrameLayout.ResizeModeZoom,
            VideoScalingMode.Stretch => AspectRatioFrameLayout.ResizeModeFill,
            _ => AspectRatioFrameLayout.ResizeModeFit,
        };

        // Fit draws a letterboxed picture and Fill a cropped one, so the room
        // there is to pan changed with the resize mode.
        handler.ApplyZoom();
    }

    private static void MapPositionUpdateRate(VideoPlayerViewHandler handler, VideoPlayerView view) =>
        handler._throttle = new PositionThrottle(view.PositionUpdateRate);

    private void LoadSource(VideoPlayerSource? source)
    {
        if (_player is not { } player)
            return;

        // Results of a load that has been replaced must not reach the view: a
        // user tapping through a list of clips starts several loads, and the
        // slowest one would otherwise win.
        var generation = ++_generation;

        StopTicker();
        _seeks.CancelPending();
        player.Stop();
        player.ClearMediaItems();
        _chapterUris.Clear();
        _frames = FrameClock.FromRate(0, TimeSpan.Zero);
        _timeline = null;
        _displayedFrame = null;
        _throttle.Reset();

        if (source is null)
        {
            Report?.OnStateChanged(VideoPlayerState.Idle);
            return;
        }

        Report?.OnStateChanged(VideoPlayerState.Opening);

        var durations = new List<TimeSpan>(source.Clips.Count);
        var builder = new ConcatenatingMediaSource2.Builder();
        builder.UseDefaultMediaSourceFactory(Context);

        for (var index = 0; index < source.Clips.Count; index++)
        {
            var clip = source.Clips[index];

            if (!TryResolve(clip, index, out var uri))
                return;

            var duration = ReadDuration(uri!);
            durations.Add(duration);
            _chapterUris.Add(uri!);

            builder.Add(MediaItem.FromUri(uri!), (long)duration.TotalMilliseconds);
        }

        if (generation != _generation)
            return;

        _timeline = new ChapterTimeline(durations);

        player.SetMediaSource(builder.Build());
        player.Prepare();

        if (VirtualView.AutoPlay)
            player.Play();
    }

    private bool TryResolve(VideoClip clip, int index, out AndroidUri? uri)
    {
        uri = null;

        switch (clip.Kind)
        {
            case VideoClipKind.FilePath:
                if (!File.Exists(clip.Reference))
                {
                    Fail(VideoErrorCode.NotFound, index, "The file does not exist.");
                    return false;
                }

                uri = AndroidUri.FromFile(new Java.IO.File(clip.Reference));
                return true;

            case VideoClipKind.ContentUri:
                var parsed = AndroidUri.Parse(clip.Reference);
                if (parsed is null)
                {
                    Fail(VideoErrorCode.NotFound, index, "The content URI could not be parsed.");
                    return false;
                }

                // A persisted grant can be revoked by the user or lost when the
                // providing app is reinstalled, and ExoPlayer would report that
                // as a generic IO error much later. Opening it now says plainly
                // that the app has to ask for the clip again.
                if (!CanRead(parsed))
                {
                    Fail(VideoErrorCode.PermissionLost, index, "No read access to this content URI.");
                    return false;
                }

                uri = parsed;
                return true;

            default:
                Fail(VideoErrorCode.UnsupportedSource, index,
                    $"{clip.Kind} is not a source kind that exists on Android.");
                return false;
        }
    }

    private bool CanRead(AndroidUri uri)
    {
        try
        {
            using var descriptor = Context.ContentResolver?.OpenFileDescriptor(uri, "r");
            return descriptor is not null;
        }
        catch (Java.Lang.SecurityException)
        {
            return false;
        }
        catch (Java.IO.FileNotFoundException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Chapter durations are needed before the player has opened anything: the
    // concatenating source wants a placeholder length per item, and the app
    // wants a total duration it can lay a timeline out with.
    private TimeSpan ReadDuration(AndroidUri uri)
    {
        MediaMetadataRetriever? retriever = null;
        try
        {
            retriever = new MediaMetadataRetriever();
            retriever.SetDataSource(Context, uri);

            var value = retriever.ExtractMetadata(MetadataKey.Duration);
            return long.TryParse(value, out var milliseconds) && milliseconds > 0
                ? TimeSpan.FromMilliseconds(milliseconds)
                : TimeSpan.Zero;
        }
        catch (Exception)
        {
            return TimeSpan.Zero;
        }
        finally
        {
            retriever?.Release();
            retriever?.Dispose();
        }
    }

    public void Play()
    {
        if (_player is not { } player)
            return;

        _displayedFrame = null;
        StartInsideRange();
        player.SetPlaybackSpeed((float)ValueClamp.Speed(VirtualView.Speed));
        player.Play();
        StartTicker();
    }

    /// <summary>
    /// Always false: Media3 has no negative playback rate.
    /// </summary>
    /// <remarks>
    /// <c>SetPlaybackSpeed</c> refuses anything at or below zero, and the
    /// alternative — a timer stepping backwards — is not the same thing. Each
    /// backward step on long-GOP footage decodes from the previous keyframe,
    /// so it would manage a handful of frames a second while claiming to play.
    /// Saying no is the honest answer, and the app can hide its button.
    /// </remarks>
    public bool PlayReverse() => false;

    public void Pause() => _player?.Pause();

    public void Stop()
    {
        if (_player is not { } player)
            return;

        player.Pause();
        player.SeekTo(0);
        _displayedFrame = 0;
        ReportPosition(TimeSpan.Zero, force: true);
    }

    public async Task<bool> SeekExactAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        if (_player is not { } player)
            return false;

        _displayedFrame = null;

        var request = _seeks.Begin();
        _currentSeekGeneration = request.Generation;

        using var registration = cancellationToken.Register(() => _seeks.Complete(request.Generation, false));

        player.SeekParameters = SeekParameters.Exact;
        player.SeekTo((long)position.TotalMilliseconds);

        var reached = await request.Completion.ConfigureAwait(true);

        // The seek moved the picture, so the moment the range ends moved too.
        ScheduleRangeEnd();

        if (reached)
            ReportPosition(TimeSpan.FromMilliseconds(player.CurrentPosition), force: true);

        return reached;
    }

    public async Task<bool> SeekToFrameAsync(long frame, CancellationToken cancellationToken)
    {
        if (_player is not { } player || _timeline is null)
            return false;

        player.Pause();

        var wanted = _frames.ClampFrame(frame);
        var target = _frames.SeekTarget(wanted, FrameSeekSemantics.AtOrAfter);

        var reached = await SeekExactAsync(target, cancellationToken).ConfigureAwait(true);

        if (!reached)
            return false;

        _displayedFrame = wanted;

        // Report where the picture is, not where the seek aimed: the two differ
        // by half a frame here, and an app drawing a frame counter from
        // Position would otherwise disagree with what the viewer reads on screen.
        ReportPosition(_frames.FrameStart(wanted), force: true);
        return true;
    }

    public Task<bool> StepFrameAsync(int frames)
    {
        if (_player is not { } player || _timeline is null)
            return Task.FromResult(false);

        var position = TimeSpan.FromMilliseconds(player.CurrentPosition);
        var current = _displayedFrame ?? _frames.FrameAt(position);

        return SeekToFrameAsync(current + frames, CancellationToken.None);
    }

    public void BeginScrub()
    {
        if (_player is not { } player)
            return;

        _isScrubbing = true;
        _scrubTarget = TimeSpan.FromMilliseconds(player.CurrentPosition);

        // Media3's scrubbing mode drops frame-exactness in favour of keeping up
        // with the drag; EndScrub puts the exactness back.
        player.ScrubbingModeEnabled = true;
        player.SeekParameters = SeekParameters.ClosestSync;
    }

    public void ScrubTo(TimeSpan position)
    {
        if (_player is not { } player || !_isScrubbing)
            return;

        _scrubTarget = position;
        player.SeekTo((long)position.TotalMilliseconds);
    }

    public void EndScrub()
    {
        if (_player is not { } player || !_isScrubbing)
            return;

        _isScrubbing = false;
        player.ScrubbingModeEnabled = false;
        player.SeekParameters = SeekParameters.Exact;

        _ = SeekExactAsync(_scrubTarget, CancellationToken.None);
    }


    private void StartTicker()
    {
        if (_ticker is null || _tick is null)
            return;

        StopTicker();
        _ticker.PostDelayed(_tick, IntervalMilliseconds());
        ScheduleRangeEnd();
    }

    private void StopTicker()
    {
        if (_ticker is not null && _tick is not null)
            _ticker.RemoveCallbacks(_tick);

        CancelRangeEnd();
    }

    private long IntervalMilliseconds() => Math.Max(1, 1000 / _throttle.RateHz);

    private void OnTick()
    {
        if (_player is not { } player || _ticker is null || _tick is null)
            return;

        if (player.IsPlaying)
        {
            var position = TimeSpan.FromMilliseconds(player.CurrentPosition);
            ReportPosition(position, force: false);

            // The safety net: if the booked moment was missed, the range still
            // ends here, a tick late rather than never.
            if (Range.IsUsable(Duration) && position >= Range.End(Duration))
                ReachedRangeEnd();

            _ticker.PostDelayed(_tick, IntervalMilliseconds());
        }
    }

    private void ReportPosition(TimeSpan position, bool force)
    {
        if (_timeline is null)
            return;

        if (force)
            _throttle.Force();

        if (!_throttle.ShouldEmit(position))
            return;

        var (index, local) = _timeline.ToLocal(position);

        // The tracked frame when there is one: Media3 reports the position a
        // seek asked for, which sits half a frame below the frame it displays,
        // so deriving the number from the position would be off by one exactly
        // where it matters most.
        var frame = _displayedFrame ?? _frames.FrameAt(position);

        Report?.OnPositionChanged(position, index, local, frame);
    }

    private void Fail(VideoErrorCode code, int chapterIndex, string message) =>
        Report?.OnMediaFailed(code, chapterIndex, message);

    private void OnPlayerReady()
    {
        if (_player is not { } player || _timeline is null)
            return;

        var reported = player.Duration;
        var duration = reported > 0 ? TimeSpan.FromMilliseconds(reported) : _timeline.Total;

        var format = player.VideoFormat;
        _frameRate = format?.FrameRate > 0 ? format.FrameRate : 0d;

        // Rotation lives in the metadata, not in the coded picture: a phone
        // held upright records 1920x1080 with a quarter turn recorded beside
        // it. NaturalSize is the picture as displayed, which is what an app
        // mapping a tap onto a pixel needs, so the turn is applied here.
        var turned = format is not null && (format.RotationDegrees == 90 || format.RotationDegrees == 270);

        var size = format is not null && format.Width > 0 && format.Height > 0
            ? new Size(turned ? format.Height : format.Width, turned ? format.Width : format.Height)
            : Size.Zero;

        Report?.OnReverseAvailabilityChanged(false);

        // Until the sample tables have been read, frames are counted by the
        // rate the container claims.
        if (!_frames.IsExact)
        {
            _frames = FrameClock.FromRate(_frameRate, duration);
            _ = BuildFrameIndexAsync(_chapterUris.ToArray(), _timeline, _frameRate, _generation);
        }

        Report?.OnMediaOpened(duration, _frameRate, size, _timeline.ChapterDurations);
        Report?.OnStateChanged(player.IsPlaying ? VideoPlayerState.Playing : VideoPlayerState.Ready);

        // Also called when the video size changes, which is when the picture
        // rectangle a zoom is clamped against changes shape.
        ApplyZoom();
    }
}
