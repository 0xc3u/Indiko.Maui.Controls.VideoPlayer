using Foundation;

namespace Indiko.Maui.Controls.VideoPlayer.Platforms.iOS;

/// <summary>
/// Observes one key path and calls back, so the handler does not have to be an
/// <see cref="NSObject"/> itself.
/// </summary>
/// <remarks>
/// AVFoundation reports readiness and play/pause transitions through KVO only;
/// there is no event or delegate for them. A MAUI handler is a plain object, so
/// something deriving from NSObject has to stand in.
/// </remarks>
internal sealed class KeyPathObserver : NSObject
{
    private readonly Action _onChange;
    private NSObject? _target;
    private string? _keyPath;

    public KeyPathObserver(NSObject target, string keyPath, Action onChange)
    {
        _onChange = onChange;
        _target = target;
        _keyPath = keyPath;

        target.AddObserver(this, (NSString)keyPath, NSKeyValueObservingOptions.New, IntPtr.Zero);
    }

    public override void ObserveValue(
        NSString keyPath, NSObject ofObject, NSDictionary change, IntPtr context) => _onChange();

    public void Stop()
    {
        if (_target is null || _keyPath is null)
            return;

        try
        {
            _target.RemoveObserver(this, (NSString)_keyPath);
        }
        catch (MonoTouchException)
        {
            // The observer was already gone; nothing left to detach.
        }

        _target = null;
        _keyPath = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Stop();

        base.Dispose(disposing);
    }
}
