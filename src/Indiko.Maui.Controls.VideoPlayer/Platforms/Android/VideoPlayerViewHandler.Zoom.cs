using Android.Views;
using AndroidX.Media3.UI;
using Indiko.Maui.Controls.VideoPlayer.Timeline;
using AndroidView = Android.Views.View;

namespace Indiko.Maui.Controls.VideoPlayer.Platforms.Android;

public partial class VideoPlayerViewHandler
{
    private ScaleGestureDetector? _pinch;
    private AndroidView? _zoomLayoutSource;
    private bool _dragging;
    private float _lastTouchX;
    private float _lastTouchY;
    private bool _reportingZoom;

    /// <summary>
    /// The frame the picture is drawn in: Media3's own
    /// <c>AspectRatioFrameLayout</c>, which is already exactly the letterboxed
    /// or cropped rectangle the resize mode asked for.
    /// </summary>
    /// <remarks>
    /// Transforming this rather than the surface inside it is what lets a
    /// magnified picture grow into the black bars: the frame's parent is the
    /// player view, which clips to the control, while the frame itself would
    /// clip to the unmagnified picture.
    /// </remarks>
    private AndroidView? ContentFrame => PlatformView?.VideoSurfaceView?.Parent as AndroidView;

    private double Density => PlatformView?.Resources?.DisplayMetrics?.Density ?? 1d;

    private static void MapZoom(VideoPlayerViewHandler handler, VideoPlayerView view) =>
        handler.ApplyZoom();

    private static void MapZoomGestures(VideoPlayerViewHandler handler, VideoPlayerView view)
    {
        if (view.IsPinchToZoomEnabled)
        {
            handler._pinch ??= new ScaleGestureDetector(
                handler.Context, new PinchListener(handler.OnScale));

            if (handler.PlatformView is ZoomablePlayerView zoomable)
                zoomable.Gesture = handler.OnTouch;
        }
        else
        {
            handler.ReleaseZoomGestures();
        }
    }

    private void ReleaseZoomGestures()
    {
        if (PlatformView is ZoomablePlayerView zoomable)
            zoomable.Gesture = null;

        _pinch?.Dispose();
        _pinch = null;
        _dragging = false;
    }

    private void OnPlatformLayout(object? sender, AndroidView.LayoutChangeEventArgs e) => ApplyZoom();

    /// <summary>The view whose layout decides how much room a pan has.</summary>
    private static AndroidView ZoomLayoutSource(PlayerView platformView) =>
        platformView.VideoSurfaceView?.Parent as AndroidView ?? platformView;

    /// <summary>
    /// Puts the view's zoom on the picture, clamped to what the picture holds,
    /// and reports back whatever survived the clamp.
    /// </summary>
    private void ApplyZoom()
    {
        if (VirtualView is not { } view)
            return;

        Apply(ZoomMath.Clamp(Requested(), Measure(), view.MaximumZoom));
    }

    private void Apply(ZoomState state)
    {
        if (ContentFrame is { } frame)
        {
            // The default pivot is the centre of the view and follows its size,
            // so magnifying needs no pivot of its own.
            frame.ScaleX = (float)state.Factor;
            frame.ScaleY = (float)state.Factor;
            frame.TranslationX = (float)state.OffsetX;
            frame.TranslationY = (float)state.OffsetY;
        }

        if (_reportingZoom || VirtualView is null)
            return;

        _reportingZoom = true;

        try
        {
            var density = Density;

            Report?.OnZoomChanged(
                state.Factor, new Point(state.OffsetX / density, state.OffsetY / density));
        }
        finally
        {
            _reportingZoom = false;
        }
    }

    // Everything inside the handler is in pixels, because that is what the
    // views and the touch events speak; the control's own offset is in
    // device-independent units, so the density conversion happens here and in
    // Apply, and nowhere else.
    private ZoomState Requested()
    {
        if (VirtualView is not { } view)
            return ZoomState.None;

        var density = Density;

        return new ZoomState(
            view.ZoomFactor, view.ZoomOffset.X * density, view.ZoomOffset.Y * density);
    }

