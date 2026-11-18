namespace Indiko.Maui.Controls.VideoPlayer.Timeline;

/// <summary>
/// Everything the handlers need to know about frames: which one is on screen,
/// when it begins, and where to seek to put one there.
/// </summary>
/// <remarks>
/// One seam with two sources of truth behind it. Before the container's sample
/// table has been read the answers come from the frame rate, which is right on
/// constant frame rate material and approximate on anything else; afterwards
/// they come from the timestamps themselves. Handlers ask the clock rather
/// than choosing, so there is no branch to forget at one of the half-dozen
/// places frames are counted.
/// </remarks>
public sealed class FrameClock
{
    private readonly double _frameRate;
    private readonly TimeSpan _duration;
    private readonly FrameIndex? _index;

    private FrameClock(double frameRate, TimeSpan duration, FrameIndex? index)
    {
        _frameRate = frameRate;
        _duration = duration;
        _index = index;
    }

    /// <summary>A clock that counts by the frame rate the container reported.</summary>
    public static FrameClock FromRate(double frameRate, TimeSpan duration) =>
        new(frameRate, duration, null);

    /// <summary>The same clock, now backed by the frames that are really there.</summary>
    public FrameClock WithIndex(FrameIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        return new FrameClock(_frameRate, _duration, index);
    }

    /// <summary>Whether frame numbers come from the container rather than from a rate.</summary>
    public bool IsExact => _index is not null;

    /// <summary>How many frames the source has, or 0 while that is not known.</summary>
    public long Count => _index?.Count ?? 0;

    /// <summary>
    /// Brings a frame number inside the media.
    /// </summary>
    /// <remarks>
    /// A seek past the end lands on the last frame, so the number that comes
    /// back has to be that frame's. Reporting the number that was asked for
    /// would put a frame on an app's readout that nobody can see and no step
    /// can reach. Without an index the end is where the frame rate says it is.
    /// </remarks>
    public long ClampFrame(long index)
    {
        if (index <= 0)
            return 0;

        var last = Count > 0
            ? Count - 1
            : FrameMath.FrameIndexAt(_duration, _frameRate) - 1;

        return last >= 0 ? Math.Min(index, last) : index;
    }

    /// <summary>The zero-based number of the frame on screen at this position.</summary>
    public long FrameAt(TimeSpan position) =>
        _index is { } index ? index.IndexAt(position) : FrameMath.FrameIndexAt(position, _frameRate);

    /// <summary>When a frame begins.</summary>
    public TimeSpan FrameStart(long index) =>
        _index is { } exact ? exact.StartTime(index) : FrameMath.FrameStartTime(index, _frameRate);

    /// <summary>Where to seek so that a frame is the one on screen.</summary>
    public TimeSpan SeekTarget(long index, FrameSeekSemantics semantics) =>
        _index is { } exact
            ? exact.SeekTarget(index, semantics, _duration)
            : FrameMath.SeekTargetForFrame(index, _frameRate, _duration, semantics);

    /// <summary>
    /// Where to seek to step <paramref name="frames"/> frames from a position.
    /// </summary>
    public TimeSpan StepTarget(TimeSpan position, int frames, FrameSeekSemantics semantics) =>
        SeekTarget(FrameAt(position) + frames, semantics);
}
