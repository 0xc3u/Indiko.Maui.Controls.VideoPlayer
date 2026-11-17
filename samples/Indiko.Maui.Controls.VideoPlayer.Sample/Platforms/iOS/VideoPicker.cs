using Foundation;
using Indiko.Maui.Controls.VideoPlayer.Models;
using Indiko.Maui.Controls.VideoPlayer.Sample.Services;
using Photos;
using PhotosUI;
using UIKit;
using UniformTypeIdentifiers;

namespace Indiko.Maui.Controls.VideoPlayer.Sample.Platforms.iOS;

public sealed class VideoPicker : IVideoPicker
{
    public (string Photos, string Files) Labels => ("From Photos", "From Files");

    public Task<IReadOnlyList<PickedClip>> PickFromPhotosAsync()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<PickedClip>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // The configuration must be built from the shared photo library, not
        // the parameterless one. Only then does a result carry an
        // AssetIdentifier; without it the picker hands back an item that can
        // be copied out but never named again, which is exactly what this
        // control is trying to avoid.
        var configuration = new PHPickerConfiguration(PHPhotoLibrary.SharedPhotoLibrary)
        {
            Filter = PHPickerFilter.VideosFilter,
            SelectionLimit = 0,
        };

        var picker = new PHPickerViewController(configuration)
        {
            Delegate = new PhotoPickerDelegate(completion),
        };

        Present(picker, completion);
        return completion.Task;
    }

    public Task<IReadOnlyList<PickedClip>> PickFromFilesAsync()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<PickedClip>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // asCopy: false is the whole point. The copying overload duplicates a
        // multi-gigabyte recording into the app container and hands back a
        // path that is useless after the next launch.
        var picker = new UIDocumentPickerViewController(
            [UTTypes.Movie, UTTypes.Mpeg4Movie, UTTypes.QuickTimeMovie], asCopy: false)
        {
            AllowsMultipleSelection = true,
            Delegate = new DocumentPickerDelegate(completion),
        };

        Present(picker, completion);
        return completion.Task;
    }

    private static void Present(
        UIViewController picker, TaskCompletionSource<IReadOnlyList<PickedClip>> completion)
    {
        var root = UIApplication.SharedApplication.ConnectedScenes
            .ToArray()
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(window => window.IsKeyWindow)?
            .RootViewController;

        while (root?.PresentedViewController is { } presented)
            root = presented;

        if (root is null)
        {
            completion.TrySetResult([]);
            return;
        }

        root.PresentViewController(picker, animated: true, completionHandler: null);
    }

    private sealed class PhotoPickerDelegate(TaskCompletionSource<IReadOnlyList<PickedClip>> completion)
        : PHPickerViewControllerDelegate
    {
        public override async void DidFinishPicking(PHPickerViewController picker, PHPickerResult[] results)
        {
            picker.DismissViewController(animated: true, completionHandler: null);

            // The picker runs out of process and needs no authorisation, so
            // picking succeeds while the app still has none. Playback is a
            // different matter: the control reads the asset through PhotoKit
            // and only checks access, it never asks. Measured without this,
            // the first play after a pick failed with PermissionDenied. Asking
            // here, with the user's own selection as the context, is the
            // moment that makes sense.
            if (results.Length > 0
                && PHPhotoLibrary.GetAuthorizationStatus(PHAccessLevel.ReadWrite)
                    == PHAuthorizationStatus.NotDetermined)
            {
                await PHPhotoLibrary.RequestAuthorizationAsync(PHAccessLevel.ReadWrite);
            }

            var clips = new List<PickedClip>();

            foreach (var result in results)
            {
                if (string.IsNullOrEmpty(result.AssetIdentifier))
                    continue;

                clips.Add(new PickedClip(
                    VideoClipKind.PhotoAsset,
                    result.AssetIdentifier,
                    NameOf(result.AssetIdentifier)));
            }

            completion.TrySetResult(clips);
        }

        // The original file name is what makes a chapter set recognisable, and
        // it is only readable once the app has library access.
        private static string NameOf(string localIdentifier)
        {
            var assets = PHAsset.FetchAssetsUsingLocalIdentifiers([localIdentifier], null);

            if (assets.FirstObject is not PHAsset asset)
                return "photo asset";

            var resources = PHAssetResource.GetAssetResources(asset);
            return resources.Length > 0 ? resources[0].OriginalFilename : "photo asset";
        }
    }

    private sealed class DocumentPickerDelegate(TaskCompletionSource<IReadOnlyList<PickedClip>> completion)
        : UIDocumentPickerDelegate
    {
        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
        {
            var clips = new List<PickedClip>();

            foreach (var url in urls)
            {
                var bookmark = VideoReferences.CreateBookmark(url);

                if (bookmark is null)
                    continue;

                clips.Add(new PickedClip(
                    VideoClipKind.SecurityScopedBookmark, bookmark, url.LastPathComponent ?? "file"));
            }

            completion.TrySetResult(clips);
        }

        public override void WasCancelled(UIDocumentPickerViewController controller) =>
            completion.TrySetResult([]);
    }
}
