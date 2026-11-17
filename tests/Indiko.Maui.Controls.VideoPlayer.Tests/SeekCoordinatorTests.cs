using Xunit;
using Indiko.Maui.Controls.VideoPlayer.Timeline;

namespace Indiko.Maui.Controls.VideoPlayer.Tests;

public class SeekCoordinatorTests
{
    [Fact]
    public async Task A_completed_seek_reports_success()
    {
        var coordinator = new SeekCoordinator();

        var seek = coordinator.Begin();
        coordinator.Complete(seek.Generation, true);

        Assert.True(await seek.Completion);
    }

    [Fact]
    public async Task A_seek_overtaken_by_a_newer_one_reports_false_immediately()
    {
        // Dragging a scrub bar starts a seek per movement. The caller of an
        // overtaken seek has to learn that its position is no longer the target,
        // or it will keep drawing a playhead the picture has left behind.
        var coordinator = new SeekCoordinator();

        var first = coordinator.Begin();
        var second = coordinator.Begin();

        Assert.False(await first.Completion);
        Assert.False(first.Completion.IsCanceled);

        coordinator.Complete(second.Generation, true);
        Assert.True(await second.Completion);
    }

    [Fact]
    public void An_overtaken_generation_is_no_longer_current()
    {
        var coordinator = new SeekCoordinator();

        var first = coordinator.Begin();
        var second = coordinator.Begin();

        Assert.False(coordinator.IsCurrent(first.Generation));
        Assert.True(coordinator.IsCurrent(second.Generation));
    }

    [Fact]
    public async Task Completing_a_stale_generation_does_not_resolve_the_current_seek()
    {
        var coordinator = new SeekCoordinator();

        var first = coordinator.Begin();
        var second = coordinator.Begin();

        coordinator.Complete(first.Generation, true);

        Assert.False(await first.Completion);
        Assert.False(second.Completion.IsCompleted);
    }

    [Fact]
    public async Task Cancelling_resolves_the_pending_seek_as_false()
    {
        // The handler is torn down while a seek is in flight: the awaiting
        // caller must be released rather than left hanging on a dead player.
        var coordinator = new SeekCoordinator();

        var seek = coordinator.Begin();
        coordinator.CancelPending();

        Assert.False(await seek.Completion);
        Assert.False(coordinator.IsCurrent(seek.Generation));
    }

    [Fact]
    public async Task Completing_twice_is_harmless()
    {
        var coordinator = new SeekCoordinator();

        var seek = coordinator.Begin();
        coordinator.Complete(seek.Generation, true);
        coordinator.Complete(seek.Generation, false);

        Assert.True(await seek.Completion);
    }

    [Fact]
    public void Generations_are_distinct_and_increasing()
    {
        var coordinator = new SeekCoordinator();

        var first = coordinator.Begin().Generation;
        var second = coordinator.Begin().Generation;

        Assert.True(second > first);
    }

    [Fact]
    public async Task A_cancelled_seek_can_be_followed_by_a_new_one()
    {
        var coordinator = new SeekCoordinator();

        coordinator.CancelPending();
        var seek = coordinator.Begin();
        coordinator.Complete(seek.Generation, true);

        Assert.True(await seek.Completion);
    }
}
