namespace Indiko.Maui.Controls.VideoPlayer.Timeline;

/// <summary>
/// A magnification and the pan that goes with it.
/// </summary>
/// <param name="Factor">
/// How much the picture is magnified. 1 is the whole picture as
/// <c>ScalingMode</c> presents it.
/// </param>
/// <param name="OffsetX">
/// How far the picture is moved right of centre, in the same units as the
/// bounds it was clamped against.
/// </param>
/// <param name="OffsetY">How far the picture is moved down from centre.</param>
public readonly record struct ZoomState(double Factor, double OffsetX, double OffsetY)
{
    /// <summary>The whole picture, centred: what the control shows by default.</summary>
    public static readonly ZoomState None = new(1d, 0d, 0d);

    /// <summary>
    /// Whether this is the plain fitted picture, needing no transform at all.
    /// A pan on its own counts: a picture at factor 1 that has been moved shows
    /// a black edge, so the handler still has work to do.
    /// </summary>
    public bool IsIdentity =>
        Math.Abs(Factor - 1d) < 0.0001 && OffsetX == 0d && OffsetY == 0d;
}

/// <summary>
/// The picture as it is actually drawn, and the window it is drawn in.
/// </summary>
/// <remarks>
/// <paramref name="ContentWidth"/> and <paramref name="ContentHeight"/> are the
/// drawn picture, not the file: a 16:9 clip fitted into a 4:3 control is as wide
/// as the control and shorter than it, and the black above and below is not
/// content that panning should be able to reach. Both platforms can measure
/// this — <c>AVPlayerLayer.VideoRect</c> on iOS, the content frame on Android —
/// so nothing here has to reason about rotation or pixel aspect ratios.
/// </remarks>
public readonly record struct ZoomBounds(
    double ContentWidth, double ContentHeight, double ViewportWidth, double ViewportHeight)
{
    /// <summary>
    /// Whether the view has been laid out and the picture measured. Before that
    /// there is no way to tell a legitimate pan from one off the edge, so the
    /// handler waits rather than clamping against zero.
    /// </summary>
    public bool IsMeasured =>
        Positive(ContentWidth) && Positive(ContentHeight) &&
        Positive(ViewportWidth) && Positive(ViewportHeight);

    private static bool Positive(double value) => double.IsFinite(value) && value > 0d;
}

/// <summary>
/// Turns pinches, drags and requested factors into a zoom the picture can
/// actually hold: magnified about the point the user chose, and never panned
/// past its own edge into black.
/// </summary>
/// <remarks>
/// Kept here, free of UIKit and Android types, because it is the part that is
/// easy to get wrong and impossible to check by eye once it is inside a gesture
/// callback. Both handlers apply the result the same way: scale the drawn
/// picture about its centre, then translate it, then clip to the control.
/// </remarks>
public static class ZoomMath
{
    /// <summary>
    /// How far the picture may be moved from centre along one axis before its
    /// edge would come into view: half of what sticks out, or nothing at all
    /// while the magnified picture still fits.
    /// </summary>
    public static double OffsetLimit(double contentExtent, double viewportExtent, double factor)
    {
        if (!double.IsFinite(contentExtent) || contentExtent <= 0d ||
            !double.IsFinite(viewportExtent) || viewportExtent <= 0d ||
            !double.IsFinite(factor) || factor <= 0d)
        {
            return 0d;
        }

        return Math.Max(0d, ((contentExtent * factor) - viewportExtent) / 2d);
    }

    /// <summary>
    /// Brings a zoom inside what the app allows and what the picture holds.
    /// Zooming back out therefore recentres by itself: at factor 1 there is no
    /// room left, so any pan collapses to zero.
    /// </summary>
    public static ZoomState Clamp(ZoomState state, ZoomBounds bounds, double maximumZoom)
    {
        var factor = ValueClamp.Zoom(state.Factor, maximumZoom);

        return new ZoomState(
            factor,
            Offset(state.OffsetX, OffsetLimit(bounds.ContentWidth, bounds.ViewportWidth, factor)),
            Offset(state.OffsetY, OffsetLimit(bounds.ContentHeight, bounds.ViewportHeight, factor)));
    }

    /// <summary>
    /// Applies one step of a pinch: multiplies the factor by
    /// <paramref name="scale"/> and moves the picture so that whatever sits
    /// under the point between the fingers stays there.
    /// </summary>
    /// <param name="focusX">
    /// The point between the fingers, measured from the centre of the control
    /// rather than its corner, because that is where the picture is anchored.
    /// </param>
    /// <param name="focusY">The same, downwards.</param>
    public static ZoomState ScaleBy(
        ZoomState state, double scale, double focusX, double focusY, ZoomBounds bounds, double maximumZoom)
    {
        var from = ValueClamp.Zoom(state.Factor, maximumZoom);

        if (!double.IsFinite(scale) || scale <= 0d)
            return Clamp(state with { Factor = from }, bounds, maximumZoom);

        var to = ValueClamp.Zoom(from * scale, maximumZoom);
        var ratio = to / from;

        // A content point sits on screen at offset + point * factor. Solving
        // that for the focus and putting it back at the new factor is what
        // keeps the picture from sliding out from under the fingers.
        var x = double.IsFinite(focusX) ? focusX : 0d;
        var y = double.IsFinite(focusY) ? focusY : 0d;

        return Clamp(
            new ZoomState(to, x - ((x - state.OffsetX) * ratio), y - ((y - state.OffsetY) * ratio)),
            bounds,
            maximumZoom);
    }

    /// <summary>Applies one step of a drag, stopping at the edge of the picture.</summary>
    public static ZoomState PanBy(
        ZoomState state, double deltaX, double deltaY, ZoomBounds bounds, double maximumZoom)
    {
        var dx = double.IsFinite(deltaX) ? deltaX : 0d;
        var dy = double.IsFinite(deltaY) ? deltaY : 0d;

        return Clamp(
            state with { OffsetX = state.OffsetX + dx, OffsetY = state.OffsetY + dy },
            bounds,
            maximumZoom);
    }

    /// <summary>
    /// Changes the factor about the middle of the control, so what is in the
    /// middle stays in the middle.
    /// </summary>
    /// <remarks>
    /// For an app setting a factor, which knows nothing about how large the
    /// control ended up: the pan is scaled with the factor and the handler
    /// clamps it once it has measured something.
    /// </remarks>
    public static ZoomState ScaleAboutCentre(ZoomState state, double factor, double maximumZoom)
    {
        var from = ValueClamp.Zoom(state.Factor, maximumZoom);
        var to = ValueClamp.Zoom(factor, maximumZoom);
        var ratio = to / from;

        return new ZoomState(to, state.OffsetX * ratio, state.OffsetY * ratio);
    }

    private static double Offset(double offset, double limit) =>
        !double.IsFinite(offset) ? 0d : Math.Clamp(offset, -limit, limit);
}
