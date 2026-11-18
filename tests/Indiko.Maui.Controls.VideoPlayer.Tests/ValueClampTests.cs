using Xunit;
using Indiko.Maui.Controls.VideoPlayer.Timeline;

namespace Indiko.Maui.Controls.VideoPlayer.Tests;

public class ValueClampTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(0.25, 0.25)]
    [InlineData(2, 2)]
    [InlineData(0.1, 0.1)]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    [InlineData(16, 16)]
    [InlineData(0, 0.1)]
    [InlineData(99, 16)]
    public void Speed_stays_between_a_tenth_and_sixteen_times(double requested, double expected)
    {
        Assert.Equal(expected, ValueClamp.Speed(requested), 6);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-3)]
    public void A_negative_speed_is_not_how_the_picture_runs_backwards(double requested)
    {
        // Direction is a verb here, not a sign: PlayReverse decides it, and it
        // can say that the platform or the media will not. A negative rate
        // smuggled through the speed would have to fail silently on Android.
        Assert.Equal(ValueClamp.MinimumSpeed, ValueClamp.Speed(requested), 6);
    }

    [Fact]
    public void A_nonsense_speed_falls_back_to_normal_play()
    {
        Assert.Equal(1, ValueClamp.Speed(double.NaN));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    [InlineData(0.5, 0.5)]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    public void Volume_stays_between_silence_and_full(double requested, double expected)
    {
        Assert.Equal(expected, ValueClamp.Volume(requested), 6);
    }

    [Fact]
    public void A_nonsense_volume_falls_back_to_full()
    {
        Assert.Equal(1, ValueClamp.Volume(double.NaN));
    }

    [Theory]
    [InlineData(15, 15)]
    [InlineData(10, 10)]
    [InlineData(30, 30)]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(1000, 30)]
    public void The_position_update_rate_stays_between_ten_and_thirty_hertz(int requested, int expected)
    {
        Assert.Equal(expected, ValueClamp.PositionUpdateRate(requested));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(-1, 0)]
    [InlineData(99, 10)]
    public void A_position_is_clamped_into_the_media_duration(double requested, double expected)
    {
        var clamped = ValueClamp.Position(TimeSpan.FromSeconds(requested), TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(expected), clamped);
    }

    [Fact]
    public void A_position_is_clamped_to_zero_while_the_duration_is_unknown()
    {
        Assert.Equal(TimeSpan.Zero, ValueClamp.Position(TimeSpan.FromSeconds(5), TimeSpan.Zero));
    }
}
