using Xunit;
using Indiko.Maui.Controls.VideoPlayer.Timeline;

namespace Indiko.Maui.Controls.VideoPlayer.Tests;

public class ZoomMathTests
{
    // A 16:9 picture letterboxed into a 320x240 well: 320x180 of drawn picture
    // with 30 points of black above and below. The numbers are chosen so every
    // expectation below can be checked by hand.
    private static readonly ZoomBounds Letterboxed = new(320, 180, 320, 240);

    // The picture fills the well exactly, as Fill and Stretch present it.
    private static readonly ZoomBounds Filled = new(320, 240, 320, 240);

    [Fact]
    public void Nothing_can_be_panned_while_the_picture_fits()
    {
        Assert.Equal(0, ZoomMath.OffsetLimit(320, 320, 1));
        Assert.Equal(0, ZoomMath.OffsetLimit(180, 240, 1));
    }

    [Theory]
    [InlineData(2, 160)]
    [InlineData(4, 480)]
    [InlineData(1.5, 80)]
    public void Magnifying_opens_up_half_the_overhang_in_each_direction(double factor, double expected)
    {
        Assert.Equal(expected, ZoomMath.OffsetLimit(320, 320, factor), 6);
    }

    [Fact]
    public void An_axis_that_still_fits_when_magnified_stays_centred()
    {
        // 180 of picture in 240 of well: at 1.25x the picture is 225 high and
        // the letterbox is merely thinner. There is nothing to pan to.
        Assert.Equal(0, ZoomMath.OffsetLimit(180, 240, 1.25), 6);
        Assert.Equal(15, ZoomMath.OffsetLimit(180, 240, 1.5), 6);
    }

    [Theory]
    [InlineData(0, 320, 2)]
    [InlineData(320, 0, 2)]
    [InlineData(-320, 320, 2)]
    [InlineData(double.NaN, 320, 2)]
    [InlineData(320, double.NaN, 2)]
    [InlineData(320, 320, double.NaN)]
    public void An_unmeasured_view_offers_no_room_to_pan(double content, double viewport, double factor)
    {
        Assert.Equal(0, ZoomMath.OffsetLimit(content, viewport, factor));
    }

    [Fact]
    public void Clamping_pulls_a_pan_back_to_the_edge_of_the_picture()
    {
        var clamped = ZoomMath.Clamp(new ZoomState(2, 999, -999), Letterboxed, 4);

        Assert.Equal(2, clamped.Factor, 6);
        Assert.Equal(160, clamped.OffsetX, 6);   // (320*2 - 320) / 2
        Assert.Equal(-60, clamped.OffsetY, 6);   // (180*2 - 240) / 2
    }

