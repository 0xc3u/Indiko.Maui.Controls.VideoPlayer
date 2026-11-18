using Xunit;
using Indiko.Maui.Controls.VideoPlayer.Timeline;

namespace Indiko.Maui.Controls.VideoPlayer.Tests;

public class VideoProjectionTests
{
    // A 1280x720 picture fitted into a 320x240 control: as wide as the control,
    // 180 units tall, with 30 units of black above and below. Every expectation
    // below can be checked by hand from those numbers.
    private static VideoProjection Letterboxed(ZoomState zoom) => new(
        new ViewRect(0, 30, 320, 180),
        new ViewRect(0, 0, 320, 240),
        zoom,
        1280,
        720);

    [Fact]
    public void The_corners_of_the_picture_are_the_corners_of_the_frame()
    {
        var projection = Letterboxed(ZoomState.None);

        Assert.Equal((0d, 0d), projection.ToVideo(0, 30));
        Assert.Equal((1280d, 720d), projection.ToVideo(320, 210));
    }

    [Fact]
    public void The_middle_of_the_control_is_the_middle_of_the_frame()
    {
        Assert.Equal((640d, 360d), Letterboxed(ZoomState.None).ToVideo(160, 120));
    }

    [Fact]
    public void A_point_in_the_letterbox_is_outside_the_frame_rather_than_nothing()
    {
        // Reported honestly rather than clamped or refused: an app drawing a
        // line that leaves the picture needs the far end of it, and one
        // hit-testing a tap can see for itself that 0 > y.
        var point = Letterboxed(ZoomState.None).ToVideo(160, 0);

        Assert.NotNull(point);
        Assert.Equal(640d, point!.Value.X, 6);
        Assert.Equal(-120d, point.Value.Y, 6);
    }

    [Fact]
    public void A_frame_pixel_maps_back_to_where_it_is_drawn()
    {
        var projection = Letterboxed(ZoomState.None);

        Assert.Equal((0d, 30d), projection.ToView(0, 0));
        Assert.Equal((160d, 120d), projection.ToView(640, 360));
        Assert.Equal((320d, 210d), projection.ToView(1280, 720));
    }

    [Theory]
    [InlineData(17, 44)]
    [InlineData(300, 200)]
    [InlineData(160, 120)]
    [InlineData(0, 0)]
    public void Mapping_there_and_back_returns_the_same_point(double x, double y)
    {
        var projection = Letterboxed(new ZoomState(2.5, 31, -17));

        var video = projection.ToVideo(x, y);
        Assert.NotNull(video);

        var back = projection.ToView(video!.Value.X, video.Value.Y);
        Assert.NotNull(back);
        Assert.Equal(x, back!.Value.X, 6);
        Assert.Equal(y, back.Value.Y, 6);
    }

    [Fact]
    public void Magnifying_leaves_the_middle_where_it_was()
    {
        Assert.Equal((640d, 360d), Letterboxed(new ZoomState(2, 0, 0)).ToVideo(160, 120));
    }

    [Fact]
    public void Magnifying_spreads_the_frame_over_more_of_the_control()
    {
        // At 2x the right-hand edge of the control shows what was a quarter of
        // the way from the middle to the edge: pixel 960 of 1280.
        var point = Letterboxed(new ZoomState(2, 0, 0)).ToVideo(320, 120);

        Assert.NotNull(point);
        Assert.Equal(960d, point!.Value.X, 6);
        Assert.Equal(360d, point.Value.Y, 6);
    }

    [Fact]
    public void A_panned_picture_maps_from_where_it_was_moved_to()
    {
        // Magnified 2x and moved 40 to the right, the middle of the frame is
        // drawn 40 right of the middle of the control.
        var point = Letterboxed(new ZoomState(2, 40, 0)).ToVideo(200, 120);

        Assert.NotNull(point);
        Assert.Equal(640d, point!.Value.X, 6);
        Assert.Equal(360d, point.Value.Y, 6);
    }

    [Fact]
    public void A_stretched_picture_maps_across_the_whole_control()
    {
        // Stretch and Fill draw over the full control, so the content rectangle
        // is the control and there is no letterbox to account for.
        var projection = new VideoProjection(
            new ViewRect(0, 0, 320, 240), new ViewRect(0, 0, 320, 240), ZoomState.None, 1280, 720);

        Assert.Equal((0d, 0d), projection.ToVideo(0, 0));
        Assert.Equal((1280d, 720d), projection.ToVideo(320, 240));
    }

    [Fact]
    public void A_cropped_picture_maps_past_the_edges_of_the_control()
    {
        // Fill crops: the drawn rectangle is wider than the control and starts
        // left of it, so the control's left edge is already inside the frame.
        var projection = new VideoProjection(
            new ViewRect(-40, 0, 400, 240), new ViewRect(0, 0, 320, 240), ZoomState.None, 1280, 720);

        var point = projection.ToVideo(0, 0);

        Assert.NotNull(point);
        Assert.Equal(128d, point!.Value.X, 6);   // 40 of 400 units in, of 1280
        Assert.Equal(0d, point.Value.Y, 6);
    }

    [Theory]
    [InlineData(0, 180, 1280, 720)]
    [InlineData(320, 0, 1280, 720)]
    [InlineData(320, 180, 0, 720)]
    [InlineData(320, 180, 1280, 0)]
    [InlineData(double.NaN, 180, 1280, 720)]
    [InlineData(320, 180, double.NaN, 720)]
    public void An_unmeasured_projection_maps_nothing(
        double contentWidth, double contentHeight, double pixelWidth, double pixelHeight)
    {
        var projection = new VideoProjection(
            new ViewRect(0, 30, contentWidth, contentHeight),
            new ViewRect(0, 0, 320, 240),
            ZoomState.None,
            pixelWidth,
            pixelHeight);

        Assert.False(projection.IsMeasured);
        Assert.Null(projection.ToVideo(160, 120));
        Assert.Null(projection.ToView(640, 360));
    }

    [Fact]
    public void A_nonsense_magnification_maps_nothing()
    {
        var projection = Letterboxed(new ZoomState(0, 0, 0));

        Assert.Null(projection.ToVideo(160, 120));
        Assert.Null(projection.ToView(640, 360));
    }

    [Theory]
    [InlineData(double.NaN, 120)]
    [InlineData(160, double.PositiveInfinity)]
    public void A_nonsense_point_maps_nothing(double x, double y)
    {
        var projection = Letterboxed(ZoomState.None);

        Assert.Null(projection.ToVideo(x, y));
        Assert.Null(projection.ToView(x, y));
    }
}
