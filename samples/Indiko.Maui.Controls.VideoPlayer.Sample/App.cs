namespace Indiko.Maui.Controls.VideoPlayer.Sample;

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new NavigationPage(new MainPage())
        {
            // The chrome belongs to the same instrument as the panel below it.
            BarBackgroundColor = Ink.Panel,
            BarTextColor = Ink.Text,
        });
}
