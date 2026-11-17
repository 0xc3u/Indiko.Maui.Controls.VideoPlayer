using Android.App;
using Android.Content;
using Android.Content.PM;

using VideoPicker = Indiko.Maui.Controls.VideoPlayer.Sample.Platforms.Android.VideoPicker;

namespace Indiko.Maui.Controls.VideoPlayer.Sample;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // Picker results arrive here. The picker itself is a plain class, so it
    // cannot receive them without this hand-off.
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode is VideoPicker.RequestPhotos or VideoPicker.RequestFiles)
            VideoPicker.OnResult(requestCode, resultCode, data);
    }
}
