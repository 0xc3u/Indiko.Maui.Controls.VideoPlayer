namespace Indiko.Maui.Controls.VideoPlayer.Timeline;

/// <summary>
/// Which frame a player shows for a given seek position.
/// </summary>
/// <remarks>
/// The two platforms genuinely differ, and the difference is a full frame.
/// Measured on a 30 fps clip whose frame numbers are burnt into the picture:
/// seeking Media3 to 45.050 s shows frame 1352, whose timestamp is 45.067 s,
/// rather than frame 1351 which the position falls inside. AVFoundation shows
/// the frame the position falls inside. A single step target cannot satisfy
/// both, so the handlers say which rule their player follows.
/// </remarks>
public enum FrameSeekSemantics
{
    /// <summary>The frame whose span contains the position (AVFoundation).</summary>
    Containing = 10,

    /// <summary>The first frame whose timestamp is at or after the position (Media3 ExoPlayer).</summary>
    AtOrAfter = 20,
}
