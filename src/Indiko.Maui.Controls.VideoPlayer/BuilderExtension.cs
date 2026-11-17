namespace Indiko.Maui.Controls.VideoPlayer;

public static class BuilderExtension
{
    /// <summary>
    /// Registers the <see cref="VideoPlayerView"/> handler. Call this in
    /// MauiProgram.cs before the first view is created.
    /// </summary>
    public static MauiAppBuilder UseIndikoVideoPlayer(this MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers =>
        {
#if ANDROID
            handlers.AddHandler<VideoPlayerView, Platforms.Android.VideoPlayerViewHandler>();
#elif IOS
            handlers.AddHandler<VideoPlayerView, Platforms.iOS.VideoPlayerViewHandler>();
#endif
        });

        return builder;
    }
}
