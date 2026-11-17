using Indiko.Maui.Controls.VideoPlayer;

namespace Indiko.Maui.Controls.VideoPlayer.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseIndikoVideoPlayer()
            .ConfigureFonts(fonts =>
            {
                // Plex Sans for text, Plex Mono only for digits that change:
                // its figures are tabular, so a running timecode does not
                // jitter as the numbers tick over.
                fonts.AddFont("IBMPlexSans-Regular.ttf", "PlexSans");
                fonts.AddFont("IBMPlexSans-SemiBold.ttf", "PlexSansSemi");
                fonts.AddFont("IBMPlexMono-Regular.ttf", "PlexMono");
                fonts.AddFont("IBMPlexMono-SemiBold.ttf", "PlexMonoSemi");
            });

        return builder.Build();
    }
}
