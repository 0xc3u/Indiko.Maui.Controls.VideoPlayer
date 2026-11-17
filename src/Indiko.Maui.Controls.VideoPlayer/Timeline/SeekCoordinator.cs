namespace Indiko.Maui.Controls.VideoPlayer.Timeline;

/// <summary>
/// Tracks which seek is the current one, so an overtaken seek resolves instead
/// of waiting for a completion that will never come.
/// </summary>
/// <remarks>
/// Dragging a scrub bar starts a seek per movement, and the platforms do not
/// promise a completion callback for a seek that a newer one replaced: iOS
/// cancels pending seeks outright, and on Media3 the discontinuity that arrives
/// belongs to the newest target. An awaited <c>SeekExactAsync</c> therefore has
/// to be told it lost, and <c>false</c> says exactly that — the position asked
/// for is not where the player ended up.
/// </remarks>
public sealed class SeekCoordinator
{
    private readonly object _gate = new();

    private int _generation;
    private TaskCompletionSource<bool>? _pending;

    /// <summary>A seek that was begun: its generation, and the task its caller awaits.</summary>
    public readonly record struct SeekRequest(int Generation, Task<bool> Completion);

    /// <summary>
    /// Begins a seek, superseding any seek still in flight — whose caller is
    /// released with <c>false</c>.
    /// </summary>
    public SeekRequest Begin()
    {
        lock (_gate)
        {
            _pending?.TrySetResult(false);

            _generation++;
            _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            return new SeekRequest(_generation, _pending.Task);
        }
    }

    /// <summary>Whether this generation is still the seek the player is working on.</summary>
    public bool IsCurrent(int generation)
    {
        lock (_gate)
            return generation == _generation && _pending is not null;
    }

    /// <summary>
    /// Reports the outcome of a seek. A completion for a superseded generation
    /// is ignored: the platform callback for an old target must not resolve the
    /// seek that replaced it.
    /// </summary>
    public void Complete(int generation, bool success)
    {
        lock (_gate)
        {
            if (generation != _generation || _pending is null)
                return;

            _pending.TrySetResult(success);
            _pending = null;
        }
    }

    /// <summary>
    /// Releases a seek in flight with <c>false</c>, for a handler being torn
    /// down or a source being replaced. Leaving it unresolved would hang the
    /// caller on a player that no longer exists.
    /// </summary>
    public void CancelPending()
    {
        lock (_gate)
        {
            _pending?.TrySetResult(false);
            _pending = null;
            _generation++;
        }
    }
}
