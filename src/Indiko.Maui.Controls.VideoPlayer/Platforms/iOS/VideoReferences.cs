using Foundation;

namespace Indiko.Maui.Controls.VideoPlayer;

/// <summary>
/// Helpers for turning what a picker hands back into a reference that survives
/// an app restart.
/// </summary>
/// <remarks>
/// The control does not present pickers: which picker to show, and when to ask
/// for photo-library access, are decisions that belong to the app. These are
/// the small pieces that are easy to get wrong — a bookmark created outside the
/// security-scoped access is useless, and a plain path to a file outside the
/// container stops working as soon as the app restarts.
/// </remarks>
public static class VideoReferences
{
    /// <summary>
    /// Creates a security-scoped bookmark for a URL from
    /// <c>UIDocumentPickerViewController</c> (opened with <c>asCopy: false</c>),
    /// as Base64 for <see cref="Models.VideoClipKind.SecurityScopedBookmark"/>.
    /// </summary>
    /// <returns>The bookmark, or null when one could not be created.</returns>
    public static string? CreateBookmark(NSUrl url)
    {
        ArgumentNullException.ThrowIfNull(url);

        // The bookmark has to be created while the access is open, or it
        // records a file the app has no right to and fails on resolution.
        var opened = url.StartAccessingSecurityScopedResource();

        try
        {
            var data = url.CreateBookmarkData(NSUrlBookmarkCreationOptions.None, [], null, out var error);

            return error is null && data is not null
                ? data.GetBase64EncodedString(NSDataBase64EncodingOptions.None)
                : null;
        }
        finally
        {
            if (opened)
                url.StopAccessingSecurityScopedResource();
        }
    }
}
