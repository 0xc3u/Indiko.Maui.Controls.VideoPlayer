using Android.Content;
using Android.Runtime;
using Android.Util;
using Android.Views;
using AndroidX.Media3.UI;

namespace Indiko.Maui.Controls.VideoPlayer.Platforms.Android;

/// <summary>
/// A <c>PlayerView</c> that can hand its touches to the handler before the
/// player view itself sees them.
/// </summary>
/// <remarks>
/// The subclass exists for two reasons. It gives the layout a root element
/// whose name this package controls, which is how the TextureView surface type
/// gets set at all. And it puts the pinch handling in <c>OnTouchEvent</c>,
/// which runs after any touch listener MAUI attached for the app's own gesture
/// recognisers, rather than in a listener of our own that would replace theirs.
/// </remarks>
[Register("indiko.maui.controls.videoplayer.ZoomablePlayerView")]
public class ZoomablePlayerView : PlayerView
{
    public ZoomablePlayerView(Context context) : base(context) { }

    public ZoomablePlayerView(Context context, IAttributeSet? attrs) : base(context, attrs) { }

    public ZoomablePlayerView(Context context, IAttributeSet? attrs, int defStyleAttr)
        : base(context, attrs, defStyleAttr) { }

    protected ZoomablePlayerView(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer) { }

    /// <summary>
    /// Offered every touch the view is given; returning true consumes it.
    /// Null while the app has not asked for zoom gestures, which leaves touch
    /// handling exactly as it was.
    /// </summary>
    public Func<MotionEvent, bool>? Gesture { get; set; }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is not null && Gesture is { } gesture && gesture(e))
            return true;

        return base.OnTouchEvent(e);
    }
}
