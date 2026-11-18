namespace Indiko.Maui.Controls.VideoPlayer.Timeline;

/// <summary>
/// The part of the timeline playback is confined to: an in point, an out
/// point, or one of each.
/// </summary>
/// <remarks>
/// The working tool of movement analysis is two seconds on repeat. Rather than
/// trimming the media or rebuilding the source, the range is a rule about
/// playback: the picture stops or jumps back at the out point, and seeking is
/// left alone, so an app can still look anywhere while a range is set.
/// <para>
/// Either end may be left out. An in point alone runs to the end of the media;
/// an out point alone runs from the beginning. A range that ends before it
/// starts is not a range at all — see <see cref="IsUsable"/>.
/// </para>
/// </remarks>
/// <param name="In">Where the range begins, or null for the start of the media.</param>
/// <param name="Out">Where it ends, or null for the end of the media.</param>
public readonly record struct PlaybackRange(TimeSpan? In, TimeSpan? Out)
{
    /// <summary>No range: playback runs the whole timeline.</summary>
    public static readonly PlaybackRange None = default;

    /// <summary>Whether the app has set either end.</summary>
    public bool IsSet => In.HasValue || Out.HasValue;

    /// <summary>Where the range begins, brought inside the media.</summary>
    public TimeSpan Start(TimeSpan duration) => Inside(In ?? TimeSpan.Zero, duration);

    /// <summary>Where the range ends, brought inside the media.</summary>
    public TimeSpan End(TimeSpan duration) => Inside(Out ?? duration, duration);

    /// <summary>
    /// Whether this range can be played: set, inside a media of known length,
    /// and ending after it starts.
    /// </summary>
    /// <remarks>
    /// An inverted range is refused rather than quietly swapped or nudged into
    /// shape. An app that computed its two points the wrong way round then gets
    /// the whole media, which is visible immediately, instead of a range nobody
    /// asked for.
    /// </remarks>
    public bool IsUsable(TimeSpan duration) =>
        IsSet && duration > TimeSpan.Zero && End(duration) > Start(duration);

    /// <summary>
    /// Whether a position is inside the range. The start counts; the end does
    /// not, because the out point is where playback stops and the instant at it
    /// is the first one outside.
    /// </summary>
    public bool Contains(TimeSpan position, TimeSpan duration) =>
        IsUsable(duration) && position >= Start(duration) && position < End(duration);

    /// <summary>
    /// Where playback should carry on from: where it is when that is inside the
    /// range, and the start of the range when it is not.
    /// </summary>
    public TimeSpan Resume(TimeSpan position, TimeSpan duration) =>
        !IsUsable(duration) || Contains(position, duration) ? position : Start(duration);

    /// <summary>
    /// How long until playback at <paramref name="speed"/> reaches the end of
    /// the range, or zero when it is already there or there is no range.
    /// </summary>
    /// <remarks>
    /// Android has no way to tell a player to stop at a time, so the handler
    /// schedules the moment itself and this is the arithmetic behind it.
    /// </remarks>
    public TimeSpan TimeUntilEnd(TimeSpan position, TimeSpan duration, double speed)
    {
        if (!IsUsable(duration))
            return TimeSpan.Zero;

        var remaining = End(duration) - position;

        if (remaining <= TimeSpan.Zero)
            return TimeSpan.Zero;

        var rate = double.IsFinite(speed) && speed > 0 ? speed : 1d;
        return TimeSpan.FromTicks((long)Math.Round(remaining.Ticks / rate));
    }

    private static TimeSpan Inside(TimeSpan value, TimeSpan duration)
    {
        if (value < TimeSpan.Zero)
            return TimeSpan.Zero;

        return value > duration ? duration : value;
    }
}
