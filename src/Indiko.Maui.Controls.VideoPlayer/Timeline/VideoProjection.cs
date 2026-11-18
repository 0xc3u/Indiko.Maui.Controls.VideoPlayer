namespace Indiko.Maui.Controls.VideoPlayer.Timeline;

/// <summary>A rectangle in the control's own units.</summary>
public readonly record struct ViewRect(double X, double Y, double Width, double Height)
{
    /// <summary>Whether this is a rectangle with an area.</summary>
    public bool IsMeasured =>
        double.IsFinite(X) && double.IsFinite(Y) &&
        double.IsFinite(Width) && Width > 0 &&
        double.IsFinite(Height) && Height > 0;

    public double CentreX => X + (Width / 2d);

    public double CentreY => Y + (Height / 2d);
}

/// <summary>
/// Converts between a point on the control and a pixel in the frame.
/// </summary>
/// <remarks>
/// Everything an app wants to draw over a video — a line between two players, a
/// circle around a number plate, the angle of a joint — has to survive the
/// control being resized, the scaling mode being changed and the picture being
/// magnified and panned. All of that is one affine chain, and it is the sort of
/// chain every consuming app would otherwise rebuild from the letterbox
/// outwards and get subtly wrong.
/// <para>
/// <paramref name="Content"/> is the rectangle the picture occupies before any
/// magnification: the letterboxed rectangle under <c>Fit</c>, the control
/// itself under <c>Stretch</c>, and a rectangle larger than the control under
/// <c>Fill</c>, where the overhang is cropped away. The zoom is applied about
/// the middle of <paramref name="Viewport"/>, which is how both handlers apply
/// it to the picture.
/// </para>
/// </remarks>
/// <param name="Content">Where the unmagnified picture is drawn.</param>
/// <param name="Viewport">The control.</param>
/// <param name="Zoom">The magnification and pan currently applied.</param>
/// <param name="PixelWidth">Width of the frame in pixels, as displayed.</param>
/// <param name="PixelHeight">Height of the frame in pixels, as displayed.</param>
public readonly record struct VideoProjection(
    ViewRect Content, ViewRect Viewport, ZoomState Zoom, double PixelWidth, double PixelHeight)
{
    /// <summary>
    /// Whether the control has been laid out and a frame size is known. Before
    /// that there is nothing to convert and both directions answer null.
    /// </summary>
    public bool IsMeasured =>
        Content.IsMeasured && Viewport.IsMeasured &&
        double.IsFinite(PixelWidth) && PixelWidth > 0 &&
        double.IsFinite(PixelHeight) && PixelHeight > 0 &&
        double.IsFinite(Zoom.Factor) && Zoom.Factor > 0 &&
        double.IsFinite(Zoom.OffsetX) && double.IsFinite(Zoom.OffsetY);

    /// <summary>
    /// The pixel of the frame under a point on the control, or null when
    /// nothing has been measured yet.
    /// </summary>
    /// <remarks>
    /// A point outside the picture — in the letterbox, or past the edge of a
    /// magnified frame — maps to a pixel outside the frame rather than to null
    /// or to the nearest edge. A caller hit-testing a tap can see that for
    /// itself; one drawing a line that leaves the picture needs it.
    /// </remarks>
    public (double X, double Y)? ToVideo(double viewX, double viewY)
    {
        if (!IsMeasured || !double.IsFinite(viewX) || !double.IsFinite(viewY))
            return null;

        // Undo the magnification about the middle of the control, then read off
        // where that lands inside the drawn rectangle.
        var x = Viewport.CentreX + ((viewX - Viewport.CentreX - Zoom.OffsetX) / Zoom.Factor);
        var y = Viewport.CentreY + ((viewY - Viewport.CentreY - Zoom.OffsetY) / Zoom.Factor);

        return (
            (x - Content.X) / Content.Width * PixelWidth,
            (y - Content.Y) / Content.Height * PixelHeight);
    }

    /// <summary>
    /// Where a pixel of the frame is drawn on the control, or null when nothing
    /// has been measured yet.
    /// </summary>
    /// <remarks>
    /// A pixel that is currently off screen — scrolled out of a magnified
    /// frame, or cropped away by <c>Fill</c> — maps to a point outside the
    /// control rather than to null.
    /// </remarks>
    public (double X, double Y)? ToView(double videoX, double videoY)
    {
        if (!IsMeasured || !double.IsFinite(videoX) || !double.IsFinite(videoY))
            return null;

        var x = Content.X + (videoX / PixelWidth * Content.Width);
        var y = Content.Y + (videoY / PixelHeight * Content.Height);

        return (
            Viewport.CentreX + ((x - Viewport.CentreX) * Zoom.Factor) + Zoom.OffsetX,
            Viewport.CentreY + ((y - Viewport.CentreY) * Zoom.Factor) + Zoom.OffsetY);
    }
}
