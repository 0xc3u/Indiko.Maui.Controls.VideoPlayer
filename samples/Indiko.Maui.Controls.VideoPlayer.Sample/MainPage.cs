using Indiko.Maui.Controls.VideoPlayer.Models;
using Indiko.Maui.Controls.VideoPlayer.Timeline;

namespace Indiko.Maui.Controls.VideoPlayer.Sample;

/// <summary>
/// Exercises every part of the control's API, and shows enough numbers to
/// check the claims by eye.
/// </summary>
/// <remarks>
/// The readouts matter as much as the controls: the test footage has its frame
/// number burnt into the picture, and the panel shows the frame number computed
/// from position and frame rate. When the two agree after a seek, seeking is
/// frame accurate — which is otherwise not something you can see.
/// </remarks>
public class MainPage : ContentPage
{
    private static readonly string[] BundledChapters =
        ["GH010001.MP4", "GH020001.MP4", "GH030001.MP4"];

    private readonly VideoPlayerView _player;
    private readonly Label _state = Readout();
    private readonly Label _position = Readout();
    private readonly Label _chapter = Readout();
    private readonly Label _format = Readout();
    private readonly Label _frame = Readout();
    private readonly Label _rate = Readout();
    private readonly Label _error = Readout();
    private readonly Slider _scrub;
    private readonly Entry _seekEntry;

    private readonly Queue<DateTime> _eventTimes = new();
    private bool _scrubbing;