    [Fact]
    public void Clamping_recentres_a_pan_that_no_longer_has_room()
    {
        var clamped = ZoomMath.Clamp(new ZoomState(1, 40, 40), Letterboxed, 4);

        Assert.Equal(1, clamped.Factor, 6);
        Assert.Equal(0, clamped.OffsetX, 6);
        Assert.Equal(0, clamped.OffsetY, 6);
    }

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(9, 4)]
    [InlineData(double.NaN, 1)]
    public void Clamping_keeps_the_factor_between_fit_and_the_apps_ceiling(double requested, double expected)
    {
        Assert.Equal(expected, ZoomMath.Clamp(new ZoomState(requested, 0, 0), Filled, 4).Factor, 6);
    }

    [Fact]
    public void A_pinch_keeps_the_point_between_the_fingers_still()
    {
        // Pinching to 2x with the fingers 80 points right of centre: the content
        // under them must not move, which takes the picture 80 points left.
        var zoomed = ZoomMath.ScaleBy(ZoomState.None, 2, 80, 0, Filled, 4);

        Assert.Equal(2, zoomed.Factor, 6);
        Assert.Equal(-80, zoomed.OffsetX, 6);
        Assert.Equal(0, zoomed.OffsetY, 6);
    }

    [Fact]
    public void A_pinch_in_the_middle_magnifies_without_moving_anything()
    {
        var zoomed = ZoomMath.ScaleBy(ZoomState.None, 2, 0, 0, Filled, 4);

        Assert.Equal(2, zoomed.Factor, 6);
        Assert.Equal(0, zoomed.OffsetX, 6);
        Assert.Equal(0, zoomed.OffsetY, 6);
    }

    [Fact]
    public void Pinching_back_out_returns_to_the_whole_picture_centred()
    {
        var zoomed = ZoomMath.ScaleBy(ZoomState.None, 3, 100, 60, Filled, 4);
        var out1 = ZoomMath.ScaleBy(zoomed, 1d / 3d, 100, 60, Filled, 4);

        Assert.Equal(1, out1.Factor, 6);
        Assert.Equal(0, out1.OffsetX, 6);
        Assert.Equal(0, out1.OffsetY, 6);
    }

    [Fact]
    public void A_pinch_that_would_leave_the_picture_is_held_at_the_edge()
    {
        // Fingers in the far corner of a letterboxed picture: the focus cannot
        // be honoured vertically, because there is no picture out there.
        var zoomed = ZoomMath.ScaleBy(ZoomState.None, 2, 160, 120, Letterboxed, 4);

        Assert.Equal(2, zoomed.Factor, 6);
        Assert.Equal(-160, zoomed.OffsetX, 6);
        Assert.Equal(-60, zoomed.OffsetY, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_nonsense_pinch_leaves_the_zoom_as_it_was(double scale)
    {
        var state = ZoomMath.Clamp(new ZoomState(2, 10, 0), Filled, 4);
        var after = ZoomMath.ScaleBy(state, scale, 0, 0, Filled, 4);

        Assert.Equal(state, after);
    }

    [Fact]
    public void A_pinch_with_a_nonsense_focus_falls_back_to_the_centre()
    {
        var zoomed = ZoomMath.ScaleBy(ZoomState.None, 2, double.NaN, double.NaN, Filled, 4);

        Assert.Equal(2, zoomed.Factor, 6);
        Assert.Equal(0, zoomed.OffsetX, 6);
        Assert.Equal(0, zoomed.OffsetY, 6);
    }

    [Fact]
    public void Dragging_moves_the_picture_and_stops_at_its_edge()
    {
        var zoomed = ZoomMath.Clamp(new ZoomState(2, 0, 0), Filled, 4);

        var dragged = ZoomMath.PanBy(zoomed, 100, -50, Filled, 4);
        Assert.Equal(100, dragged.OffsetX, 6);
        Assert.Equal(-50, dragged.OffsetY, 6);

        var further = ZoomMath.PanBy(dragged, 500, 0, Filled, 4);
        Assert.Equal(160, further.OffsetX, 6);   // (320*2 - 320) / 2
        Assert.Equal(-50, further.OffsetY, 6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_nonsense_drag_is_ignored(double delta)
    {
        var zoomed = ZoomMath.Clamp(new ZoomState(2, 40, 0), Filled, 4);
        var dragged = ZoomMath.PanBy(zoomed, delta, delta, Filled, 4);

        Assert.Equal(40, dragged.OffsetX, 6);
        Assert.Equal(0, dragged.OffsetY, 6);
    }

    [Fact]
    public void Zooming_about_the_centre_keeps_the_middle_of_the_picture_in_the_middle()
    {
        // An app setting ZoomFactor knows nothing about the view's size, so this
        // scales the pan with the factor and leaves the clamping to the handler.
        var state = new ZoomState(2, 100, -40);

        var closer = ZoomMath.ScaleAboutCentre(state, 4, 8);
        Assert.Equal(4, closer.Factor, 6);
        Assert.Equal(200, closer.OffsetX, 6);
        Assert.Equal(-80, closer.OffsetY, 6);

        var wider = ZoomMath.ScaleAboutCentre(state, 1, 8);
        Assert.Equal(1, wider.Factor, 6);
        Assert.Equal(50, wider.OffsetX, 6);
        Assert.Equal(-20, wider.OffsetY, 6);
    }

    [Fact]
    public void Zooming_right_back_out_leaves_no_pan_behind()
    {
        // Factor 1 with an offset would show black at one edge, so returning to
        // fit must also return to the centre.
        var state = ZoomMath.Clamp(ZoomMath.ScaleAboutCentre(new ZoomState(4, 300, 200), 1, 8), Filled, 8);

        Assert.Equal(1, state.Factor, 6);
        Assert.Equal(0, state.OffsetX, 6);
        Assert.Equal(0, state.OffsetY, 6);
    }

    [Fact]
    public void None_is_the_unzoomed_picture()
    {
        Assert.Equal(1, ZoomState.None.Factor);
        Assert.Equal(0, ZoomState.None.OffsetX);
        Assert.Equal(0, ZoomState.None.OffsetY);
        Assert.True(ZoomState.None.IsIdentity);
        Assert.False(new ZoomState(1.5, 0, 0).IsIdentity);
        Assert.False(new ZoomState(1, 10, 0).IsIdentity);
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(1, 1)]
    [InlineData(0.5, 1)]
    [InlineData(16, 16)]
    [InlineData(100, 16)]
    [InlineData(double.NaN, 4)]
    public void An_apps_ceiling_stays_between_fit_and_sixteen(double requested, double expected)
    {
        Assert.Equal(expected, ValueClamp.MaximumZoom(requested), 6);
    }

    [Theory]
    [InlineData(2, 4, 2)]
    [InlineData(2, 1.5, 1.5)]
    [InlineData(0, 4, 1)]
    [InlineData(-2, 4, 1)]
    [InlineData(double.NaN, 4, 1)]
    public void A_zoom_factor_is_clamped_to_the_apps_ceiling(double requested, double maximum, double expected)
    {
        Assert.Equal(expected, ValueClamp.Zoom(requested, maximum), 6);
    }

    [Fact]
    public void A_nonsense_ceiling_does_not_let_a_factor_through()
    {
        Assert.Equal(ValueClamp.DefaultMaximumZoom, ValueClamp.Zoom(99, double.NaN), 6);
    }

    [Fact]
    public void Bounds_report_whether_the_view_has_been_measured()
    {
        Assert.True(Letterboxed.IsMeasured);
        Assert.False(default(ZoomBounds).IsMeasured);
        Assert.False(new ZoomBounds(320, 180, 0, 240).IsMeasured);
        Assert.False(new ZoomBounds(double.NaN, 180, 320, 240).IsMeasured);
    }
}
