using Android.App;
using Android.Content;
using Android.Provider;
using Indiko.Maui.Controls.VideoPlayer.Models;
using Indiko.Maui.Controls.VideoPlayer.Sample.Services;
using AndroidUri = Android.Net.Uri;

namespace Indiko.Maui.Controls.VideoPlayer.Sample.Platforms.Android;

public sealed class VideoPicker : IVideoPicker
{
    internal const int RequestPhotos = 4201;
    internal const int RequestFiles = 4202;

    private static TaskCompletionSource<IReadOnlyList<PickedClip>>? _pending;

    public (string Photos, string Files) Labels => ("Photo picker", "Files (SAF)");

    public Task<IReadOnlyList<PickedClip>> PickFromPhotosAsync()
    {
        // The system photo picker arrived in API 33. Below that the storage
        // access framework is the only route that needs no media permission.
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return PickFromFilesAsync();

        var intent = new Intent(MediaStore.ActionPickImages);
        intent.SetType("video/*");
        intent.PutExtra(MediaStore.ExtraPickImagesMax, 10);

        return StartAsync(intent, RequestPhotos);
    }

    public Task<IReadOnlyList<PickedClip>> PickFromFilesAsync()
    {
        // ACTION_OPEN_DOCUMENT rather than ACTION_GET_CONTENT: only the former
        // hands back a URI whose read permission can be persisted, and without
        // that the clip stops playing after the next app start.
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("video/*");
        intent.PutExtra(Intent.ExtraAllowMultiple, true);

        return StartAsync(intent, RequestFiles);
    }

    private static Task<IReadOnlyList<PickedClip>> StartAsync(Intent intent, int requestCode)
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;

        if (activity is null)
            return Task.FromResult<IReadOnlyList<PickedClip>>([]);

        _pending?.TrySetResult([]);
        _pending = new TaskCompletionSource<IReadOnlyList<PickedClip>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        activity.StartActivityForResult(intent, requestCode);
        return _pending.Task;
    }

    /// <summary>Called from MainActivity, which is where results arrive.</summary>
    internal static void OnResult(int requestCode, Result resultCode, Intent? data)
    {
        if (_pending is not { } pending)
            return;

        _pending = null;

        if (resultCode != Result.Ok || data is null)
        {
            pending.TrySetResult([]);
            return;
        }

        var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var clips = new List<PickedClip>();

        foreach (var uri in UrisOf(data))
        {
            var persisted = false;

            if (context is not null)
            {
                // Measured on API 36: both the storage access framework and
                // the system photo picker hand out grants that can be
                // persisted, and both still play after a cold restart. That is
                // not promised everywhere - older versions and third-party
                // providers can refuse - so the result is shown rather than
                // assumed, because it decides whether the reference is still
                // playable tomorrow.
                persisted = VideoReferences.TakePersistablePermission(context, uri);
            }

            var label = DisplayNameOf(context, uri) ?? uri.LastPathSegment ?? "video";
            if (!persisted)
                label += " (session only)";

            clips.Add(new PickedClip(VideoClipKind.ContentUri, uri.ToString()!, label));
        }

        pending.TrySetResult(clips);
    }

    private static IEnumerable<AndroidUri> UrisOf(Intent data)
    {
        if (data.ClipData is { } clip)
        {
            for (var i = 0; i < clip.ItemCount; i++)
            {
                if (clip.GetItemAt(i)?.Uri is { } uri)
                    yield return uri;
            }

            yield break;
        }

        if (data.Data is { } single)
            yield return single;
    }

    private static string? DisplayNameOf(Context? context, AndroidUri uri)
    {
        if (context is null)
            return null;

        try
        {
            using var cursor = context.ContentResolver?.Query(
                uri, [IOpenableColumns.DisplayName], null, null, null);

            if (cursor is not null && cursor.MoveToFirst())
                return cursor.GetString(0);
        }
        catch (Exception)
        {
            // A provider that refuses the query is not a reason to fail the
            // pick; the URI itself is what matters.
        }

        return null;
    }
}
