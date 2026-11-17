using AndroidX.Media3.Common;
using Indiko.Maui.Controls.VideoPlayer.Models;

namespace Indiko.Maui.Controls.VideoPlayer.Platforms.Android;

public partial class VideoPlayerViewHandler
{
    /// <summary>
    /// Translates ExoPlayer callbacks into the control's events.
    /// </summary>
    private sealed class PlayerListener(VideoPlayerViewHandler owner)
        : Java.Lang.Object, IPlayerListener
    {
        public void OnPlaybackStateChanged(int playbackState)
        {
            switch (playbackState)
            {
                case BasePlayer.InterfaceConsts.StateReady:
                    owner.OnPlayerReady();
                    owner.ResolveSeek(true);
                    break;

                case BasePlayer.InterfaceConsts.StateBuffering:
                    owner.Report?.OnStateChanged(VideoPlayerState.Buffering);
                    break;

                case BasePlayer.InterfaceConsts.StateEnded:
                    owner.Report?.OnStateChanged(VideoPlayerState.Ended);
                    owner.Report?.OnPlaybackEnded();
                    break;
            }
        }

        public void OnIsPlayingChanged(bool isPlaying)
        {
            owner.Report?.OnStateChanged(isPlaying ? VideoPlayerState.Playing : VideoPlayerState.Paused);

            if (isPlaying)
                owner.StartTicker();
            else
                owner.StopTicker();
        }

        public void OnPlayerError(PlaybackException? error)
        {
            owner.Fail(error is null ? VideoErrorCode.Unknown : MapError(error), -1, "Playback failed.");
        }

        public void OnPositionDiscontinuity(
            PlayerPositionInfo? oldPosition, PlayerPositionInfo? newPosition, int reason)
        {
            if (reason == BasePlayer.InterfaceConsts.DiscontinuityReasonSeek)
                owner.ResolveSeek(true);
        }

        public void OnVideoSizeChanged(VideoSize? videoSize) => owner.OnPlayerReady();

        // The codes that tell the app something it can act on are separated out;
        // everything else is Unknown rather than a guess, because a wrong code
        // sends the user to the wrong remedy.
        private static VideoErrorCode MapError(PlaybackException error) => error.ErrorCode switch
        {
            PlaybackException.ErrorCodeIoFileNotFound => VideoErrorCode.NotFound,
            PlaybackException.ErrorCodeIoNoPermission => VideoErrorCode.PermissionLost,
            PlaybackException.ErrorCodeIoNetworkConnectionFailed => VideoErrorCode.NetworkRequired,
            PlaybackException.ErrorCodeIoNetworkConnectionTimeout => VideoErrorCode.NetworkRequired,
            PlaybackException.ErrorCodeDecoderInitFailed => VideoErrorCode.UnsupportedFormat,
            PlaybackException.ErrorCodeDecodingFormatUnsupported => VideoErrorCode.UnsupportedFormat,
            PlaybackException.ErrorCodeParsingContainerUnsupported => VideoErrorCode.UnsupportedFormat,
            _ => VideoErrorCode.Unknown,
        };
    }

    private void ResolveSeek(bool reached)
    {
        if (_seeks.IsCurrent(_currentSeekGeneration))
            _seeks.Complete(_currentSeekGeneration, reached);
    }
}
