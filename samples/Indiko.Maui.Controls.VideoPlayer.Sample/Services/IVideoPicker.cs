namespace Indiko.Maui.Controls.VideoPlayer.Sample.Services;

/// <summary>
/// Presents the platform pickers and turns what comes back into references the
/// control can play.
/// </summary>
/// <remarks>
/// This lives in the sample rather than the package on purpose: when to show a
/// picker, and when it is reasonable to ask for photo-library access, are the
/// app's decisions. The package only reads what it is given.
/// </remarks>
public interface IVideoPicker
{
    /// <summary>iOS: the photo library. Android: the system photo picker.</summary>
    Task<IReadOnlyList<PickedClip>> PickFromPhotosAsync();

    /// <summary>iOS: the document picker. Android: the storage access framework.</summary>
    Task<IReadOnlyList<PickedClip>> PickFromFilesAsync();

    /// <summary>What the platform calls these two sources, for button labels.</summary>
    (string Photos, string Files) Labels { get; }
}
