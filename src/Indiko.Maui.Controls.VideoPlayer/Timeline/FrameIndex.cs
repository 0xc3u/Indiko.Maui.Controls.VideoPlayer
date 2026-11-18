namespace Indiko.Maui.Controls.VideoPlayer.Timeline;

/// <summary>
/// When every frame of a source begins, read from the container rather than
/// calculated from a frame rate.
/// </summary>
/// <remarks>
/// A frame rate is an average, and on variable frame rate material it is an
/// average of things that never happened: an iPhone that dropped into slow
/// motion, an action camera that dropped frames in the heat, a surveillance
/// recorder that slows down when nothing moves. Multiplying a position by such
/// a rate gives a frame number that is wrong without ever looking wrong, which
/// is the worst way for a control that promises frame accuracy to be wrong.
/// <para>
/// The timestamps come from the container's sample table, which costs one pass
/// over a few hundred kilobytes of index and no decoding at all. Until that
/// pass finishes the control falls back to the frame rate, so the index is an
/// improvement in accuracy rather than a precondition for playing.
/// </para>
/// </remarks>
public sealed class FrameIndex
{
    private readonly long[] _ticks;

    /// <param name="frameStartTimes">
    /// When each frame begins. Order does not matter — a container hands out
    /// samples in decode order, which with B-frames is not the order they are
    /// shown in — and repeated timestamps count once.
    /// </param>
    /// <param name="offset">
    /// Where this clip begins on the timeline the app sees, for a source built
    /// from several files.
    /// </param>
    /// <exception cref="ArgumentException">There is not a single frame.</exception>
    public FrameIndex(IEnumerable<TimeSpan> frameStartTimes, TimeSpan offset = default)
    {
        ArgumentNullException.ThrowIfNull(frameStartTimes);

        _ticks = [.. frameStartTimes
            .Select(t => t.Ticks + offset.Ticks)
            .Distinct()
            .Order()];

        if (_ticks.Length == 0)
            throw new ArgumentException("A frame index needs at least one frame.", nameof(frameStartTimes));
    }

    private FrameIndex(long[] ticks) => _ticks = ticks;

    /// <summary>How many frames the source has.</summary>
    public int Count => _ticks.Length;

    /// <summary>Where the last frame begins.</summary>
    public TimeSpan LastFrameStart => TimeSpan.FromTicks(_ticks[^1]);

    /// <summary>
    /// Joins the indexes of several clips, which are expected to carry their
    /// own offsets, into one index for the whole timeline.
    /// </summary>
    public static FrameIndex Join(IEnumerable<FrameIndex> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var ticks = parts.SelectMany(p => p._ticks).Distinct().Order().ToArray();

        if (ticks.Length == 0)
            throw new ArgumentException("A frame index needs at least one frame.", nameof(parts));

        return new FrameIndex(ticks);
    }

    /// <summary>When frame <paramref name="index"/> begins, clamped into the index.</summary>
    public TimeSpan StartTime(long index) =>
        TimeSpan.FromTicks(_ticks[(int)Math.Clamp(index, 0, _ticks.Length - 1)]);

    /// <summary>
    /// The frame on screen at a position: the last one that has begun.
    /// </summary>
    public long IndexAt(TimeSpan position)
    {
        var found = Array.BinarySearch(_ticks, position.Ticks);

        if (found >= 0)
            return found;

        // BinarySearch returns the complement of the next larger entry, so the
        // frame that is showing is the one before it.
        var next = ~found;
        return next == 0 ? 0 : next - 1;
    }

    /// <summary>
    /// Where to seek so that frame <paramref name="index"/> is the one on
    /// screen, for a player following <paramref name="semantics"/>.
    /// </summary>
    /// <remarks>
    /// Aimed at the middle of a frame rather than at its edge, as with a fixed
    /// frame rate, but the middle is now measured between the real neighbours
    /// of that frame instead of assumed from an average.
    /// </remarks>
    public TimeSpan SeekTarget(long index, FrameSeekSemantics semantics, TimeSpan duration)
    {
        var i = (int)Math.Clamp(index, 0, _ticks.Length - 1);

        if (semantics == FrameSeekSemantics.AtOrAfter)
        {
            // Short of the wanted frame, inside the one before it; there is
            // nothing before the first frame but the beginning.
            return i == 0 ? TimeSpan.Zero : Middle(_ticks[i - 1], _ticks[i]);
        }

        var end = i + 1 < _ticks.Length
            ? _ticks[i + 1]
            : Math.Max(_ticks[i] + 1, duration.Ticks);

        return Middle(_ticks[i], end);
    }

    private static TimeSpan Middle(long from, long to) =>
        TimeSpan.FromTicks(from + ((to - from) / 2));
}
