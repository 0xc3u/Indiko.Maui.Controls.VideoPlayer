namespace Indiko.Maui.Controls.VideoPlayer.Models;

/// <summary>
/// What the player plays: one clip, or several that form a single timeline.
/// </summary>
/// <remarks>
/// Action cameras split a long recording into chapters — a GoPro writes
/// GH010123.MP4, GH020123.MP4, GH030123.MP4 for one take — and the viewer
/// thinks of that as one video. Handing the clips over in order produces one
/// timeline whose duration is their sum, so positions and seeks never have to
/// mention which file they land in.
/// </remarks>
public sealed class VideoPlayerSource
{
    /// <param name="clips">Clips in playback order; at least one.</param>
    /// <exception cref="ArgumentException">The sequence is empty, or a reference is blank.</exception>
    public VideoPlayerSource(IEnumerable<VideoClip> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);

        var ordered = clips.ToArray();

        if (ordered.Length == 0)
            throw new ArgumentException("A source needs at least one clip.", nameof(clips));

        if (ordered.Any(c => c is null || string.IsNullOrWhiteSpace(c.Reference)))
            throw new ArgumentException("Every clip needs a reference.", nameof(clips));

        Clips = ordered;
    }

    /// <summary>The clips in playback order. Chapter index <c>n</c> is <c>Clips[n]</c>.</summary>
    public IReadOnlyList<VideoClip> Clips { get; }

    /// <summary>A single-clip source from an absolute file-system path.</summary>
    public static VideoPlayerSource FromFile(string path) => FromClip(VideoClip.FromFile(path));

    /// <summary>A single-clip source.</summary>
    public static VideoPlayerSource FromClip(VideoClip clip) => new([clip]);

    /// <summary>A source whose clips play back to back as one timeline.</summary>
    public static VideoPlayerSource FromChapters(IEnumerable<VideoClip> clips) => new(clips);
}
