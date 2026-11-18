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

    /// <summary>
    /// Fastest playback rate the control offers.
    /// </summary>
    /// <remarks>
    /// Sixteen because reviewing an hour of footage at two times is half an
    /// hour of someone's attention. What the decoder can actually keep up with
    /// is another matter — both platforms drop frames rather than fall behind,
    /// which is what a fast review wants — and both stop carrying audio well
    /// before this.
    /// </remarks>
    public const double MaximumSpeed = 16.0;

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

    /// <summary>No magnification: the whole picture, as the scaling mode presents it.</summary>
    public const double MinimumZoom = 1.0;

    /// <summary>
    /// The highest ceiling an app may set. Past this a frame is mostly the
    /// decoder's interpolation rather than anything that was filmed.
    /// </summary>
    public const double MaximumZoomCeiling = 16.0;

    /// <summary>The default ceiling: enough to read a number burnt into a frame.</summary>
    public const double DefaultMaximumZoom = 4.0;

    /// <summary>
    /// Clamps the ceiling an app sets to 1–16; a value that is not a number
    /// falls back to the default.
    /// </summary>
    public static double MaximumZoom(double maximum)
    {
        if (!double.IsFinite(maximum))
            return DefaultMaximumZoom;

        return Math.Clamp(maximum, MinimumZoom, MaximumZoomCeiling);
    }

    /// <summary>
    /// Clamps a magnification to between the whole picture and the app's own
    /// ceiling. There is deliberately nothing below 1: zooming out past the
    /// fitted picture would letterbox a letterbox.
    /// </summary>
    public static double Zoom(double factor, double maximum)
    {
        if (!double.IsFinite(factor))
            return MinimumZoom;

        return Math.Clamp(factor, MinimumZoom, MaximumZoom(maximum));
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