    private ZoomBounds Measure()
    {
        if (PlatformView is not { } platformView)
            return default;

        var frame = ContentFrame;

        // Before the first layout the frame has no size. The player view's own
        // is the closest honest guess, and the next layout corrects it.
        var contentWidth = frame?.Width > 0 ? frame.Width : platformView.Width;
        var contentHeight = frame?.Height > 0 ? frame.Height : platformView.Height;

        return new ZoomBounds(
            contentWidth, contentHeight, platformView.Width, platformView.Height);
    }

    private bool CanPan()
    {
        if (VirtualView is not { } view)
            return false;

        var bounds = Measure();
        var factor = view.ZoomFactor;

        return ZoomMath.OffsetLimit(bounds.ContentWidth, bounds.ViewportWidth, factor) > 0 ||
               ZoomMath.OffsetLimit(bounds.ContentHeight, bounds.ViewportHeight, factor) > 0;
    }

    private bool OnScale(ScaleGestureDetector detector)
    {
        if (PlatformView is not { } platformView || VirtualView is not { } view)
            return false;

        Apply(ZoomMath.ScaleBy(
            Requested(),
            detector.ScaleFactor,
            detector.FocusX - (platformView.Width / 2d),
            detector.FocusY - (platformView.Height / 2d),
            Measure(),
            view.MaximumZoom));

        return true;
    }

    /// <summary>
    /// Handles a touch on the picture: a pinch magnifies, and a drag pans once
    /// there is something to pan.
    /// </summary>
    /// <remarks>
    /// The stream is kept from the first finger down, because a pinch that only
    /// declared itself on the second finger would never see one: a parent that
    /// was handed the gesture does not give it back. Intercepting is only
    /// disallowed once the gesture is certainly ours — two fingers, or a drag
    /// on a magnified picture — so until then a scrolling page can still take
    /// the drag away, which is what keeps the player usable inside a list.
    /// </remarks>
    private bool OnTouch(MotionEvent e)
    {
        if (VirtualView?.IsPinchToZoomEnabled != true)
            return false;

        _pinch?.OnTouchEvent(e);

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                Track(e, 0);
                _dragging = CanPan();

                if (_dragging)
                    HoldTouches(true);

                return true;

            case MotionEventActions.PointerDown:
                HoldTouches(true);

                // Two fingers are a pinch, and the pinch moves the picture by
                // its own focus; a drag on top of that would move it twice.
                _dragging = false;
                return true;

            case MotionEventActions.Move:
                if (_dragging && e.PointerCount == 1 && VirtualView is { } view)
                {
                    var dx = e.GetX() - _lastTouchX;
                    var dy = e.GetY() - _lastTouchY;

                    Track(e, 0);
                    Apply(ZoomMath.PanBy(Requested(), dx, dy, Measure(), view.MaximumZoom));
                }

                return true;

            case MotionEventActions.PointerUp:
                // The finger left on the glass starts a fresh drag from where it
                // is, rather than from wherever the lifted one had been.
                var remaining = e.ActionIndex == 0 ? 1 : 0;

                if (remaining < e.PointerCount)
                {
                    Track(e, remaining);
                    _dragging = CanPan();
                }

                return true;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                _dragging = false;
                HoldTouches(false);
                return true;

            default:
                return false;
        }
    }

    private void Track(MotionEvent e, int pointerIndex)
    {
        _lastTouchX = e.GetX(pointerIndex);
        _lastTouchY = e.GetY(pointerIndex);
    }

    private void HoldTouches(bool hold) =>
        PlatformView?.Parent?.RequestDisallowInterceptTouchEvent(hold);

    private sealed class PinchListener(Func<ScaleGestureDetector, bool> onScale)
        : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        public override bool OnScale(ScaleGestureDetector? detector) =>
            detector is not null && onScale(detector);
    }
}
