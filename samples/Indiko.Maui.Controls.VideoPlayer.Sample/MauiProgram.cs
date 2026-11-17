using Indiko.Maui.Controls.VideoPlayer;

namespace Indiko.Maui.Controls.VideoPlayer.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseIndikoVideoPlayer();

        return builder.Build();
    }
}
