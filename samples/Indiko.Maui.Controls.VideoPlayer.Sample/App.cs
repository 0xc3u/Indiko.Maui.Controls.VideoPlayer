namespace Indiko.Maui.Controls.VideoPlayer.Sample;

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new NavigationPage(new MainPage()) { BarBackgroundColor = Color.FromArgb("#111111"), BarTextColor = Colors.White });
}
