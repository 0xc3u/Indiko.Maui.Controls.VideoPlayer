namespace Indiko.Maui.Controls.VideoPlayer.Models;

/// <summary>How a clip's <see cref="VideoClip.Reference"/> is to be read.</summary>
public enum VideoClipKind
{
    /// <summary>An absolute path in the file system. Available on both platforms.</summary>
    FilePath = 10,

    /// <summary>
    /// An iOS <c>PHAsset.LocalIdentifier</c> from the photo library. The asset
    /// is read where it lies; nothing is exported or copied.
    /// </summary>
    PhotoAsset = 20,

    /// <summary>
    /// Base64 of iOS security-scoped bookmark data, as created for a file the
    /// user picked outside the app's container. Survives app restarts, which a
    /// plain path to such a file does not.
    /// </summary>
    SecurityScopedBookmark = 30,

    /// <summary>
    /// An Android <c>content://</c> URI. Needs a persisted read permission to
    /// survive a restart; see <c>VideoReferences.TakePersistablePermission</c>.
    /// </summary>
    ContentUri = 40,
}

/// <summary>
/// One video file, named by reference rather than by content.
/// </summary>
/// <remarks>
/// Nothing here is copied or exported. Training footage runs to gigabytes, and
/// an app that copies a recording into its own container to play it doubles the
/// storage and leaves the user to wonder where their space went.
/// </remarks>
public sealed record VideoClip(VideoClipKind Kind, string Reference)
{
    /// <summary>A clip read from an absolute file-system path.</summary>
    public static VideoClip FromFile(string path) => new(VideoClipKind.FilePath, path);

    /// <summary>A clip read from the iOS photo library by local identifier.</summary>
    public static VideoClip FromPhotoAsset(string localIdentifier) =>
        new(VideoClipKind.PhotoAsset, localIdentifier);

    /// <summary>A clip read through an iOS security-scoped bookmark (Base64).</summary>
    public static VideoClip FromBookmark(string base64Bookmark) =>
        new(VideoClipKind.SecurityScopedBookmark, base64Bookmark);

    /// <summary>A clip read from an Android <c>content://</c> URI.</summary>
    public static VideoClip FromContentUri(string contentUri) =>
        new(VideoClipKind.ContentUri, contentUri);
}
