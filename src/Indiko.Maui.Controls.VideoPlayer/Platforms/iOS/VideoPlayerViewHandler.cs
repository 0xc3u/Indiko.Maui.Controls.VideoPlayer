using AVFoundation;
using CoreMedia;
using Foundation;
using Indiko.Maui.Controls.VideoPlayer.Models;
using Indiko.Maui.Controls.VideoPlayer.Timeline;
using Microsoft.Maui.Handlers;
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
    private KeyPathObserver? _statusObserver;
    private KeyPathObserver? _timeControlObserver;
    private CancellationTokenSource? _loading;

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

        _timeControlObserver = new KeyPathObserver(_player, "timeControlStatus", OnTimeControlStatusChanged);

        // 1/30 s is the finest granularity worth observing; PositionThrottle
        // decides how much of it the app actually hears about.
        _timeObserver = _player.AddPeriodicTimeObserver(
            new CMTime(1, 30), CoreFoundation.DispatchQueue.MainQueue, OnTimeObserved);
    }

    protected override void DisconnectHandler(PlayerContainerView platformView)
    {
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;

        _seeks.CancelPending();

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
        }
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
