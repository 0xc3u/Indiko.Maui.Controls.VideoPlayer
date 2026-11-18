using Indiko.Maui.Controls.VideoPlayer.Models;

namespace Indiko.Maui.Controls.VideoPlayer;

/// <summary>
/// What a platform handler must offer the control beyond property mapping.
/// </summary>
/// <remarks>
/// A <c>CommandMapper</c> covers fire-and-forget calls, but seeking and frame
/// stepping have to be awaited: a caller that scrubs a timeline needs to know
/// whether its target was reached or overtaken. This interface is the handler
/// side of that, kept internal because it is an implementation detail between
/// <see cref="VideoPlayerView"/> and its handlers.
/// </remarks>
internal interface IVideoPlayerController
{
    void Play();

    bool PlayReverse();

    void Pause();

    void Stop();

    Task<bool> SeekExactAsync(TimeSpan position, CancellationToken cancellationToken);

    Task<bool> SeekToFrameAsync(long frame, CancellationToken cancellationToken);

    Task<bool> StepFrameAsync(int frames);

    Point? ToPicturePoint(Point viewPoint);

    Point? ToViewPoint(Point picturePoint);

    Task<byte[]?> CaptureFrameAsync(CancellationToken cancellationToken);

    void BeginScrub();

    void ScrubTo(TimeSpan position);

    void EndScrub();
}

/// <summary>
/// The control side, so a handler can report what the player did without
/// knowing how the view stores it.
/// </summary>
internal interface IVideoPlayerViewController
{
    void OnPositionChanged(TimeSpan position, int chapterIndex, TimeSpan chapterPosition, long frame);

    void OnMediaOpened(TimeSpan duration, double frameRate, Size naturalSize, IReadOnlyList<TimeSpan> chapterDurations);

    void OnMediaFailed(VideoErrorCode errorCode, int chapterIndex, string message);

    void OnPlaybackEnded();

    void OnStateChanged(VideoPlayerState state);

    void OnZoomChanged(double factor, Point offset);

    void OnReverseAvailabilityChanged(bool canPlayReverse);

    void OnFrameCountChanged(long frameCount);

    void OnBookmarkRefreshed(int chapterIndex, string newBookmark);

    void OnDownloadProgress(int chapterIndex, double progress);
}
