namespace Indiko.Maui.Controls.VideoPlayer.Models;

/// <summary>How the picture fills the control.</summary>
public enum VideoScalingMode
{
    /// <summary>Fit inside the bounds, keeping the aspect ratio. Letterboxed.</summary>
    Fit = 10,

    /// <summary>Fill the bounds, keeping the aspect ratio. Cropped.</summary>
    Fill = 20,

    /// <summary>Fill the bounds exactly, distorting the aspect ratio.</summary>
    Stretch = 30,
}

/// <summary>What the player is doing.</summary>
public enum VideoPlayerState
{
    /// <summary>No source assigned.</summary>
    Idle = 10,

    /// <summary>Resolving references and loading tracks.</summary>
    Opening = 20,

    /// <summary>Loaded and paused at a position.</summary>
    Ready = 30,

    /// <summary>Playing.</summary>
    Playing = 40,

    /// <summary>Paused by the app or the system.</summary>
    Paused = 50,

    /// <summary>Waiting for data mid-playback.</summary>
    Buffering = 60,

    /// <summary>Reached the end and not looping.</summary>
    Ended = 70,

    /// <summary>Failed; see the last <c>MediaFailed</c>.</summary>
    Failed = 80,
}

/// <summary>Why opening or playing failed.</summary>
/// <remarks>
/// Deliberately coarse and free of any detail about the file: these codes reach
/// crash reporters and logs, where a path, a content URI or an asset identifier
/// would be a privacy leak about the user's own library.
/// </remarks>
public enum VideoErrorCode
{
    /// <summary>The reference resolved to nothing: deleted, moved, or not in the library.</summary>
    NotFound = 10,

    /// <summary>The app has no photo-library access, so the asset cannot be read.</summary>
    PermissionDenied = 20,

    /// <summary>A permission that existed before is gone: a revoked persisted URI grant.</summary>
    PermissionLost = 30,

    /// <summary>The bookmark could not be resolved and could not be renewed.</summary>
    BookmarkInvalid = 40,

    /// <summary>Only a cloud copy exists and network access was not allowed.</summary>
    NetworkRequired = 50,

    /// <summary>The clip kind does not exist on this platform.</summary>
    UnsupportedSource = 60,

    /// <summary>The container or codec cannot be decoded here.</summary>
    UnsupportedFormat = 70,

    /// <summary>Anything else.</summary>
    Unknown = 80,
}
