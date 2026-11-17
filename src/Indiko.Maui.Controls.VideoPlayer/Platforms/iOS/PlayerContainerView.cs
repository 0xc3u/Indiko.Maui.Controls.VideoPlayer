using AVFoundation;
using CoreAnimation;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Indiko.Maui.Controls.VideoPlayer.Platforms.iOS;

/// <summary>
/// A plain <see cref="UIView"/> backed by an <see cref="AVPlayerLayer"/>.
/// </summary>
/// <remarks>
/// Deliberately not <c>AVPlayerViewController</c>: that brings Apple's playback
/// UI, which cannot be reduced to a bare picture and would sit on top of the
/// app's own frame-stepping controls. Backing the view with the player layer
/// (rather than adding a sublayer) means the layer resizes with the view for
/// free, with no layout callback to keep in step.
/// </remarks>
[Register(nameof(PlayerContainerView))]
public sealed class PlayerContainerView : UIView
{
    public PlayerContainerView() => BackgroundColor = UIColor.Black;

    public PlayerContainerView(NSCoder coder) : base(coder) => BackgroundColor = UIColor.Black;

    private PlayerContainerView(NativeHandle handle) : base(handle) { }

    [Export("layerClass")]
    public static Class GetLayerClass() => new(typeof(AVPlayerLayer));

    public AVPlayerLayer PlayerLayer => (AVPlayerLayer)Layer;

    public AVPlayer? Player
    {
        get => PlayerLayer.Player;
        set => PlayerLayer.Player = value;
    }

    public AVLayerVideoGravity VideoGravity
    {
        get => PlayerLayer.VideoGravity;
        set => PlayerLayer.VideoGravity = value;
    }
}
