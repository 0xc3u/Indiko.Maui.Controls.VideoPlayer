namespace Indiko.Maui.Controls.VideoPlayer.Timeline;

/// <summary>
/// Thins the player's position observations down to the rate the app asked for.
/// </summary>
/// <remarks>
/// Both platforms are observed at roughly 30 Hz — iOS through a periodic time
/// observer, Android through a main-looper handler — because that is the finest
/// granularity worth having. An app that only moves a playhead on a map does
/// not want 30 layout passes a second, so it asks for fewer; one that drives a
/// frame counter wants all of them.
/// </remarks>
public sealed class PositionThrottle
{
    /// <summary>Slowest rate an app may ask for.</summary>
    public const int MinimumRateHz = 10;

    /// <summary>Fastest rate an app may ask for.</summary>
    public const int MaximumRateHz = 30;

    private readonly double _intervalTicks;
    private readonly double _toleranceTicks;

    private TimeSpan? _lastEmit;
    private bool _forced;

    public PositionThrottle(int rateHz)
    {
        RateHz = ValueClamp.PositionUpdateRate(rateHz);
        _intervalTicks = (double)TimeSpan.TicksPerSecond / RateHz;

        // The observation clock is quantised (the platforms report whole
        // milliseconds), so an interval that is not a whole number of
        // milliseconds would otherwise lose one event per second to a gap that
        // is short by a fraction of a tick.
        _toleranceTicks = _intervalTicks * 0.01;
    }

    /// <summary>The rate actually in use, after clamping.</summary>
    public int RateHz { get; }

    /// <summary>
    /// Whether this observation should be reported. <paramref name="position"/>
    /// is the player clock, which is also what the caller is about to report.
    /// </summary>
    public bool ShouldEmit(TimeSpan position)
    {
        if (_forced || _lastEmit is null)
        {
            _forced = false;
            _lastEmit = position;
            return true;
        }

        // Loading a new source or looping restarts the clock behind the last
        // reported position. Without this the throttle would go silent until
        // playback passed the old position again.
        if (position < _lastEmit.Value)
        {
            _lastEmit = position;
            return true;
        }

        if ((position - _lastEmit.Value).Ticks + _toleranceTicks >= _intervalTicks)
        {
            _lastEmit = position;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Makes the next observation report regardless of the interval, for a
    /// position the app did not ask for: a finished seek or a frame step. The
    /// alternative is a playhead that lags the picture by up to a full interval.
    /// </summary>
    public void Force() => _forced = true;

    /// <summary>Forgets the last report, so the next observation is the first one again.</summary>
    public void Reset()
    {
        _lastEmit = null;
        _forced = false;
    }
}
