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

    public static readonly BindableProperty InPointProperty = BindableProperty.Create(
        nameof(InPoint), typeof(TimeSpan?), typeof(VideoPlayerView), null);

    public static readonly BindableProperty OutPointProperty = BindableProperty.Create(
        nameof(OutPoint), typeof(TimeSpan?), typeof(VideoPlayerView), null);

    public static readonly BindableProperty ZoomFactorProperty = BindableProperty.Create(
        nameof(ZoomFactor), typeof(double), typeof(VideoPlayerView), 1d, BindingMode.TwoWay,
        coerceValue: (bindable, value) =>
            ValueClamp.Zoom((double)value!, ((VideoPlayerView)bindable).MaximumZoom),
        propertyChanged: OnZoomPropertyChanged);

    public static readonly BindableProperty ZoomOffsetProperty = BindableProperty.Create(
        nameof(ZoomOffset), typeof(Point), typeof(VideoPlayerView), Point.Zero, BindingMode.TwoWay,
        propertyChanged: OnZoomPropertyChanged);

    public static readonly BindableProperty MaximumZoomProperty = BindableProperty.Create(
        nameof(MaximumZoom), typeof(double), typeof(VideoPlayerView), ValueClamp.DefaultMaximumZoom,
        coerceValue: (_, value) => ValueClamp.MaximumZoom((double)value!),
        // Lowering the ceiling has to pull the current factor down with it,
        // otherwise the picture stays magnified past what the app now allows.
        propertyChanged: (bindable, _, _) => bindable.CoerceValue(ZoomFactorProperty));

    public static readonly BindableProperty IsPinchToZoomEnabledProperty = BindableProperty.Create(
        nameof(IsPinchToZoomEnabled), typeof(bool), typeof(VideoPlayerView), false);

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

    private static readonly BindablePropertyKey CanPlayReversePropertyKey = BindableProperty.CreateReadOnly(
        nameof(CanPlayReverse), typeof(bool), typeof(VideoPlayerView), false);

    private static readonly BindablePropertyKey FrameCountPropertyKey = BindableProperty.CreateReadOnly(
        nameof(FrameCount), typeof(long), typeof(VideoPlayerView), 0L);

    private static readonly BindablePropertyKey CurrentFramePropertyKey = BindableProperty.CreateReadOnly(
        nameof(CurrentFrame), typeof(long), typeof(VideoPlayerView), 0L);

    private static readonly BindablePropertyKey CurrentChapterIndexPropertyKey = BindableProperty.CreateReadOnly(
        nameof(CurrentChapterIndex), typeof(int), typeof(VideoPlayerView), 0);

    public static readonly BindableProperty PositionProperty = PositionPropertyKey.BindableProperty;
    public static readonly BindableProperty DurationProperty = DurationPropertyKey.BindableProperty;
    public static readonly BindableProperty StateProperty = StatePropertyKey.BindableProperty;
    public static readonly BindableProperty IsPlayingProperty = IsPlayingPropertyKey.BindableProperty;
    public static readonly BindableProperty FrameRateProperty = FrameRatePropertyKey.BindableProperty;
    public static readonly BindableProperty NaturalSizeProperty = NaturalSizePropertyKey.BindableProperty;
    public static readonly BindableProperty CanPlayReverseProperty = CanPlayReversePropertyKey.BindableProperty;
    public static readonly BindableProperty FrameCountProperty = FrameCountPropertyKey.BindableProperty;
    public static readonly BindableProperty CurrentFrameProperty = CurrentFramePropertyKey.BindableProperty;
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

    /// <summary>
    /// Playback rate, 0.1 to 16. Values outside that are clamped.
    /// </summary>
    /// <remarks>
    /// Always positive: direction is <see cref="PlayReverse"/>, not a sign, so
    /// that a platform which cannot run backwards can say so instead of
    /// silently standing still. What the decoder keeps up with at the top of
    /// the range is its own business — both platforms drop frames rather than
    /// fall behind — and neither carries audio far past 2.
    /// </remarks>
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
    /// Where playback begins, or null for the start of the media.
    /// </summary>
    /// <remarks>
    /// An in and an out point confine <em>playback</em>, not seeking: the
    /// picture stops at <see cref="OutPoint"/>, or jumps back here when
    /// <see cref="IsLooping"/> is on, and an app can still seek anywhere it
    /// likes while the range is set. Pressing play from outside the range
    /// starts at this point.
    /// <para>
    /// Two seconds on repeat is the working tool of movement analysis, which
    /// is why this is a rule about playback rather than a trimmed source: it
    /// costs nothing to set, change or clear while the picture is running.
    /// </para>
    /// </remarks>
    public TimeSpan? InPoint
    {
        get => (TimeSpan?)GetValue(InPointProperty);
        set => SetValue(InPointProperty, value);
    }

    /// <summary>
    /// Where playback stops, or null for the end of the media.
    /// </summary>
    /// <remarks>
    /// The instant at the out point is the first one outside the range: a loop
    /// that included it would show a frame from beyond the range on every pass.
    /// A range whose out point is at or before its in point is ignored, and
    /// the whole media plays.
    /// </remarks>
    public TimeSpan? OutPoint
    {
        get => (TimeSpan?)GetValue(OutPointProperty);
        set => SetValue(OutPointProperty, value);
    }

    /// <summary>The range as the handlers see it.</summary>
    internal PlaybackRange Range => new(InPoint, OutPoint);

    /// <summary>
    /// How much the picture is magnified, from 1 (the whole picture, as
    /// <see cref="ScalingMode"/> presents it) up to <see cref="MaximumZoom"/>.
    /// Values outside that are clamped.
    /// </summary>
    /// <remarks>
    /// Magnifying samples the decoded frame, so it shows detail the control was
    /// too small to draw rather than enlarged pixels — up to the resolution of
    /// the footage. A pinch writes back here, which is why the default binding
    /// mode is two-way: a view model binding follows the user's fingers.
    /// </remarks>
    public double ZoomFactor
    {
        get => (double)GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    /// <summary>
    /// How far the magnified picture is moved from the centre, in device-
    /// independent units: positive X to the right, positive Y down.
    /// </summary>
    /// <remarks>
    /// Clamped by the handler to the edges of the picture, so it can never show
    /// black beside a magnified frame, and written back with whatever survived
    /// that clamp. At <see cref="ZoomFactor"/> 1 there is nowhere to go and this
    /// is always <see cref="Point.Zero"/>.
    /// </remarks>
    public Point ZoomOffset
    {
        get => (Point)GetValue(ZoomOffsetProperty);
        set => SetValue(ZoomOffsetProperty, value);
    }

    /// <summary>
    /// The highest <see cref="ZoomFactor"/> the app allows, 1 to 16. Four by
    /// default, which is enough to read a number burnt into a frame.
    /// </summary>
    public double MaximumZoom
    {
        get => (double)GetValue(MaximumZoomProperty);
        set => SetValue(MaximumZoomProperty, value);
    }

    /// <summary>
    /// Whether the user may pinch to magnify and drag to pan the picture.
    /// </summary>
    /// <remarks>
    /// Off by default, like every other interaction: the control draws no
    /// controls and claims no gestures unless asked, so that an app's own
    /// recognisers on the player keep working. While it is on, a drag over the
    /// picture pans instead of scrolling the page, but only once there is
    /// something to pan — unmagnified, touches pass straight through.
    /// </remarks>
    public bool IsPinchToZoomEnabled
    {
        get => (bool)GetValue(IsPinchToZoomEnabledProperty);
        set => SetValue(IsPinchToZoomEnabledProperty, value);
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

    /// <summary>
    /// Whether this media can be played backwards on this platform.
    /// </summary>
    /// <remarks>
    /// False until a source is open, and false on Android throughout: Media3
    /// has no negative playback rate, and running a timer that steps backwards
    /// would be a different thing wearing the same name — on long-GOP footage
    /// each step decodes from the previous keyframe, so it would manage a
    /// handful of frames a second and call it play. An app can bind a reverse
    /// button to this and have it disappear where it would not work.
    /// </remarks>
    public bool CanPlayReverse
    {
        get => (bool)GetValue(CanPlayReverseProperty);
        private set => SetValue(CanPlayReversePropertyKey, value);
    }

    /// <summary>
    /// How many frames the source has, or 0 until that is known exactly.
    /// </summary>
    /// <remarks>
    /// Counted from the container's own sample table rather than from the
    /// duration times the frame rate, which is why it arrives a moment after
    /// <see cref="MediaOpened"/> rather than with it. While it is 0,
    /// <see cref="CurrentFrame"/> and the frame-stepping methods are working
    /// from the reported frame rate and are exact only on constant frame rate
    /// material; once it is set, every frame number comes from the timestamps
    /// the camera wrote.
    /// </remarks>
    public long FrameCount
    {
        get => (long)GetValue(FrameCountProperty);
        private set => SetValue(FrameCountPropertyKey, value);
    }

    /// <summary>
    /// The zero-based number of the frame on screen, as the handler knows it
    /// rather than as the app would have to guess it.
    /// </summary>
    /// <remarks>
    /// Not the same thing as <see cref="Position"/> times the frame rate. The
    /// two platforms disagree about which frame a seek shows, and Media3
    /// reports the position a seek asked for rather than the one it landed on,
    /// so the Android handler tracks the displayed frame itself. Reading it
    /// here is the only way to get the number the viewer can see burnt into the
    /// picture. On variable frame rate material it is still derived from a
    /// single frame rate and is therefore approximate.
    /// </remarks>
    public long CurrentFrame
    {
        get => (long)GetValue(CurrentFrameProperty);
        private set => SetValue(CurrentFramePropertyKey, value);
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

    /// <summary>
    /// The magnification or the pan changed, whether from a pinch or from the
    /// app setting it.
    /// </summary>
    public event EventHandler<VideoZoomChangedEventArgs>? ZoomChanged;

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

    /// <summary>
    /// Plays backwards at <see cref="Speed"/>, stopping at the in point of the
    /// range or at the beginning of the media.
    /// </summary>
    /// <returns>
    /// <c>false</c> when the platform or this particular media cannot run
    /// backwards, or nothing is loaded — see <see cref="CanPlayReverse"/>. The
    /// picture then stays where it is rather than pretending.
    /// </returns>
    public bool PlayReverse()
    {
        VerifyMainThread();
        return Controller?.PlayReverse() ?? false;
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
    /// Seeks so that frame <paramref name="frame"/> is the one on screen,
    /// pausing first.
    /// </summary>
    /// <remarks>
    /// Analysis counts in frames, and converting a frame number to a position
    /// is exactly the calculation the two platforms disagree about. Handing the
    /// number over instead of a time keeps that disagreement inside the
    /// handlers, where it is already solved.
    /// </remarks>
    /// <returns>
    /// <c>false</c> when a newer seek overtook this one or nothing is loaded.
    /// </returns>
    public Task<bool> SeekToFrameAsync(long frame, CancellationToken cancellationToken = default)
    {
        VerifyMainThread();

        return Controller is { } controller
            ? controller.SeekToFrameAsync(frame < 0 ? 0 : frame, cancellationToken)
            : Task.FromResult(false);
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
    /// Magnifies about the middle of the control, so whatever is in the middle
    /// stays there, and carries the current pan with the factor.
    /// </summary>
    /// <remarks>
    /// Needs no player and no loaded source: it is a property change, which is
    /// why it is safe to call before anything is open.
    /// </remarks>
    public void ZoomTo(double factor)
    {
        VerifyMainThread();

        var next = ZoomMath.ScaleAboutCentre(
            new ZoomState(ZoomFactor, ZoomOffset.X, ZoomOffset.Y), factor, MaximumZoom);

        // The factor last, so the pan it belongs to is already in place when the
        // handler measures the room it has.
        ZoomOffset = new Point(next.OffsetX, next.OffsetY);
        ZoomFactor = next.Factor;
    }

    /// <summary>Clears both ends of the range, so the whole media plays again.</summary>
    public void ClearRange()
    {
        VerifyMainThread();

        InPoint = null;
        OutPoint = null;
    }

    /// <summary>Returns to the whole picture, centred.</summary>
    public void ResetZoom()
    {
        VerifyMainThread();

        ZoomFactor = 1d;
        ZoomOffset = Point.Zero;
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

    /// <summary>
    /// The pixel of the frame under a point on the control, or null before
    /// anything has been laid out and opened.
    /// </summary>
    /// <remarks>
    /// Accounts for <see cref="ScalingMode"/>, the letterbox it produces, the
    /// rotation the footage was recorded with, and the current
    /// <see cref="ZoomFactor"/> and <see cref="ZoomOffset"/> — so an annotation
    /// placed with this stays on the same part of the picture when the control
    /// is resized or the picture is magnified.
    /// <para>
    /// A point outside the picture maps to a pixel outside the frame rather
    /// than to null: a caller hit-testing a tap can see that for itself, and
    /// one drawing a line that leaves the picture needs the far end of it.
    /// </para>
    /// </remarks>
    public Point? ToPicturePoint(Point viewPoint)
    {
        VerifyMainThread();
        return Controller?.ToPicturePoint(viewPoint);
    }

    /// <summary>
    /// Where a pixel of the frame is drawn on the control, or null before
    /// anything has been laid out and opened. The inverse of
    /// <see cref="ToPicturePoint"/>.
    /// </summary>
    public Point? ToViewPoint(Point picturePoint)
    {
        VerifyMainThread();
        return Controller?.ToViewPoint(picturePoint);
    }

    /// <summary>
    /// The frame on screen, encoded as a PNG at the resolution of the footage.
    /// </summary>
    /// <remarks>
    /// The frame as recorded, not as drawn: full resolution, and without the
    /// magnification, the scaling mode or anything the app has drawn over the
    /// control. That is what a report or a measurement wants — a screenshot of
    /// the control would be a picture of a phone screen instead of evidence.
    /// <para>
    /// PNG rather than JPEG because the point of the thing is to be looked at
    /// closely, and because compression artefacts in a frame someone is
    /// measuring are indistinguishable from the thing being measured. A 4K
    /// frame runs to several megabytes; decoding one takes long enough to be
    /// worth awaiting rather than blocking a tap on.
    /// </para>
    /// </remarks>
    /// <returns>PNG bytes, or null when nothing is loaded or the frame could not be decoded.</returns>
    public Task<byte[]?> CaptureFrameAsync(CancellationToken cancellationToken = default)
    {
        VerifyMainThread();

        return Controller is { } controller
            ? controller.CaptureFrameAsync(cancellationToken)
            : Task.FromResult<byte[]?>(null);
    }

    /// <summary>Ends a scrub with an exact seek to the position last scrubbed to.</summary>
    public void EndScrub()
    {
        VerifyMainThread();
        Controller?.EndScrub();
    }

    void IVideoPlayerViewController.OnPositionChanged(
        TimeSpan position, int chapterIndex, TimeSpan chapterPosition, long frame)
    {
        Position = position;
        CurrentChapterIndex = chapterIndex;
        CurrentFrame = frame;

        PositionChanged?.Invoke(
            this, new VideoPositionChangedEventArgs(position, chapterIndex, chapterPosition, frame));
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

    void IVideoPlayerViewController.OnZoomChanged(double factor, Point offset)
    {
        // Straight onto the properties: the coercion clamps the factor and the
        // ZoomChanged event follows from the property change, so a pinch and an
        // app setting a factor are reported through exactly one path.
        ZoomOffset = offset;
        ZoomFactor = factor;
    }

    void IVideoPlayerViewController.OnReverseAvailabilityChanged(bool canPlayReverse)
    {
        CanPlayReverse = canPlayReverse;
    }

    void IVideoPlayerViewController.OnFrameCountChanged(long frameCount)
    {
        FrameCount = frameCount;
    }

    private static void OnZoomPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (!Equals(oldValue, newValue) && bindable is VideoPlayerView view)
        {
            view.ZoomChanged?.Invoke(
                view, new VideoZoomChangedEventArgs(view.ZoomFactor, view.ZoomOffset));
        }
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
