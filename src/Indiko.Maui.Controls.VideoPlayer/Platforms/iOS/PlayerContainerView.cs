using AVFoundation;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Indiko.Maui.Controls.VideoPlayer.Platforms.iOS;

/// <summary>
/// A plain <see cref="UIView"/> holding a surface backed by an
/// <see cref="AVPlayerLayer"/>, clipped to its own bounds.
/// </summary>
/// <remarks>
/// Deliberately not <c>AVPlayerViewController</c>: that brings Apple's playback
/// UI, which cannot be reduced to a bare picture and would sit on top of the
/// app's own frame-stepping controls.
/// <para>
/// The player layer backs an inner view rather than this one so that magnifying
/// has somewhere to happen. A transform on the layer that backs the outer view
/// would draw outside it — over the app's controls, since nothing above is
/// obliged to clip — and setting a frame on a transformed view is undefined in
/// UIKit. With an inner view the outer one keeps its untransformed bounds and
/// does the clipping, and the surface carries the zoom.
/// </para>
/// </remarks>
[Register(nameof(PlayerContainerView))]
public sealed class PlayerContainerView : UIView
{
    private readonly PlayerSurfaceView _surface = new();

    public PlayerContainerView() => Initialise();

    public PlayerContainerView(NSCoder coder) : base(coder) => Initialise();

    private PlayerContainerView(NativeHandle handle) : base(handle) => Initialise();

    /// <summary>The view was laid out, so the picture has to be measured again.</summary>
    public event EventHandler? DidLayout;

    public AVPlayerLayer PlayerLayer => _surface.PlayerLayer;

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

    /// <summary>
    /// The picture as drawn: the part of the video rectangle that is actually
    /// on screen.
    /// </summary>
    /// <remarks>
    /// <c>VideoRect</c> accounts for the gravity, the rotation metadata and any
    /// pixel aspect ratio, which is what makes it the right thing to pan
    /// against. Under <see cref="AVLayerVideoGravity.ResizeAspectFill"/> it
    /// reaches past the bounds, and the part outside is cropped away rather
    /// than hidden — panning towards it would uncover black — so the
    /// intersection is what counts. Empty until the first frame is ready.
    /// </remarks>
    public CGRect ContentRect
    {
        get
        {
            var video = PlayerLayer.VideoRect;

            return video.IsEmpty ? CGRect.Empty : CGRect.Intersect(video, _surface.Bounds);
        }
    }

    /// <summary>
    /// Magnifies the picture about the centre of the view and moves it by
    /// <paramref name="offsetX"/> and <paramref name="offsetY"/> points.
    /// </summary>
    public void ApplyZoom(double factor, double offsetX, double offsetY)
    {
        var scale = CGAffineTransform.MakeScale((nfloat)factor, (nfloat)factor);

        // Scale first, then translate: CoreGraphics multiplies left to right,
        // so the offset stays in view points instead of being magnified too.
        _surface.Transform = CGAffineTransform.Multiply(
            scale, CGAffineTransform.MakeTranslation((nfloat)offsetX, (nfloat)offsetY));
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        // Bounds and centre rather than Frame, which is derived from the
        // transform and would fight with the zoom.
        _surface.Bounds = new CGRect(CGPoint.Empty, Bounds.Size);
        _surface.Center = new CGPoint(Bounds.GetMidX(), Bounds.GetMidY());

        DidLayout?.Invoke(this, EventArgs.Empty);
    }

    private void Initialise()
    {
        BackgroundColor = UIColor.Black;

        // The zoomed surface has to stop at the edge of the control; the app's
        // own layout is not responsible for containing it.
        ClipsToBounds = true;

        AddSubview(_surface);
    }
}

/// <summary>The surface the video is drawn on, and the view the zoom transforms.</summary>
[Register(nameof(PlayerSurfaceView))]
public sealed class PlayerSurfaceView : UIView
{
    public PlayerSurfaceView() => BackgroundColor = UIColor.Black;

    public PlayerSurfaceView(NSCoder coder) : base(coder) => BackgroundColor = UIColor.Black;

    private PlayerSurfaceView(NativeHandle handle) : base(handle) { }

    // Backing the view with the player layer, rather than adding a sublayer,
    // means the layer follows the view's bounds with no layout code of its own.
    [Export("layerClass")]
    public static Class GetLayerClass() => new(typeof(AVPlayerLayer));

    public AVPlayerLayer PlayerLayer => (AVPlayerLayer)Layer;
}
