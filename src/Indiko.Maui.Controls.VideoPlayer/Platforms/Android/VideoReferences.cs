using Android.Content;
using AndroidUri = Android.Net.Uri;

namespace Indiko.Maui.Controls.VideoPlayer;

/// <summary>
/// Helpers for turning what a picker hands back into a reference that survives
/// an app restart.
/// </summary>
public static class VideoReferences
{
    /// <summary>
    /// Takes a persistable read permission for a URI from the photo picker or
    /// from <c>ACTION_OPEN_DOCUMENT</c>.
    /// </summary>
    /// <remarks>
    /// Without this the grant lives only as long as the activity that received
    /// it: the clip plays now and reports PermissionLost after the next app
    /// start. The system caps how many grants an app may hold (in the
    /// thousands), so an app that links many clips should release the ones it
    /// no longer needs.
    /// </remarks>
    /// <returns>Whether the permission is now held.</returns>
    public static bool TakePersistablePermission(Context context, AndroidUri uri)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(uri);

        try
        {
            context.ContentResolver?.TakePersistableUriPermission(
                uri, ActivityFlags.GrantReadUriPermission);

            return HasPersistedPermission(context, uri);
        }
        catch (Java.Lang.SecurityException)
        {
            // The URI did not come with a persistable grant — a picker result
            // already consumed, or a provider that does not offer one.
            return false;
        }
    }

    /// <summary>Whether a persisted read permission for this URI is still held.</summary>
    public static bool HasPersistedPermission(Context context, AndroidUri uri)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(uri);

        var persisted = context.ContentResolver?.PersistedUriPermissions;
        if (persisted is null)
            return false;

        foreach (var permission in persisted)
        {
            if (permission.IsReadPermission && permission.Uri?.Equals(uri) == true)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gives back a persisted read permission, for a clip the app has unlinked.
    /// </summary>
    public static void ReleasePersistablePermission(Context context, AndroidUri uri)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(uri);

        try
        {
            context.ContentResolver?.ReleasePersistableUriPermission(
                uri, ActivityFlags.GrantReadUriPermission);
        }
        catch (Java.Lang.SecurityException)
        {
            // Already released, or never held.
        }
    }
}
