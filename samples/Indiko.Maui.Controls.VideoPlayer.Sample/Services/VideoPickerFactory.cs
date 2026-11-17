namespace Indiko.Maui.Controls.VideoPlayer.Sample.Services;

public static class VideoPickerFactory
{
    public static IVideoPicker Create() =>
#if IOS
        new Platforms.iOS.VideoPicker();
#elif ANDROID
        new Platforms.Android.VideoPicker();
#else
        throw new PlatformNotSupportedException();
#endif
}
