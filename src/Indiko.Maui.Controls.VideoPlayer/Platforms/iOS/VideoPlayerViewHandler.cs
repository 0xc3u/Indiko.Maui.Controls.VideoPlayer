using AVFoundation;
using CoreGraphics;
using CoreMedia;
using Foundation;
using Indiko.Maui.Controls.VideoPlayer.Models;
using Indiko.Maui.Controls.VideoPlayer.Timeline;
using Microsoft.Maui.Handlers;
using UIKit;
using Size = Microsoft.Maui.Graphics.Size;

namespace Indiko.Maui.Controls.VideoPlayer.Platforms.iOS;

/// <summary>
/// Drives an <see cref="AVPlayer"/> on a bare player layer, with no system
/// playback UI.
/// </summary>
public class VideoPlayerViewHandler
    : ViewHandler<VideoPlayerView, PlayerContainerView>, IVideoPlayerController
{
    public static readonly IPropertyMapper<VideoPlayerView, VideoPlayerViewHandler> Mapper =
        new PropertyMapper<VideoPlayerView, VideoPlayerViewHandler>(ViewMapper)
        {
            [nameof(VideoPlayerView.Source)] = MapSource,
            [nameof(VideoPlayerView.Speed)] = MapSpeed,
            [nameof(VideoPlayerView.IsMuted)] = MapVolume,
            [nameof(VideoPlayerView.Volume)] = MapVolume,
            [nameof(VideoPlayerView.ScalingMode)] = MapScalingMode,
            [nameof(VideoPlayerView.ZoomFactor)] = MapZoom,
            [nameof(VideoPlayerView.ZoomOffset)] = MapZoom,
            [nameof(VideoPlayerView.MaximumZoom)] = MapZoom,
            [nameof(VideoPlayerView.IsPinchToZoomEnabled)] = MapZoomGestures,
            [nameof(VideoPlayerView.PositionUpdateRate)] = MapPositionUpdateRate,
        };

    // The composition is built on a 600-tick timescale, the traditional choice
    // because it divides evenly by 24, 25, 30, 50 and 60. Seeks are expressed
    // in it, so a frame boundary at any common rate is representable exactly.
    private const int TimeScale = 600;

    private readonly SeekCoordinator _seeks = new();
    private readonly List<ResolvedClip> _clips = [];

    private AVPlayer? _player;
    private AVPlayerItem? _item;
    private NSObject? _timeObserver;
    private NSObject? _endObserver;
    private NSObject? _resignObserver;
    private KeyPathObserver? _statusObserver;
    private KeyPathObserver? _timeControlObserver;
    private CancellationTokenSource? _loading;

    private UIPinchGestureRecognizer? _pinch;
    private UIPanGestureRecognizer? _drag;
    private bool _reportingZoom;

    private ChapterTimeline? _timeline;
    private PositionThrottle _throttle = new(15);
    private double _frameRate;
    private Size _naturalSize = Size.Zero;
    private bool _isScrubbing;
    private TimeSpan _scrubTarget;
    private bool _opened;
    private int _generation;

    public VideoPlayerViewHandler() : base(Mapper) { }

    public VideoPlayerViewHandler(IPropertyMapper? mapper) : base(mapper ?? Mapper) { }

    private IVideoPlayerViewController? Report => VirtualView;

    protected override PlayerContainerView CreatePlatformView() => new();

    protected override void ConnectHandler(PlayerContainerView platformView)
    {
        base.ConnectHandler(platformView);

        _player = new AVPlayer();
        platformView.Player = _player;

        // A new size means a new picture rectangle, and a pan that was at the
        // edge of the old one is now either short of the new edge or past it.
        platformView.DidLayout += OnPlatformLayout;

        _timeControlObserver = new KeyPathObserver(_player, "timeControlStatus", OnTimeControlStatusChanged);

        // 1/30 s is the finest granularity worth observing; PositionThrottle
        // decides how much of it the app actually hears about.
        _timeObserver = _player.AddPeriodicTimeObserver(
            new CMTime(1, 30), CoreFoundation.DispatchQueue.MainQueue, OnTimeObserved);

        // Leaving the app pauses, and nothing resumes by itself: the control
        // runs no background audio session and no now-playing entry, so
        // continuing would be audio the user cannot see or stop.
        _resignObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            UIApplication.WillResignActiveNotification,
            _ =>
            {
                if (VirtualView?.PauseWhenInBackground == true)
                    _player?.Pause();
            });
    }

    protected override void DisconnectHandler(PlayerContainerView platformView)
    {
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;

        _seeks.CancelPending();

        platformView.DidLayout -= OnPlatformLayout;
        RemoveZoomGestures(platformView);

        if (_player is { } player)
        {
            if (_timeObserver is not null)
            {
                player.RemoveTimeObserver(_timeObserver);
                _timeObserver.Dispose();
                _timeObserver = null;
            }

            player.Pause();
            player.ReplaceCurrentItemWithPlayerItem(null);
        }

        if (_resignObserver is not null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(_resignObserver);
            _resignObserver.Dispose();
            _resignObserver = null;
        }

        _timeControlObserver?.Stop();
        _timeControlObserver?.Dispose();
        _timeControlObserver = null;

        ReleaseItem();

        platformView.Player = null;

        _player?.Dispose();
        _player = null;

        base.DisconnectHandler(platformView);
    }

    private void ReleaseItem()
    {
        _statusObserver?.Stop();
        _statusObserver?.Dispose();
        _statusObserver = null;

        if (_endObserver is not null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(_endObserver);
            _endObserver.Dispose();
            _endObserver = null;
        }

        _item?.CancelPendingSeeks();
        _item?.Dispose();
        _item = null;

        // Each clip holds a security-scoped access open for as long as it is
        // playable; this is where those are handed back.
        foreach (var clip in _clips)
            clip.Dispose();

        _clips.Clear();

        _timeline = null;
        _opened = false;
    }

    private static void MapSource(VideoPlayerViewHandler handler, VideoPlayerView view) =>
        _ = handler.LoadSourceAsync(view.Source);

    private static void MapSpeed(VideoPlayerViewHandler handler, VideoPlayerView view) =>
        handler.ApplySpeed();

    private static void MapVolume(VideoPlayerViewHandler handler, VideoPlayerView view)
    {
        if (handler._player is { } player)
        {
            player.Muted = view.IsMuted;
            player.Volume = (float)ValueClamp.Volume(view.Volume);
        }
    }

    private static void MapScalingMode(VideoPlayerViewHandler handler, VideoPlayerView view)
    {
        if (handler.PlatformView is { } platformView)
        {
            platformView.VideoGravity = view.ScalingMode switch
            {
                VideoScalingMode.Fill => AVLayerVideoGravity.ResizeAspectFill,
                VideoScalingMode.Stretch => AVLayerVideoGravity.Resize,
                _ => AVLayerVideoGravity.ResizeAspect,
            };

            // Fit draws a letterboxed picture and Fill a cropped one, so the
            // room there is to pan changed with the gravity.
            handler.ApplyZoom();
        }
    }

    private static void MapZoom(VideoPlayerViewHandler handler, VideoPlayerView view) =>
        handler.ApplyZoom();

    private static void MapZoomGestures(VideoPlayerViewHandler handler, VideoPlayerView view)
    {
        if (handler.PlatformView is not { } platformView)
            return;

        if (view.IsPinchToZoomEnabled)
            handler.AddZoomGestures(platformView);
        else
            handler.RemoveZoomGestures(platformView);
    }

    private void OnPlatformLayout(object? sender, EventArgs e) => ApplyZoom();

    /// <summary>
    /// Puts the view's zoom on the picture, clamped to what the picture holds,
    /// and reports back whatever survived the clamp.
    /// </summary>
    private void ApplyZoom()
    {
        if (PlatformView is not { } platformView || VirtualView is not { } view)
            return;

        Apply(ZoomMath.Clamp(Requested(view), Measure(platformView), view.MaximumZoom));
    }

    private void Apply(ZoomState state)
    {
        if (PlatformView is not { } platformView)
            return;

        platformView.ApplyZoom(state.Factor, state.OffsetX, state.OffsetY);

        // The view wrote the request; this is the answer. The guard is what
        // stops that answer from coming back round as another request.
        if (_reportingZoom)
            return;

        _reportingZoom = true;

        try
        {
            Report?.OnZoomChanged(state.Factor, new Point(state.OffsetX, state.OffsetY));
        }
        finally
        {
            _reportingZoom = false;
        }
    }

    private static ZoomState Requested(VideoPlayerView view) =>
        new(view.ZoomFactor, view.ZoomOffset.X, view.ZoomOffset.Y);

    private static ZoomBounds Measure(PlayerContainerView platformView)
    {
        var viewport = platformView.Bounds;
        var content = platformView.ContentRect;

        // Before the first frame there is no video rectangle to measure. The
        // view's own size is the closest honest guess, and the next layout or
        // the first frame corrects it.
        if (content.IsEmpty)
            content = viewport;

        return new ZoomBounds(content.Width, content.Height, viewport.Width, viewport.Height);
    }

    private void AddZoomGestures(PlayerContainerView platformView)
    {
        if (_pinch is not null)
            return;

        _pinch = new UIPinchGestureRecognizer(OnPinch)
        {
            // The app may have its own recognisers on the player; claiming
            // exclusivity would break them.
            ShouldRecognizeSimultaneously = (_, _) => true,
        };

        _drag = new UIPanGestureRecognizer(OnDrag)
        {
            // One finger only: a pinch already moves the picture with the point
            // between the fingers, and a second translation on top of that
            // would move it twice as far as the hand.
            MinimumNumberOfTouches = 1,
            MaximumNumberOfTouches = 1,
            ShouldRecognizeSimultaneously = (_, _) => true,

            // Unmagnified there is nothing to pan, and a recogniser that began
            // anyway would swallow the drag that scrolls the page the player
            // sits on.
            ShouldBegin = _ => CanPan(),
        };

        platformView.AddGestureRecognizer(_pinch);
        platformView.AddGestureRecognizer(_drag);
    }

    private void RemoveZoomGestures(PlayerContainerView platformView)
    {
        if (_pinch is not null)
        {
            platformView.RemoveGestureRecognizer(_pinch);
            _pinch.Dispose();
            _pinch = null;
        }

        if (_drag is not null)
        {
            platformView.RemoveGestureRecognizer(_drag);
            _drag.Dispose();
            _drag = null;
        }
    }

    private bool CanPan()
    {
        if (PlatformView is not { } platformView || VirtualView is not { } view)
            return false;

        var bounds = Measure(platformView);
        var factor = view.ZoomFactor;

        return ZoomMath.OffsetLimit(bounds.ContentWidth, bounds.ViewportWidth, factor) > 0 ||
               ZoomMath.OffsetLimit(bounds.ContentHeight, bounds.ViewportHeight, factor) > 0;
    }

    private void OnPinch(UIPinchGestureRecognizer recognizer)
    {
        if (PlatformView is not { } platformView || VirtualView is not { } view)
            return;

        if (recognizer.State is not (UIGestureRecognizerState.Began or UIGestureRecognizerState.Changed))
            return;

        var focus = recognizer.LocationInView(platformView);
        var bounds = platformView.Bounds;

        var state = ZoomMath.ScaleBy(
            Requested(view),
            recognizer.Scale,
            focus.X - bounds.GetMidX(),
            focus.Y - bounds.GetMidY(),
            Measure(platformView),
            view.MaximumZoom);

        // Scale is cumulative from the start of the gesture; resetting it turns
        // it into the step since the last callback, which is what composes with
        // a factor the app may have changed underneath.
        recognizer.Scale = 1;

        Apply(state);
    }

    private void OnDrag(UIPanGestureRecognizer recognizer)
    {
        if (PlatformView is not { } platformView || VirtualView is not { } view)
            return;

        if (recognizer.State is not (UIGestureRecognizerState.Began or UIGestureRecognizerState.Changed))
            return;

        var moved = recognizer.TranslationInView(platformView);
        recognizer.SetTranslation(CGPoint.Empty, platformView);

        Apply(ZoomMath.PanBy(
            Requested(view), moved.X, moved.Y, Measure(platformView), view.MaximumZoom));
    }

    private static void MapPositionUpdateRate(VideoPlayerViewHandler handler, VideoPlayerView view) =>
        handler._throttle = new PositionThrottle(view.PositionUpdateRate);

    private void ApplySpeed()
    {
        if (_player is not { } player)
            return;

        var speed = (float)ValueClamp.Speed(VirtualView.Speed);

        if (OperatingSystem.IsIOSVersionAtLeast(16))
            player.DefaultRate = speed;

        // Writing Rate while paused would start playback, which is not what
        // setting a speed means.
        if (player.Rate != 0)
            player.Rate = speed;
    }

    private async Task LoadSourceAsync(VideoPlayerSource? source)
    {
        if (_player is not { } player)
            return;

        // A user tapping through a list of clips starts several loads; only the
        // newest may reach the view, however the earlier ones finish.
        var generation = ++_generation;

        _loading?.Cancel();
        _loading?.Dispose();
        _loading = new CancellationTokenSource();
        var token = _loading.Token;

        _seeks.CancelPending();
        player.Pause();
        player.ReplaceCurrentItemWithPlayerItem(null);
        ReleaseItem();
        _throttle.Reset();

        if (source is null)
        {
            Report?.OnStateChanged(VideoPlayerState.Idle);
            return;
        }

        Report?.OnStateChanged(VideoPlayerState.Opening);

        var composition = new AVMutableComposition();
        var videoTrack = composition.AddMutableTrack(AVMediaTypes.Video.GetConstant()!, 0);
        var audioTrack = composition.AddMutableTrack(AVMediaTypes.Audio.GetConstant()!, 0);

        if (videoTrack is null)
        {
            Report?.OnMediaFailed(
                VideoErrorCode.Unknown, -1, "The composition could not take a video track.");
            return;
        }

        var durations = new List<TimeSpan>(source.Clips.Count);
        var cursor = CMTime.Zero;
        var transformTaken = false;

        for (var index = 0; index < source.Clips.Count; index++)
        {
            var chapter = index;
            var resolution = await ClipResolver.ResolveAsync(
                source.Clips[index],
                VirtualView.AllowNetworkAccess,
                progress => Report?.OnDownloadProgress(chapter, progress),
                token).ConfigureAwait(true);

            if (generation != _generation || token.IsCancellationRequested)
            {
                resolution.Clip?.Dispose();
                return;
            }

            if (resolution.Clip is not { } clip)
            {
                Report?.OnMediaFailed(resolution.Error ?? VideoErrorCode.Unknown, chapter, resolution.Message);
                return;
            }

            _clips.Add(clip);

            if (clip.RenewedBookmark is { } renewed)
                Report?.OnBookmarkRefreshed(chapter, renewed);

            var videoTracks = await clip.Asset
                .LoadTracksWithMediaTypeAsync(AVMediaTypes.Video.GetConstant()!)
                .ConfigureAwait(true);

            if (generation != _generation)
                return;

            if (videoTracks is null || videoTracks.Count == 0)
            {
                Report?.OnMediaFailed(
                    VideoErrorCode.UnsupportedFormat, chapter, "The clip has no video track.");
                return;
            }

            var sourceVideo = videoTracks[0];
            var range = sourceVideo.TimeRange;

            videoTrack.InsertTimeRange(range, sourceVideo, cursor, out var videoError);
            if (videoError is not null)
            {
                Report?.OnMediaFailed(
                    VideoErrorCode.UnsupportedFormat, chapter, "The clip could not be added to the timeline.");
                return;
            }

            // A chapter without audio is normal (an action camera in a housing,
            // or a muted export) and must not stop the composition.
            var audioTracks = await clip.Asset
                .LoadTracksWithMediaTypeAsync(AVMediaTypes.Audio.GetConstant()!)
                .ConfigureAwait(true);

            if (audioTracks is not null && audioTracks.Count > 0)
                audioTrack?.InsertTimeRange(range, audioTracks[0], cursor, out _);

            if (!transformTaken)
            {
                // Rotation lives in the track's transform, not in the pixels. A
                // composition starts with the identity transform, so footage
                // recorded in portrait would otherwise play on its side.
                videoTrack.PreferredTransform = sourceVideo.PreferredTransform;
                _naturalSize = NaturalSizeOf(sourceVideo);
                _frameRate = sourceVideo.NominalFrameRate > 0 ? sourceVideo.NominalFrameRate : 0d;
                transformTaken = true;
            }

            durations.Add(TimeSpan.FromSeconds(range.Duration.Seconds));
            cursor = CMTime.Add(cursor, range.Duration);
        }

        if (generation != _generation)
            return;

        _timeline = new ChapterTimeline(durations);

        var item = new AVPlayerItem(composition)
        {
            // Spectral keeps the pitch where it belongs when the rate changes,
            // so audio at 0.25x is still recognisable rather than a growl.
            AudioTimePitchAlgorithm = AVAudioTimePitchAlgorithm.Spectral,
        };

        _item = item;
        _statusObserver = new KeyPathObserver(item, "status", OnItemStatusChanged);

        _endObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            AVPlayerItem.DidPlayToEndTimeNotification, OnPlayedToEnd, item);

        player.ReplaceCurrentItemWithPlayerItem(item);

        MapVolume(this, VirtualView);
        ApplySpeed();

        if (VirtualView.AutoPlay)
            player.Play();
    }

    private static Size NaturalSizeOf(AVAssetTrack track)
    {
        var size = track.NaturalSize;
        var transform = track.PreferredTransform;

        // A 90 degree rotation swaps the reported width and height; the app
        // wants the size it will see on screen.
        var rotated = Math.Abs(transform.B) > 0.5 && Math.Abs(transform.C) > 0.5;

        return rotated
            ? new Size(size.Height, size.Width)
            : new Size(size.Width, size.Height);
    }

    private void OnItemStatusChanged()
    {
        if (_item is not { } item || _timeline is null)
            return;

        switch (item.Status)
        {
            case AVPlayerItemStatus.ReadyToPlay when !_opened:
                _opened = true;

                var duration = item.Duration.IsNumeric
                    ? TimeSpan.FromSeconds(item.Duration.Seconds)
                    : _timeline.Total;

                Report?.OnMediaOpened(duration, _frameRate, _naturalSize, _timeline.ChapterDurations);

                // The video rectangle only exists once there is a frame, so a
                // zoom set before this was clamped against the view's size.
                ApplyZoom();
                Report?.OnStateChanged(
                    _player?.Rate != 0 ? VideoPlayerState.Playing : VideoPlayerState.Ready);
                break;

            case AVPlayerItemStatus.Failed:
                Report?.OnMediaFailed(VideoErrorCode.UnsupportedFormat, -1, "The item could not be played.");
                break;
        }
    }

    private void OnTimeControlStatusChanged()
    {
        if (_player is not { } player || !_opened)
            return;

        var state = player.TimeControlStatus switch
        {
            AVPlayerTimeControlStatus.Playing => VideoPlayerState.Playing,
            AVPlayerTimeControlStatus.WaitingToPlayAtSpecifiedRate => VideoPlayerState.Buffering,
            _ => VideoPlayerState.Paused,
        };

        Report?.OnStateChanged(state);
    }

    private void OnPlayedToEnd(NSNotification notification)
    {
        Report?.OnStateChanged(VideoPlayerState.Ended);
        Report?.OnPlaybackEnded();

        if (VirtualView.IsLooping && _player is { } player)
        {
            player.Seek(CMTime.Zero, CMTime.Zero, CMTime.Zero);
            player.Play();
        }
    }

    private void OnTimeObserved(CMTime time)
    {
        if (!time.IsNumeric)
            return;

        ReportPosition(TimeSpan.FromSeconds(time.Seconds), force: false);
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
        Report?.OnPositionChanged(position, index, local);
    }

    public void Play()
    {
        if (_player is not { } player)
            return;

        ApplySpeed();

        // DefaultRate only applies from iOS 16; below it, playing at a rate
        // means setting the rate.
        if (OperatingSystem.IsIOSVersionAtLeast(16))
            player.Play();
        else
            player.Rate = (float)ValueClamp.Speed(VirtualView.Speed);
    }

    public void Pause() => _player?.Pause();

    public void Stop()
    {
        if (_player is not { } player)
            return;

        player.Pause();
        player.Seek(CMTime.Zero, CMTime.Zero, CMTime.Zero);
        ReportPosition(TimeSpan.Zero, force: true);
    }

    public async Task<bool> SeekExactAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        if (_player is not { } player || _item is null)
            return false;

        var request = _seeks.Begin();
        var generation = request.Generation;

        using var registration = cancellationToken.Register(() => _seeks.Complete(generation, false));

        // Zero tolerance either side is what makes this exact rather than a
        // jump to the nearest keyframe, which at a 2 s keyframe interval can be
        // sixty frames away from what the caller asked for.
        player.Seek(
            CMTime.FromSeconds(position.TotalSeconds, TimeScale),
            CMTime.Zero,
            CMTime.Zero,
            finished => _seeks.Complete(generation, finished));

        var reached = await request.Completion.ConfigureAwait(true);

        if (reached)
            ReportPosition(position, force: true);

        return reached;
    }

    public async Task<bool> StepFrameAsync(int frames)
    {
        if (_player is not { } player || _item is not { } item || _timeline is null)
            return false;

        player.Pause();

        // StepByCount walks the decoded frames and is exact by construction,
        // but it refuses to cross the ends of the item, and AVFoundation does
        // not promise it for every codec. The seek is the fallback and the only
        // route that works across a chapter seam the stepper will not cross.
        var canStep = frames > 0 ? item.CanStepForward : item.CanStepBackward;

        if (canStep && frames != 0)
        {
            item.StepByCount(frames);

            var stepped = TimeSpan.FromSeconds(player.CurrentTime.Seconds);
            ReportPosition(stepped, force: true);
            return true;
        }

        var position = TimeSpan.FromSeconds(player.CurrentTime.Seconds);
        var target = FrameMath.StepTarget(position, _frameRate, frames, _timeline.Total);

        return await SeekExactAsync(target, CancellationToken.None).ConfigureAwait(true);
    }

    public void BeginScrub()
    {
        if (_player is not { } player)
            return;

        _isScrubbing = true;
        _scrubTarget = TimeSpan.FromSeconds(player.CurrentTime.Seconds);
        player.Pause();
    }

    public void ScrubTo(TimeSpan position)
    {
        if (_player is not { } player || !_isScrubbing)
            return;

        _scrubTarget = position;

        // A tenth of a second of tolerance lets AVFoundation answer from the
        // keyframes it already has, which is what keeps a dragged slider from
        // stuttering. EndScrub puts the exactness back.
        var tolerance = CMTime.FromSeconds(0.1, TimeScale);

        _item?.CancelPendingSeeks();
        player.Seek(CMTime.FromSeconds(position.TotalSeconds, TimeScale), tolerance, tolerance);
    }

    public void EndScrub()
    {
        if (!_isScrubbing)
            return;

        _isScrubbing = false;
        _ = SeekExactAsync(_scrubTarget, CancellationToken.None);
    }
}
