namespace Indiko.Maui.Controls.VideoPlayer.Timeline;

/// <summary>
/// Keeps the values an app can set inside what the platforms actually support.
/// </summary>
/// <remarks>
/// Bindable properties are written from bindings, and a binding can deliver
/// anything a converter or a slider produces. The platforms differ in how they
/// react: a rate of zero pauses AVPlayer without changing its state, and a
/// negative speed throws on Media3. Clamping once here keeps both handlers free
/// of that argument checking.
/// </remarks>
public static class ValueClamp
{
    /// <summary>Slowest playback rate. Below this, audio is unusable and decoding stalls.</summary>
    public const double MinimumSpeed = 0.1;

    /// <summary>Fastest playback rate the control offers.</summary>
    public const double MaximumSpeed = 2.0;

    /// <summary>Clamps a playback rate; a value that is not a number falls back to normal speed.</summary>
    public static double Speed(double speed)
    {
        if (!double.IsFinite(speed))
            return 1d;

        return Math.Clamp(speed, MinimumSpeed, MaximumSpeed);
    }

    /// <summary>Clamps a volume to 0–1; a value that is not a number falls back to full.</summary>
    public static double Volume(double volume)
    {
        if (!double.IsFinite(volume))
            return 1d;

        return Math.Clamp(volume, 0d, 1d);
    }

    /// <summary>Clamps a position-event rate to 10–30 Hz.</summary>
    public static int PositionUpdateRate(int rateHz) =>
        Math.Clamp(rateHz, PositionThrottle.MinimumRateHz, PositionThrottle.MaximumRateHz);

    /// <summary>
    /// Clamps a position into the media. While the duration is still unknown
    /// (nothing opened yet) the only position that exists is zero.
    /// </summary>
    public static TimeSpan Position(TimeSpan position, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return TimeSpan.Zero;

        if (position < TimeSpan.Zero)
            return TimeSpan.Zero;

        return position > duration ? duration : position;
    }
}
