namespace Indiko.Maui.Controls.VideoPlayer.Models;

/// <summary>The player moved. Raised at the configured rate while playing, and once after every completed seek or frame step.</summary>
public sealed class VideoPositionChangedEventArgs(TimeSpan position, int chapterIndex, TimeSpan chapterPosition)
    : EventArgs
{
    /// <param name="frame">The frame on screen, as the handler knows it.</param>
    public VideoPositionChangedEventArgs(
        TimeSpan position, int chapterIndex, TimeSpan chapterPosition, long frame)
        : this(position, chapterIndex, chapterPosition) => Frame = frame;

    /// <summary>Position on the whole timeline.</summary>
    public TimeSpan Position { get; } = position;

    /// <summary>Which clip is playing.</summary>
    public int ChapterIndex { get; } = chapterIndex;

    /// <summary>Position inside that clip.</summary>
    public TimeSpan ChapterPosition { get; } = chapterPosition;

    /// <summary>
    /// The zero-based number of the frame on screen. Not <see cref="Position"/>
    /// times the frame rate: see <c>VideoPlayerView.CurrentFrame</c>.
    /// </summary>
    public long Frame { get; }
}

/// <summary>The source is loaded and its properties are known.</summary>
public sealed class VideoOpenedEventArgs(
    TimeSpan duration,
    double frameRate,
    Size naturalSize,
    IReadOnlyList<TimeSpan> chapterDurations) : EventArgs
{
    /// <summary>Length of the whole timeline: the sum of the chapters.</summary>
    public TimeSpan Duration { get; } = duration;

    /// <summary>Frames per second, or 0 when the container did not say.</summary>
    public double FrameRate { get; } = frameRate;

    /// <summary>Picture size in pixels, after any rotation metadata.</summary>
    public Size NaturalSize { get; } = naturalSize;

    /// <summary>Length of each chapter, in playback order.</summary>
    public IReadOnlyList<TimeSpan> ChapterDurations { get; } = chapterDurations;
}

/// <summary>Opening or playing failed.</summary>
public sealed class VideoFailedEventArgs(VideoErrorCode errorCode, int chapterIndex, string message)
    : EventArgs
{
    /// <summary>What went wrong.</summary>
    public VideoErrorCode ErrorCode { get; } = errorCode;

    /// <summary>Which clip failed, or -1 when it is not about one clip.</summary>
    public int ChapterIndex { get; } = chapterIndex;

    /// <summary>
    /// A short explanation, safe to log: it never contains a path, a content
    /// URI, an asset identifier or a file name.
    /// </summary>
    public string Message { get; } = message;
}

/// <summary>The player state changed.</summary>
public sealed class VideoStateChangedEventArgs(VideoPlayerState oldState, VideoPlayerState newState)
    : EventArgs
{
    public VideoPlayerState OldState { get; } = oldState;

    public VideoPlayerState NewState { get; } = newState;
}

/// <summary>The magnification or the pan of the picture changed.</summary>
public sealed class VideoZoomChangedEventArgs(double factor, Point offset) : EventArgs
{
    /// <summary>How much the picture is magnified; 1 is the whole picture.</summary>
    public double Factor { get; } = factor;

    /// <summary>
    /// How far the picture is moved from the centre, in device-independent
    /// units, after clamping to its own edges.
    /// </summary>
    public Point Offset { get; } = offset;
}

/// <summary>
/// iOS: a security-scoped bookmark had gone stale and was renewed. The app
/// should store <see cref="NewBookmark"/> in place of the one it passed in, or
/// the renewal is repeated on every open until it eventually fails.
/// </summary>
public sealed class VideoBookmarkRefreshedEventArgs(int chapterIndex, string newBookmark) : EventArgs
{
    public int ChapterIndex { get; } = chapterIndex;

    /// <summary>The renewed bookmark data, Base64.</summary>
    public string NewBookmark { get; } = newBookmark;
}

/// <summary>iOS: progress of a photo-library download from iCloud.</summary>
public sealed class VideoDownloadProgressEventArgs(int chapterIndex, double progress) : EventArgs
{
    public int ChapterIndex { get; } = chapterIndex;

    /// <summary>0 to 1.</summary>
    public double Progress { get; } = progress;
}