    public MainPage()
    {
        Title = "Indiko VideoPlayer";
        BackgroundColor = Color.FromArgb("#0E0E14");

        _player = new VideoPlayerView
        {
            HeightRequest = 220,
            ScalingMode = VideoScalingMode.Fit,
            PositionUpdateRate = 15,
        };

        _player.MediaOpened += OnMediaOpened;
        _player.MediaFailed += OnMediaFailed;
        _player.PositionChanged += OnPositionChanged;
        _player.StateChanged += OnStateChanged;
        _player.PlaybackEnded += (_, _) => _error.Text = "playback ended";
        _player.BookmarkRefreshed += (_, e) =>
            _error.Text = $"bookmark renewed for chapter {e.ChapterIndex}";
        _player.DownloadProgress += (_, e) =>
            _error.Text = $"iCloud download {e.Progress:P0}";

        _scrub = new Slider { Maximum = 1, Minimum = 0, ThumbColor = Colors.White };
        _scrub.DragStarted += (_, _) => { _scrubbing = true; _player.BeginScrub(); };
        _scrub.DragCompleted += (_, _) => { _scrubbing = false; _player.EndScrub(); };
        _scrub.ValueChanged += (_, e) =>
        {
            if (_scrubbing && _player.Duration > TimeSpan.Zero)
                _player.ScrubTo(_player.Duration * e.NewValue);
        };

        _seekEntry = new Entry
        {
            Placeholder = "seconds, e.g. 23.5",
            Keyboard = Keyboard.Numeric,
            TextColor = Colors.White,
            WidthRequest = 140,
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 12,
                Spacing = 10,
                Children =
                {
                    _player,
                    Section("Source"),
                    Row(
                        Button("Bundled chapters", LoadBundledChaptersAsync),
                        Button("Single clip", LoadSingleBundledClipAsync)),
                    Row(
                        Button("Missing file", () => { _player.Source = VideoPlayerSource.FromFile("/does/not/exist.mp4"); return Task.CompletedTask; }),
                        Button("Unsupported kind", () => { _player.Source = VideoPlayerSource.FromClip(VideoClip.FromPhotoAsset("no-such-asset")); return Task.CompletedTask; }),
                        Button("Clear", () => { _player.Source = null; return Task.CompletedTask; })),

                    Section("Transport"),
                    Row(
                        Button("-10f", () => _player.StepFrameAsync(-10)),
                        Button("-1f", () => _player.StepFrameAsync(-1)),
                        Button("Play", () => { _player.Play(); return Task.CompletedTask; }),
                        Button("Pause", () => { _player.Pause(); return Task.CompletedTask; }),
                        Button("+1f", () => _player.StepFrameAsync(1)),
                        Button("+10f", () => _player.StepFrameAsync(10))),
                    Row(
                        Button("Stop", () => { _player.Stop(); return Task.CompletedTask; }),
                        Button("0.25x", () => SetSpeed(0.25)),
                        Button("0.5x", () => SetSpeed(0.5)),
                        Button("1x", () => SetSpeed(1)),
                        Button("2x", () => SetSpeed(2))),
                    _scrub,
                    Row(
                        _seekEntry,
                        Button("Seek exact", SeekToEntryAsync)),

                    Section("Options"),
                    Row(
                        Button("Mute", () => { _player.IsMuted = !_player.IsMuted; return Task.CompletedTask; }),
                        Button("Loop", () => { _player.IsLooping = !_player.IsLooping; return Task.CompletedTask; }),
                        Button("Scaling", CycleScaling)),
                    Row(
                        Button("10 Hz", () => SetRate(10)),
                        Button("15 Hz", () => SetRate(15)),
                        Button("30 Hz", () => SetRate(30))),

                    Section("Readout"),
                    _state, _position, _chapter, _format, _frame, _rate, _error,
                },
            },
        };
    }

    protected override void OnDisappearing()
    {
        _player.Pause();
        base.OnDisappearing();
    }

    // Bundled assets live inside the app package, which is not a path the
    // platforms can play from. Copying once into the cache is the shortest way
    // to a real file path; a real app would hold a reference the user picked.
    private static async Task<string> EnsureCachedAsync(string assetName)
    {
        var target = Path.Combine(FileSystem.CacheDirectory, assetName);

        if (!File.Exists(target))
        {
            await using var source = await FileSystem.OpenAppPackageFileAsync(assetName);
            await using var destination = File.Create(target);
            await source.CopyToAsync(destination);
        }

        return target;
    }

    private async Task LoadBundledChaptersAsync()
    {
        var clips = new List<VideoClip>();
        foreach (var name in BundledChapters)
            clips.Add(VideoClip.FromFile(await EnsureCachedAsync(name)));

        _error.Text = "three files, one timeline (frame numbers run 0-1799)";
        _player.Source = VideoPlayerSource.FromChapters(clips);
    }

    private async Task LoadSingleBundledClipAsync()
    {
        _error.Text = "single clip";
        _player.Source = VideoPlayerSource.FromFile(await EnsureCachedAsync(BundledChapters[0]));
    }

    private Task SetSpeed(double speed)
    {
        _player.Speed = speed;
        return Task.CompletedTask;
    }

    private Task SetRate(int hz)
    {
        _player.PositionUpdateRate = hz;
        return Task.CompletedTask;
    }

    private Task CycleScaling()
    {
        _player.ScalingMode = _player.ScalingMode switch
        {
            VideoScalingMode.Fit => VideoScalingMode.Fill,
            VideoScalingMode.Fill => VideoScalingMode.Stretch,
            _ => VideoScalingMode.Fit,
        };

        _error.Text = $"scaling: {_player.ScalingMode}";
        return Task.CompletedTask;
    }

    private async Task SeekToEntryAsync()
    {
        if (!double.TryParse(_seekEntry.Text, out var seconds))
        {
            _error.Text = "enter a number of seconds";
            return;
        }

        var reached = await _player.SeekExactAsync(TimeSpan.FromSeconds(seconds));
        _error.Text = reached ? $"seek to {seconds:0.###}s reached" : "seek was overtaken";
    }

    private void OnMediaOpened(object? sender, VideoOpenedEventArgs e)
    {
        _format.Text =
            $"fps {e.FrameRate:0.###}   {e.NaturalSize.Width:0}x{e.NaturalSize.Height:0}   " +
            $"chapters {e.ChapterDurations.Count} [{string.Join(", ", e.ChapterDurations.Select(d => $"{d.TotalSeconds:0.##}s"))}]";
    }

    private void OnMediaFailed(object? sender, VideoFailedEventArgs e) =>
        _error.Text = $"failed: {e.ErrorCode} (chapter {e.ChapterIndex}) - {e.Message}";

    private void OnStateChanged(object? sender, VideoStateChangedEventArgs e) =>
        _state.Text = $"state {e.NewState}";

    private void OnPositionChanged(object? sender, VideoPositionChangedEventArgs e)
    {
        _position.Text = $"{e.Position:mm\\:ss\\.fff} / {_player.Duration:mm\\:ss\\.fff}";
        _chapter.Text = $"chapter {e.ChapterIndex} at {e.ChapterPosition:mm\\:ss\\.fff}";

        // The number the picture should be showing, if seeking is exact.
        var fps = _player.FrameRate;
        _frame.Text = fps > 0
            ? $"frame {FrameMath.FrameIndexAt(e.Position, fps)} (expected on screen)"
            : "frame rate unknown";

        if (!_scrubbing && _player.Duration > TimeSpan.Zero)
            _scrub.Value = Math.Clamp(e.Position / _player.Duration, 0, 1);

        MeasureEventRate();
    }

    // The control promises 10-30 Hz; this is the sample measuring what it
    // actually delivers rather than repeating the claim.
    private void MeasureEventRate()
    {
        var now = DateTime.UtcNow;
        _eventTimes.Enqueue(now);

        while (_eventTimes.Count > 0 && now - _eventTimes.Peek() > TimeSpan.FromSeconds(1))
            _eventTimes.Dequeue();

        _rate.Text = $"events {_eventTimes.Count}/s (asked for {_player.PositionUpdateRate} Hz)";
    }

    private static Label Readout() => new()
    {
        TextColor = Color.FromArgb("#D6D6E0"),
        FontSize = 13,
        FontFamily = "monospace",
    };

    private static Label Section(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        TextColor = Color.FromArgb("#6F7BA0"),
        FontSize = 11,
        CharacterSpacing = 1.5,
        Margin = new Thickness(0, 8, 0, 0),
    };

    private static View Row(params View[] children)
    {
        var layout = new HorizontalStackLayout { Spacing = 6 };
        foreach (var child in children)
            layout.Children.Add(child);

        return new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = layout };
    }

    private static Button Button(string text, Func<Task> action)
    {
        var button = new Microsoft.Maui.Controls.Button
        {
            Text = text,
            FontSize = 13,
            Padding = new Thickness(12, 6),
            MinimumWidthRequest = 0,
            BackgroundColor = Color.FromArgb("#243049"),
            TextColor = Colors.White,
            CornerRadius = 8,
        };

        // A sample that dies on a failed action teaches nothing. Showing the
        // exception in the readout is also how the deliberate failure buttons
        // stay honest: MediaFailed is an event, but a missing bundled asset is
        // an ordinary IO error on the app's side of the line.
        button.Clicked += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                if (Application.Current?.Windows.FirstOrDefault()?.Page is MainPage page)
                    page._error.Text = $"{ex.GetType().Name}: {ex.Message}";
            }
        };
        return button;
    }
}
