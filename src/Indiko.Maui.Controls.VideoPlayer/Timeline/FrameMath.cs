namespace Indiko.Maui.Controls.VideoPlayer.Timeline;

/// <summary>
/// Converts between positions and frame numbers, and works out where to seek
/// for a frame-by-frame step.
/// </summary>
public static class FrameMath
{
    /// <summary>
    /// Frame rate assumed when the container does not report one. Variable
    /// frame rate material has no single answer, so stepping through it is
    /// approximate by nature.
    /// </summary>
    public const double FallbackFrameRate = 30d;

    // A frame boundary at a rate like 29.97 is not representable in ticks, so a
    // position computed as "frame 300" can arrive a few ticks below the real
    // boundary and read as frame 299. A thousandth of a frame absorbs that
    // without being large enough to pull a position into its neighbour: the
    // step targets below sit half a frame from either boundary, which is three
    // orders of magnitude outside this tolerance.
    private const double BoundaryTolerance = 0.001;

    /// <summary>The frame rate to calculate with: the reported one, or the fallback.</summary>
    public static double EffectiveFrameRate(double frameRate) =>
        double.IsFinite(frameRate) && frameRate > 0 ? frameRate : FallbackFrameRate;

    /// <summary>How long a single frame lasts at this frame rate.</summary>
    /// <remarks>
    /// Rounded to whole ticks, because a <see cref="TimeSpan"/> cannot hold
    /// anything finer. Frame times are not built by adding this up — see
    /// <see cref="FrameTicks"/>.
    /// </remarks>
    public static TimeSpan FrameDuration(double frameRate) =>
        TimeSpan.FromTicks((long)Math.Round(FrameTicks(frameRate)));

    /// <summary>
    /// How long a frame lasts in ticks, unrounded.
    /// </summary>
    /// <remarks>
    /// A frame at 30 fps is 333333.33 ticks. Rounding that to 333333 and then
    /// multiplying by the frame number loses a third of a tick per frame, which
    /// by frame 1350 is 45 microseconds — enough to put the start of frame 1350
    /// at 44.999955 s, in the second before the one it belongs to. Multiplying
    /// the unrounded value instead and rounding once at the end keeps every
    /// frame boundary where the frame rate says it is.
    /// </remarks>
    private static double FrameTicks(double frameRate) =>
        TimeSpan.TicksPerSecond / EffectiveFrameRate(frameRate);

    /// <summary>
    /// The zero-based number of the frame on screen at this position. Frame
    /// <c>n</c> covers <c>[n / fps, (n + 1) / fps)</c>.
    /// </summary>
    public static long FrameIndexAt(TimeSpan position, double frameRate)
    {
        if (position <= TimeSpan.Zero)
            return 0;

        return (long)Math.Floor(position.Ticks / FrameTicks(frameRate) + BoundaryTolerance);
    }

    /// <summary>
    /// Where to seek so that the frame <paramref name="frames"/> away from the
    /// one at <paramref name="position"/> is actually shown.
    /// </summary>
    /// <remarks>
    /// Never the frame boundary itself: a seek landing exactly on one is a coin
    /// flip once decoder and container rounding have their say, and a stepping
    /// control that lands on boundaries shows some frames twice and skips
    /// others. Which side of the boundary to aim for depends on the platform,
    /// hence <paramref name="semantics"/>.
    /// </remarks>
    /// <param name="duration">
    /// Total length of the media. The result stays a full frame short of it,
    /// because the instant at <paramref name="duration"/> is past the last
    /// frame and seeking there ends playback instead of showing a picture.
    /// </param>
    public static TimeSpan StepTarget(
        TimeSpan position,
        double frameRate,
        int frames,
        TimeSpan duration,
        FrameSeekSemantics semantics = FrameSeekSemantics.Containing) =>
        SeekTargetForFrame(FrameIndexAt(position, frameRate) + frames, frameRate, duration, semantics);

    /// <summary>When frame <paramref name="index"/> begins.</summary>
    public static TimeSpan FrameStartTime(long index, double frameRate)
    {
        if (index <= 0)
            return TimeSpan.Zero;

        return TimeSpan.FromTicks((long)Math.Round(index * FrameTicks(frameRate)));
    }

    /// <summary>
    /// Where to seek so that frame <paramref name="index"/> is the one on
    /// screen, for a player following <paramref name="semantics"/>.
    /// </summary>
    /// <remarks>
    /// Never the frame boundary itself: a seek landing exactly on one is a coin
    /// flip once decoder and container rounding have had their say, and a
    /// stepping control that lands on boundaries shows some frames twice and
    /// skips others. Which side of the boundary to aim for is the thing the two
    /// platforms disagree about.
    /// </remarks>
    /// <param name="duration">
    /// Total length of the media. The result stays a full frame short of it,
    /// because the instant at <paramref name="duration"/> is past the last
    /// frame and seeking there ends playback instead of showing a picture.
    /// </param>
    public static TimeSpan SeekTargetForFrame(
        long index,
        double frameRate,
        TimeSpan duration,
        FrameSeekSemantics semantics = FrameSeekSemantics.Containing)
    {
        var frame = FrameDuration(frameRate);

        // Stepping back past the beginning means the beginning, not part of a
        // frame into it: position zero already shows frame zero.
        if (index < 0)
            return TimeSpan.Zero;

        // Containing (AVFoundation): the frame shown is the one the position
        // falls inside, so aim at the middle of the wanted frame.
        // AtOrAfter (Media3 ExoPlayer): the frame shown is the first one whose
        // timestamp is at or after the position, so aim just short of the
        // wanted frame's start, which is the middle of the one before it.
        // Aiming at the middle of the wanted frame there lands a frame too far,
        // every time.
        var offset = semantics == FrameSeekSemantics.Containing ? 0.5 : -0.5;
        var target = TimeSpan.FromTicks((long)Math.Round((index + offset) * FrameTicks(frameRate)));

        var last = duration > frame ? duration - frame : TimeSpan.Zero;
        if (target > last)
            target = last;

        return target < TimeSpan.Zero ? TimeSpan.Zero : target;
    }
}
