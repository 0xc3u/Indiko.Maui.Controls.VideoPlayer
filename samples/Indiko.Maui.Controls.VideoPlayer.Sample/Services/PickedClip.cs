using System.Text.Json;
using Indiko.Maui.Controls.VideoPlayer.Models;
using Microsoft.Maui.Storage;

namespace Indiko.Maui.Controls.VideoPlayer.Sample.Services;

/// <summary>A clip the user picked, with a label the UI can show.</summary>
public sealed record PickedClip(VideoClipKind Kind, string Reference, string Label)
{
    public VideoClip ToClip() => new(Kind, Reference);
}

/// <summary>
/// Remembers what was picked, so the next app start can replay it.
/// </summary>
/// <remarks>
/// This is the point of the whole exercise. A path, a photo-library identifier
/// and a content URI all play fine in the session they were picked in; whether
/// they still play tomorrow is what separates a reference that was stored
/// properly from one that was not. The sample writes them to Preferences and
/// offers a button to load them back.
/// </remarks>
public static class PickedClipStore
{
    private const string Key = "picked-clips";

    public static void Save(IReadOnlyList<PickedClip> clips) =>
        Preferences.Default.Set(Key, JsonSerializer.Serialize(clips));

    public static IReadOnlyList<PickedClip> Load()
    {
        var json = Preferences.Default.Get(Key, string.Empty);

        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<PickedClip>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static void Clear() => Preferences.Default.Remove(Key);

    /// <summary>
    /// Replaces a stored reference, for a bookmark the player had to renew.
    /// </summary>
    public static void Replace(int index, string newReference)
    {
        var clips = Load().ToList();

        if (index < 0 || index >= clips.Count)
            return;

        clips[index] = clips[index] with { Reference = newReference };
        Save(clips);
    }
}
