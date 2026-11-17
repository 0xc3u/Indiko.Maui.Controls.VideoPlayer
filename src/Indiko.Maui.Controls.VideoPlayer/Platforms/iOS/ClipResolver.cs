using AVFoundation;
using Foundation;
using Indiko.Maui.Controls.VideoPlayer.Models;
using Photos;

namespace Indiko.Maui.Controls.VideoPlayer.Platforms.iOS;

/// <summary>A clip turned into something AVFoundation can play.</summary>
internal sealed class ResolvedClip(AVAsset asset) : IDisposable
{
    public AVAsset Asset { get; } = asset;

    /// <summary>
    /// The security-scoped URL whose access must stay open for as long as the
    /// asset is in use, and be closed when it is not.
    /// </summary>
    public NSUrl? ScopedUrl { get; init; }

    /// <summary>A renewed bookmark, when the one passed in had gone stale.</summary>
    public string? RenewedBookmark { get; init; }

    public void Dispose()
    {
        // Letting a security-scoped access leak would eventually exhaust the
        // per-process limit and start failing unrelated files.
        ScopedUrl?.StopAccessingSecurityScopedResource();
        ScopedUrl?.Dispose();
        Asset.Dispose();
    }
}

/// <summary>The outcome of resolving one clip: an asset, or a reason it failed.</summary>
internal readonly record struct ClipResolution(ResolvedClip? Clip, VideoErrorCode? Error, string Message)
{
    public static ClipResolution Ok(ResolvedClip clip) => new(clip, null, string.Empty);

    public static ClipResolution Fail(VideoErrorCode error, string message) => new(null, error, message);
}

/// <summary>
/// Turns the four kinds of reference into AVFoundation assets, without copying
/// or exporting anything.
/// </summary>
internal static class ClipResolver
{
    public static Task<ClipResolution> ResolveAsync(
        VideoClip clip,
        bool allowNetworkAccess,
        Action<double> onDownloadProgress,
        CancellationToken cancellationToken) => clip.Kind switch
        {
            VideoClipKind.FilePath => Task.FromResult(ResolveFile(clip.Reference)),
            VideoClipKind.SecurityScopedBookmark => Task.FromResult(ResolveBookmark(clip.Reference)),
            VideoClipKind.PhotoAsset =>
                ResolvePhotoAssetAsync(clip.Reference, allowNetworkAccess, onDownloadProgress, cancellationToken),
            _ => Task.FromResult(ClipResolution.Fail(
                VideoErrorCode.UnsupportedSource,
                $"{clip.Kind} is not a source kind that exists on iOS.")),
        };

    private static ClipResolution ResolveFile(string path)
    {
        if (!File.Exists(path))
            return ClipResolution.Fail(VideoErrorCode.NotFound, "The file does not exist.");

        var url = NSUrl.FromFilename(path);
        return ClipResolution.Ok(new ResolvedClip(new AVUrlAsset(url)));
    }

    private static ClipResolution ResolveBookmark(string base64)
    {
        NSData? data;
        try
        {
            data = new NSData(base64, NSDataBase64DecodingOptions.None);
        }
        catch (Exception)
        {
            return ClipResolution.Fail(VideoErrorCode.BookmarkInvalid, "The bookmark could not be decoded.");
        }

        if (data.Length == 0)
            return ClipResolution.Fail(VideoErrorCode.BookmarkInvalid, "The bookmark was empty.");

        var url = NSUrl.FromBookmarkData(
            data, NSUrlBookmarkResolutionOptions.WithoutUI, null, out var isStale, out var error);

        if (url is null || error is not null)
            return ClipResolution.Fail(VideoErrorCode.BookmarkInvalid, "The bookmark could not be resolved.");

        if (!url.StartAccessingSecurityScopedResource())
        {
            return ClipResolution.Fail(
                VideoErrorCode.PermissionDenied, "Access to the bookmarked file was refused.");
        }

        // A bookmark goes stale when the file is moved or renamed, or after an
        // OS update. It still resolves, but only this once: renewing it now and
        // handing the new data back is what keeps the link working next launch.
        string? renewed = null;
        if (isStale)
            renewed = TryCreateBookmark(url);

        return ClipResolution.Ok(new ResolvedClip(new AVUrlAsset(url))
        {
            ScopedUrl = url,
            RenewedBookmark = renewed,
        });
    }

    private static string? TryCreateBookmark(NSUrl url)
    {
        var data = url.CreateBookmarkData(
            NSUrlBookmarkCreationOptions.None, [], null, out var error);

        return error is null && data is not null ? data.GetBase64EncodedString(NSDataBase64EncodingOptions.None) : null;
    }

    private static async Task<ClipResolution> ResolvePhotoAssetAsync(
        string localIdentifier,
        bool allowNetworkAccess,
        Action<double> onDownloadProgress,
        CancellationToken cancellationToken)
    {
        // Authorisation is checked, never requested: a control cannot know when
        // in the app's flow a system prompt is appropriate, and a prompt raised
        // from a view appearing is the kind the user denies.
        var status = PHPhotoLibrary.GetAuthorizationStatus(PHAccessLevel.ReadWrite);
        if (status is not (PHAuthorizationStatus.Authorized or PHAuthorizationStatus.Limited))
        {
            return ClipResolution.Fail(
                VideoErrorCode.PermissionDenied, "The app has no access to the photo library.");
        }

        var assets = PHAsset.FetchAssetsUsingLocalIdentifiers([localIdentifier], null);
        if (assets.FirstObject() is not PHAsset asset)
        {
            // Also the "limited access" case where this particular asset was
            // never shared with the app: indistinguishable from deleted, and the
            // remedy the app should offer is the same — pick it again.
            return ClipResolution.Fail(
                VideoErrorCode.NotFound, "The asset is not in the library, or not shared with the app.");
        }

        var options = new PHVideoRequestOptions
        {
            // Original rather than a rendered version: a slow-motion recording
            // delivered as an edited composition arrives already slowed down,
            // which would make the frame rate and every frame number wrong.
            Version = PHVideoRequestOptionsVersion.Original,
            DeliveryMode = PHVideoRequestOptionsDeliveryMode.HighQualityFormat,
            NetworkAccessAllowed = allowNetworkAccess,
        };

        if (allowNetworkAccess)
            options.ProgressHandler = (progress, _, _, _) => onDownloadProgress(progress);

        var completion = new TaskCompletionSource<ClipResolution>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var requestId = PHImageManager.DefaultManager.RequestAVAsset(
            asset, options, (avAsset, _, info) =>
            {
                if (avAsset is not null)
                {
                    completion.TrySetResult(ClipResolution.Ok(new ResolvedClip(avAsset)));
                    return;
                }

                var inCloud = info?[PHImageKeys.ResultIsInCloud] is NSNumber { BoolValue: true };

                completion.TrySetResult(inCloud && !allowNetworkAccess
                    ? ClipResolution.Fail(
                        VideoErrorCode.NetworkRequired,
                        "Only a cloud copy exists and network access was not allowed.")
                    : ClipResolution.Fail(VideoErrorCode.NotFound, "The asset could not be loaded."));
            });

        using var registration = cancellationToken.Register(() =>
        {
            PHImageManager.DefaultManager.CancelImageRequest(requestId);
            completion.TrySetResult(ClipResolution.Fail(VideoErrorCode.Unknown, "The request was cancelled."));
        });

        return await completion.Task.ConfigureAwait(false);
    }
}
