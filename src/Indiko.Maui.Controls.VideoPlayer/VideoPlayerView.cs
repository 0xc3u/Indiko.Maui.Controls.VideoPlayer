using Indiko.Maui.Controls.VideoPlayer.Models;
using Indiko.Maui.Controls.VideoPlayer.Timeline;

namespace Indiko.Maui.Controls.VideoPlayer;

/// <summary>
/// A video surface for analysing footage: frame-accurate seeking, frame-by-frame
/// stepping, slow motion, and position events fast enough to drive a map or a
/// timeline. It draws no playback controls of its own — the app builds those.
/// </summary>
public class VideoPlayerView : View, IVideoPlayerViewController
{
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(VideoPlayerSource), typeof(VideoPlayerView));

    public static readonly BindableProperty SpeedProperty = BindableProperty.Create(
        nameof(Speed), typeof(double), typeof(VideoPlayerView), 1d,
        coerceValue: (_, value) => ValueClamp.Speed((double)value!));

    public static readonly BindableProperty IsMutedProperty = BindableProperty.Create(
        nameof(IsMuted), typeof(bool), typeof(VideoPlayerView), false);

    public static readonly BindableProperty VolumeProperty = BindableProperty.Create(
        nameof(Volume), typeof(double), typeof(VideoPlayerView), 1d,
        coerceValue: (_, value) => ValueClamp.Volume((double)value!));

    public static readonly BindableProperty IsLoopingProperty = BindableProperty.Create(
        nameof(IsLooping), typeof(bool), typeof(VideoPlayerView), false);

    public static readonly BindableProperty ScalingModeProperty = BindableProperty.Create(
        nameof(ScalingMode), typeof(VideoScalingMode), typeof(VideoPlayerView), VideoScalingMode.Fit);

    public static readonly BindableProperty PositionUpdateRateProperty = BindableProperty.Create(
        nameof(PositionUpdateRate), typeof(int), typeof(VideoPlayerView), 15,
        coerceValue: (_, value) => ValueClamp.PositionUpdateRate((int)value!));

    public static readonly BindableProperty AllowNetworkAccessProperty = BindableProperty.Create(
        nameof(AllowNetworkAccess), typeof(bool), typeof(VideoPlayerView), false);

    public static readonly BindableProperty AutoPlayProperty = BindableProperty.Create(
        nameof(AutoPlay), typeof(bool), typeof(VideoPlayerView), false);

    public static readonly BindableProperty PauseWhenInBackgroundProperty = BindableProperty.Create(
        nameof(PauseWhenInBackground), typeof(bool), typeof(VideoPlayerView), true);

    // Read-only properties: written by the handler through
    // IVideoPlayerViewController, bindable so a view model can follow them.
    private static readonly BindablePropertyKey PositionPropertyKey = BindableProperty.CreateReadOnly(
        nameof(Position), typeof(TimeSpan), typeof(VideoPlayerView), TimeSpan.Zero);

    private static readonly BindablePropertyKey DurationPropertyKey = BindableProperty.CreateReadOnly(
        nameof(Duration), typeof(TimeSpan), typeof(VideoPlayerView), TimeSpan.Zero);

    private static readonly BindablePropertyKey StatePropertyKey = BindableProperty.CreateReadOnly(
        nameof(State), typeof(VideoPlayerState), typeof(VideoPlayerView), VideoPlayerState.Idle);

    private static readonly BindablePropertyKey IsPlayingPropertyKey = BindableProperty.CreateReadOnly(
        nameof(IsPlaying), typeof(bool), typeof(VideoPlayerView), false);

    private static readonly BindablePropertyKey FrameRatePropertyKey = BindableProperty.CreateReadOnly(
        nameof(FrameRate), typeof(double), typeof(VideoPlayerView), 0d);

    private static readonly BindablePropertyKey NaturalSizePropertyKey = BindableProperty.CreateReadOnly(
        nameof(NaturalSize), typeof(Size), typeof(VideoPlayerView), Size.Zero);

    private static readonly BindablePropertyKey CurrentChapterIndexPropertyKey = BindableProperty.CreateReadOnly(
        nameof(CurrentChapterIndex), typeof(int), typeof(VideoPlayerView), 0);

    public static readonly BindableProperty PositionProperty = PositionPropertyKey.BindableProperty;
    public static readonly BindableProperty DurationProperty = DurationPropertyKey.BindableProperty;
    public static readonly BindableProperty StateProperty = StatePropertyKey.BindableProperty;
    public static readonly BindableProperty IsPlayingProperty = IsPlayingPropertyKey.BindableProperty;
    public static readonly BindableProperty FrameRateProperty = FrameRatePropertyKey.BindableProperty;
    public static readonly BindableProperty NaturalSizeProperty = NaturalSizePropertyKey.BindableProperty;
    public static readonly BindableProperty CurrentChapterIndexProperty = CurrentChapterIndexPropertyKey.BindableProperty;

    /// <summary>
    /// What to play. Assigning a new source tears down the current content and
    /// loads afresh; assigning null releases everything.
    /// </summary>
    public VideoPlayerSource? Source
    {
        get => (VideoPlayerSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Playback rate, 0.1 to 2.0. Values outside that are clamped.</summary>
    public double Speed
    {
        get => (double)GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty);
        set => SetValue(IsMutedProperty, value);
    }

    /// <summary>Volume, 0 to 1.</summary>
    public double Volume
    {
        get => (double)GetValue(VolumeProperty);
        set => SetValue(VolumeProperty, value);
    }

    public bool IsLooping
    {
        get => (bool)GetValue(IsLoopingProperty);
        set => SetValue(IsLoopingProperty, value);
    }

    public VideoScalingMode ScalingMode
    {
        get => (VideoScalingMode)GetValue(ScalingModeProperty);
        set => SetValue(ScalingModeProperty, value);
    }

    /// <summary>
    /// How often <see cref="PositionChanged"/> fires while playing, in hertz.
    /// Clamped to 10–30: below 10 a playhead visibly steps, and above 30 there
    /// is nothing more to report.
    /// </summary>
    public int PositionUpdateRate
    {
        get => (int)GetValue(PositionUpdateRateProperty);
        set => SetValue(PositionUpdateRateProperty, value);
    }

    /// <summary>
    /// iOS only: whether a photo-library asset may be downloaded from iCloud
    /// when no local original exists. Off by default — a download can be
    /// gigabytes on a mobile connection, so the app should ask first and then
    /// set this.
    /// </summary>
    public bool AllowNetworkAccess
    {
        get => (bool)GetValue(AllowNetworkAccessProperty);
        set => SetValue(AllowNetworkAccessProperty, value);
    }

    /// <summary>Whether to start playing as soon as the source is open.</summary>
    public bool AutoPlay
    {
        get => (bool)GetValue(AutoPlayProperty);
        set => SetValue(AutoPlayProperty, value);
    }

    /// <summary>
    /// Whether to pause when the app leaves the foreground. Playback never
    /// resumes by itself: the control runs no foreground service and no
    /// background audio session.
    /// </summary>
    public bool PauseWhenInBackground
    {
        get => (bool)GetValue(PauseWhenInBackgroundProperty);
        set => SetValue(PauseWhenInBackgroundProperty, value);
    }

    /// <summary>Current position on the whole timeline.</summary>
    public TimeSpan Position
    {
        get => (TimeSpan)GetValue(PositionProperty);
        private set => SetValue(PositionPropertyKey, value);
    }

    /// <summary>Length of the whole timeline: the sum of the chapters.</summary>
    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        private set => SetValue(DurationPropertyKey, value);
    }

    public VideoPlayerState State
    {
        get => (VideoPlayerState)GetValue(StateProperty);
        private set => SetValue(StatePropertyKey, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        private set => SetValue(IsPlayingPropertyKey, value);
    }

    /// <summary>Frames per second, or 0 while unknown.</summary>
    public double FrameRate
    {
        get => (double)GetValue(FrameRateProperty);
        private set => SetValue(FrameRatePropertyKey, value);
    }

    /// <summary>Picture size in pixels, or <see cref="Size.Zero"/> while unknown.</summary>
    public Size NaturalSize
    {
        get => (Size)GetValue(NaturalSizeProperty);
        private set => SetValue(NaturalSizePropertyKey, value);
    }

    /// <summary>Which clip of <see cref="Source"/> is playing.</summary>
    public int CurrentChapterIndex
    {
        get => (int)GetValue(CurrentChapterIndexProperty);
        private set => SetValue(CurrentChapterIndexPropertyKey, value);
    }

    /// <summary>Length of each chapter, in playback order. Empty until the source is open.</summary>
    public IReadOnlyList<TimeSpan> ChapterDurations { get; private set; } = [];

    /// <summary>The player moved.</summary>
    public event EventHandler<VideoPositionChangedEventArgs>? PositionChanged;

    /// <summary>The source is loaded; duration, frame rate and size are known.</summary>
    public event EventHandler<VideoOpenedEventArgs>? MediaOpened;

    /// <summary>Opening or playing failed. Raised instead of throwing.</summary>
    public event EventHandler<VideoFailedEventArgs>? MediaFailed;

    /// <summary>Reached the end of the timeline.</summary>
    public event EventHandler? PlaybackEnded;

    public event EventHandler<VideoStateChangedEventArgs>? StateChanged;

    /// <summary>iOS: a stale bookmark was renewed and should be stored by the app.</summary>
    public event EventHandler<VideoBookmarkRefreshedEventArgs>? BookmarkRefreshed;

    /// <summary>iOS: progress of an iCloud download.</summary>
    public event EventHandler<VideoDownloadProgressEventArgs>? DownloadProgress;

    private IVideoPlayerController? Controller => Handler as IVideoPlayerController;

    /// <summary>Starts or resumes playback.</summary>
    public void Play()
    {
        VerifyMainThread();
        Controller?.Play();
    }

    /// <summary>Pauses, keeping the position.</summary>
    public void Pause()
    {
        VerifyMainThread();
        Controller?.Pause();
    }

    /// <summary>Pauses and returns to the start.</summary>
    public void Stop()
    {
        VerifyMainThread();
        Controller?.Stop();
    }

    /// <summary>
    /// Seeks to an exact position — frame-accurate, not to the nearest keyframe.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the player reached the position; <c>false</c> when a
    /// newer seek overtook this one, the call was cancelled, or no source is
    /// loaded. A caller drawing a playhead should ignore its own target on
    /// <c>false</c>, because the picture went somewhere else.
    /// </returns>
    public Task<bool> SeekExactAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        VerifyMainThread();

        if (Controller is not { } controller)
            return Task.FromResult(false);

        return controller.SeekExactAsync(ValueClamp.Position(position, Duration), cancellationToken);
    }

    /// <summary>
    /// Steps <paramref name="frames"/> frames forward (positive) or back
    /// (negative), pausing first. Steps across chapter boundaries.
    /// </summary>
    public Task<bool> StepFrameAsync(int frames)
    {
        VerifyMainThread();
        return Controller is { } controller ? controller.StepFrameAsync(frames) : Task.FromResult(false);
    }

    /// <summary>
    /// Begins a scrub. Between this and <see cref="EndScrub"/> seeks are fast
    /// and approximate, which is what keeps a dragged slider responsive.
    /// </summary>
    public void BeginScrub()
    {
        VerifyMainThread();
        Controller?.BeginScrub();
    }

    /// <summary>Moves during a scrub. Approximate; no completion to await.</summary>
    public void ScrubTo(TimeSpan position)
    {
        VerifyMainThread();
        Controller?.ScrubTo(ValueClamp.Position(position, Duration));
    }

    /// <summary>Ends a scrub with an exact seek to the position last scrubbed to.</summary>
    public void EndScrub()
    {
        VerifyMainThread();
        Controller?.EndScrub();
    }

    void IVideoPlayerViewController.OnPositionChanged(TimeSpan position, int chapterIndex, TimeSpan chapterPosition)
    {
        Position = position;
        CurrentChapterIndex = chapterIndex;
        PositionChanged?.Invoke(this, new VideoPositionChangedEventArgs(position, chapterIndex, chapterPosition));
    }

    void IVideoPlayerViewController.OnMediaOpened(
        TimeSpan duration, double frameRate, Size naturalSize, IReadOnlyList<TimeSpan> chapterDurations)
    {
        Duration = duration;
        FrameRate = frameRate;
        NaturalSize = naturalSize;
        ChapterDurations = chapterDurations;

        MediaOpened?.Invoke(this, new VideoOpenedEventArgs(duration, frameRate, naturalSize, chapterDurations));
    }

    void IVideoPlayerViewController.OnMediaFailed(VideoErrorCode errorCode, int chapterIndex, string message)
    {
        ((IVideoPlayerViewController)this).OnStateChanged(VideoPlayerState.Failed);
        MediaFailed?.Invoke(this, new VideoFailedEventArgs(errorCode, chapterIndex, message));
    }

    void IVideoPlayerViewController.OnPlaybackEnded()
    {
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    void IVideoPlayerViewController.OnStateChanged(VideoPlayerState state)
    {
        var previous = State;
        if (previous == state)
            return;

        State = state;
        IsPlaying = state == VideoPlayerState.Playing;

        StateChanged?.Invoke(this, new VideoStateChangedEventArgs(previous, state));
    }

    void IVideoPlayerViewController.OnBookmarkRefreshed(int chapterIndex, string newBookmark)
    {
        BookmarkRefreshed?.Invoke(this, new VideoBookmarkRefreshedEventArgs(chapterIndex, newBookmark));
    }

    void IVideoPlayerViewController.OnDownloadProgress(int chapterIndex, double progress)
    {
        DownloadProgress?.Invoke(this, new VideoDownloadProgressEventArgs(chapterIndex, progress));
    }

    // The native players are not thread-safe and both platforms require their
    // playback objects to be driven from the main thread. Failing loudly here
    // turns a race that shows up as a frozen picture or a crash deep in
    // AVFoundation into a stack trace pointing at the offending call.
    private void VerifyMainThread()
    {
        var dispatcher = Dispatcher ?? Application.Current?.Dispatcher;

        if (dispatcher is not null && dispatcher.IsDispatchRequired)
        {
            throw new InvalidOperationException(
                $"{nameof(VideoPlayerView)} must be driven from the main thread.");
        }
    }
}
