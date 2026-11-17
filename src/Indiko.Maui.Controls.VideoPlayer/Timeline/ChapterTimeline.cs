namespace Indiko.Maui.Controls.VideoPlayer.Timeline;

/// <summary>
/// Maps between a position on the single timeline the player exposes and the
/// (chapter, position-within-chapter) pair the platform needs.
/// </summary>
/// <remarks>
/// A camera that splits a recording into chapters (GoPro writes GH010001.MP4,
/// GH020001.MP4, …) hands the app several files for one continuous take. The
/// control presents them as one timeline, so every position the app sees is a
/// global one and this type is the only place that knows where the seams are.
/// <para>
/// iOS reaches the same result natively through an <c>AVMutableComposition</c>,
/// so there the conversion is only needed for reporting the current chapter.
/// On Android the playlist fallback relies on it for every seek.
/// </para>
/// </remarks>
public sealed class ChapterTimeline
{
    private readonly TimeSpan[] _durations;
    private readonly TimeSpan[] _starts;

    /// <param name="chapterDurations">
    /// Chapter durations in playback order. A chapter whose duration could not
    /// be read is allowed to be zero; it then occupies an index but can never
    /// be resolved to.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The list is empty, or a duration is negative.
    /// </exception>
    public ChapterTimeline(IEnumerable<TimeSpan> chapterDurations)
    {
        ArgumentNullException.ThrowIfNull(chapterDurations);

        _durations = [.. chapterDurations];

        if (_durations.Length == 0)
            throw new ArgumentException("A timeline needs at least one chapter.", nameof(chapterDurations));

        if (_durations.Any(d => d < TimeSpan.Zero))
            throw new ArgumentException("A chapter duration cannot be negative.", nameof(chapterDurations));

        _starts = new TimeSpan[_durations.Length];
        var cursor = TimeSpan.Zero;
        for (var i = 0; i < _durations.Length; i++)
        {
            _starts[i] = cursor;
            cursor += _durations[i];
        }

        Total = cursor;
    }

    /// <summary>Total length of the timeline: the sum of all chapters.</summary>
    public TimeSpan Total { get; }

    /// <summary>Number of chapters, including any of zero length.</summary>
    public int Count => _durations.Length;

    /// <summary>Chapter durations in playback order.</summary>
    public IReadOnlyList<TimeSpan> ChapterDurations => _durations;

    /// <summary>Where a chapter begins on the global timeline.</summary>
    public TimeSpan ChapterStart(int chapterIndex)
    {
        ThrowIfOutOfRange(chapterIndex);
        return _starts[chapterIndex];
    }

    /// <summary>
    /// Converts a position inside one chapter to a position on the global
    /// timeline. The position is clamped into that chapter.
    /// </summary>
    public TimeSpan ToGlobal(int chapterIndex, TimeSpan positionInChapter)
    {
        ThrowIfOutOfRange(chapterIndex);

        var local = positionInChapter;
        if (local < TimeSpan.Zero)
            local = TimeSpan.Zero;
        else if (local > _durations[chapterIndex])
            local = _durations[chapterIndex];

        return _starts[chapterIndex] + local;
    }

    /// <summary>
    /// Resolves a global position to the chapter playing at that moment and the
    /// position within it.
    /// </summary>
    /// <remarks>
    /// A position that falls exactly on a seam belongs to the chapter that is
    /// starting, not to the one that just ended: at that instant the player has
    /// already moved on, and reporting the old chapter would show the app a
    /// frame the viewer is no longer looking at. Positions outside the timeline
    /// clamp to its first or last frame, so a caller never has to range-check
    /// before asking.
    /// </remarks>
    public (int Index, TimeSpan Position) ToLocal(TimeSpan globalPosition)
    {
        if (globalPosition <= TimeSpan.Zero)
            return (FirstPlayable(), TimeSpan.Zero);

        if (globalPosition >= Total)
        {
            var last = LastPlayable();
            return (last, _durations[last]);
        }

        for (var i = 0; i < _durations.Length; i++)
        {
            if (_durations[i] == TimeSpan.Zero)
                continue;

            var end = _starts[i] + _durations[i];
            if (globalPosition < end)
                return (i, globalPosition - _starts[i]);
        }

        var fallback = LastPlayable();
        return (fallback, _durations[fallback]);
    }

    // A timeline of nothing but zero-length chapters has no playable chapter at
    // all; index 0 is then the only answer that is not a lie about the content.
    private int FirstPlayable()
    {
        for (var i = 0; i < _durations.Length; i++)
        {
            if (_durations[i] > TimeSpan.Zero)
                return i;
        }
        return 0;
    }

    private int LastPlayable()
    {
        for (var i = _durations.Length - 1; i >= 0; i--)
        {
            if (_durations[i] > TimeSpan.Zero)
                return i;
        }
        return 0;
    }

    private void ThrowIfOutOfRange(int chapterIndex)
    {
        if (chapterIndex < 0 || chapterIndex >= _durations.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chapterIndex), chapterIndex, "The chapter index is outside this timeline.");
        }
    }
}
