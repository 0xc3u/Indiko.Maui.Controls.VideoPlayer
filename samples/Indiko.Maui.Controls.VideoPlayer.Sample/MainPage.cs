using Indiko.Maui.Controls.VideoPlayer.Models;
using Indiko.Maui.Controls.VideoPlayer.Sample.Services;
using Indiko.Maui.Controls.VideoPlayer.Timeline;

namespace Indiko.Maui.Controls.VideoPlayer.Sample;

/// <summary>
/// Exercises the control, and shows enough numbers to check its claims.
/// </summary>
/// <remarks>
/// Laid out as an instrument rather than a demo: the timecode is the largest
/// thing on the page because the frame field is what the package is for. The
/// test footage burns its frame number into the picture, so the computed
/// number sits directly above it and the two can be compared without
/// instrumentation of any kind.
/// </remarks>
public class MainPage : ContentPage
{
    private static readonly string[] BundledChapters =
        ["GH010001.MP4", "GH020001.MP4", "GH030001.MP4"];

    private static readonly double[] Speeds = [0.25, 0.5, 1, 2];
    private static readonly int[] Rates = [10, 15, 30];
    private static readonly double[] Zooms = [1, 2, 4, 8];

    private readonly VideoPlayerView _player;
    private readonly IVideoPicker _picker = VideoPickerFactory.Create();

    private readonly Label _timecode = Numeric(40, Type.MonoSemi, text: "00:00:00:00");
    private readonly Label _frame = Numeric(27, Type.MonoSemi, Ink.Measure, "0");
    private readonly Label _duration = Numeric(13, Type.Mono, Ink.Muted, "of 00:00:00:00");
    private readonly Label _chapter = Numeric(13, Type.Mono, Ink.Muted, "no source");
    private readonly BoxView _tally = new()
    {
        WidthRequest = 9, HeightRequest = 9, CornerRadius = 5,
        Color = Ink.Hairline, VerticalOptions = LayoutOptions.Center,
        Margin = new Thickness(0, 0, 2, 0),
    };

    private readonly TimelineDrawable _timeline = new();
    private readonly GraphicsView _timelineView;
    private readonly Slider _scrub;
    private readonly TransportGlyph _glyph = new();
    private readonly GraphicsView _glyphView;

    private readonly Label _source = Detail("No source loaded");
    private readonly Label _message = Detail("Pick a clip, or load the bundled chapters.");
    private readonly Label _format = Numeric(13, Type.Mono);
    private readonly Label _events = Numeric(13, Type.Mono);
    private readonly Label _state = Numeric(13, Type.Mono);

    private readonly Button[] _speedButtons = new Button[Speeds.Length];
    private readonly Button[] _rateButtons = new Button[Rates.Length];
    private readonly Button[] _zoomButtons = new Button[Zooms.Length];
    private readonly Entry _seekEntry;

    private readonly Label _zoomReadout = Numeric(13, Type.Mono, text: "1.00×");
    private readonly Label _derived = Numeric(13, Type.Mono);
    private readonly Label _picked = Numeric(13, Type.Mono, text: "tap the picture");
    private readonly Label _grab = Numeric(13, Type.Mono);
    private readonly Image _still = new()
    {
        HeightRequest = 92,
        Aspect = Aspect.AspectFit,
        HorizontalOptions = LayoutOptions.Start,
        IsVisible = false,
    };

    private Button? _pinchButton;
    private readonly Entry _frameEntry;

    private readonly Label _empty = new()
    {
        Text = "Load the bundled chapters below.\nThey carry their frame number in the picture.",
        FontFamily = Type.Sans,
        FontSize = 13,
        TextColor = Ink.Faint,
        HorizontalTextAlignment = TextAlignment.Center,
        VerticalTextAlignment = TextAlignment.Center,
        LineHeight = 1.4,
        Margin = 20,
    };

    private readonly Queue<DateTime> _eventTimes = new();
    private int _chapterCount;
    private bool _scrubbing;

    public MainPage()
    {
        Title = "Indiko Video Player";
        BackgroundColor = Ink.Panel;

        _player = new VideoPlayerView
        {
            ScalingMode = VideoScalingMode.Fit,
            PositionUpdateRate = 15,

            // Eight rather than the default four: the burnt-in frame number is
            // small on a phone, and this is the page for finding out where the
            // magnification stops being worth anything.
            MaximumZoom = 8,

            // The control claims no gestures unless asked. Here they are asked
            // for, which also puts the cooperation with the page's own
            // scrolling under test.
            IsPinchToZoomEnabled = true,
        };

        _player.MediaOpened += OnMediaOpened;
        _player.MediaFailed += OnMediaFailed;
        _player.PositionChanged += OnPositionChanged;
        _player.StateChanged += OnStateChanged;
        _player.PlaybackEnded += (_, _) => Say("Reached the end of the timeline.");
        _player.BookmarkRefreshed += (_, e) =>
        {
            PickedClipStore.Replace(e.ChapterIndex, e.NewBookmark);
            Say($"Renewed and stored the bookmark for chapter {e.ChapterIndex + 1}.");
        };
        _player.DownloadProgress += (_, e) => Say($"Downloading from iCloud, {e.Progress:P0}.");
        _player.ZoomChanged += (_, _) => SyncZoom();

        _timelineView = new GraphicsView
        {
            Drawable = _timeline,
            HeightRequest = 24,
            InputTransparent = true,
        };

        // A stock slider cannot show chapter seams, so the track is drawn and
        // the slider rides invisibly on top purely to collect the drag.
        _scrub = new Slider
        {
            Maximum = 1,
            ThumbColor = Colors.Transparent,
            MinimumTrackColor = Colors.Transparent,
            MaximumTrackColor = Colors.Transparent,
            HeightRequest = 24,
        };
        _scrub.DragStarted += (_, _) => { _scrubbing = true; _player.BeginScrub(); };
        _scrub.DragCompleted += (_, _) => { _scrubbing = false; _player.EndScrub(); };
        _scrub.ValueChanged += (_, e) =>
        {
            if (!_scrubbing || _player.Duration <= TimeSpan.Zero)
                return;

            _timeline.Progress = e.NewValue;
            _timelineView.Invalidate();
            _player.ScrubTo(_player.Duration * e.NewValue);
        };

        _glyphView = new GraphicsView { Drawable = _glyph, HeightRequest = 26, WidthRequest = 26 };

        _seekEntry = new Entry
        {
            Placeholder = "seconds",
            Keyboard = Keyboard.Numeric,
            TextColor = Ink.Text,
            PlaceholderColor = Ink.Faint,
            BackgroundColor = Ink.Well,
            FontFamily = Type.Mono,
            FontSize = 14,
            HeightRequest = 44,
            WidthRequest = 110,
        };

        _frameEntry = new Entry
        {
            Placeholder = "frame",
            Keyboard = Keyboard.Numeric,
            TextColor = Ink.Text,
            PlaceholderColor = Ink.Faint,
            BackgroundColor = Ink.Well,
            FontFamily = Type.Mono,
            FontSize = 14,
            HeightRequest = 44,
            WidthRequest = 110,
        };

        // Tapping the picture reads the pixel under the finger. It is also the
        // proof that the package's own gestures leave an app's alone: this
        // fires while pinch-to-zoom is on.
        var pick = new TapGestureRecognizer();
        pick.Tapped += (_, e) => ShowPickedPixel(e.GetPosition(_player));
        _player.GestureRecognizers.Add(pick);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Children =
                {
                    VideoWell(),
                    Readout(),
                    Scrubber(),
                    Transport(),
                    Hairline(),
                    Section("Playback", SpeedAndRate()),
                    Hairline(),
                    Section("Zoom", ZoomControls()),
                    Hairline(),
                    Section("Frame and picture", FrameControls()),
                    Hairline(),
                    Section("Source", SourceControls()),
                    Hairline(),
                    Section("Failure modes", FailureControls()),
                    Hairline(),
                    Section("Diagnostics", Diagnostics()),
                },
            },
        };

        SyncSpeed();
        SyncRate();
        SyncZoom();
        SyncPinch();
    }

    protected override void OnDisappearing()
    {
        _player.Pause();
        base.OnDisappearing();
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    private View VideoWell()
    {
        var surface = new Grid { Children = { _player, _empty } };

        return new Border
        {
            BackgroundColor = Ink.Well,
            Stroke = Ink.Hairline,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
            Margin = new Thickness(16, 16, 16, 12),
            HeightRequest = 212,
            Content = surface,
        };
    }

    private View Readout()
    {
        var left = new VerticalStackLayout { Spacing = 2 };
        left.Children.Add(_timecode);

        var meta = new HorizontalStackLayout { Spacing = 14 };
        meta.Children.Add(_duration);
        meta.Children.Add(_chapter);
        left.Children.Add(meta);

        // The frame number is the other half of the claim, so it is sized to
        // match the timecode rather than tucked into a corner. Its caption sits
        // underneath, where it cannot be mistaken for part of the number.
        _frame.HorizontalTextAlignment = TextAlignment.End;

        var right = new VerticalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Children =
            {
                _frame,
                new Label
                {
                    Text = "frame on screen",
                    FontFamily = Type.Sans,
                    FontSize = 11,
                    TextColor = Ink.Faint,
                    HorizontalTextAlignment = TextAlignment.End,
                },
            },
        };

        var grid = new Grid
        {
            Padding = new Thickness(16, 0, 16, 10),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 8,
        };

        grid.Add(_tally, 0);
        grid.Add(left, 1);
        grid.Add(right, 2);
        return grid;
    }

    private View Scrubber() => new Grid
    {
        Padding = new Thickness(16, 0, 16, 4),
        Children = { _timelineView, _scrub },
    };

    private View Transport()
    {
        var row = new Grid
        {
            Padding = new Thickness(16, 6, 16, 16),
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(1.3, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
        };

        row.Add(Step("−10", -10), 0);
        row.Add(Step("−1", -1), 1);
        row.Add(PlayPause(), 2);
        row.Add(Step("+1", 1), 3);
        row.Add(Step("+10", 10), 4);
        return row;
    }

    private View PlayPause()
    {
        var border = new Border
        {
            BackgroundColor = Ink.Raised,
            Stroke = Ink.RaisedEdge,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            HeightRequest = 52,
            Content = _glyphView,
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (_player.IsPlaying)
                _player.Pause();
            else
                _player.Play();
        };
        border.GestureRecognizers.Add(tap);

        SemanticProperties.SetDescription(border, "Play or pause");
        return border;
    }

    private View SpeedAndRate()
    {
        var stack = new VerticalStackLayout { Spacing = 14 };

        var speeds = Group("Speed");
        for (var i = 0; i < Speeds.Length; i++)
        {
            var speed = Speeds[i];
            _speedButtons[i] = Segment(FormatSpeed(speed), () =>
            {
                _player.Speed = speed;
                SyncSpeed();
                return Task.CompletedTask;
            });
            speeds.Add(_speedButtons[i]);
        }

        stack.Children.Add(Labelled("Speed", speeds));

        var rates = Group("Rate");
        for (var i = 0; i < Rates.Length; i++)
        {
            var rate = Rates[i];
            _rateButtons[i] = Segment($"{rate} Hz", () =>
            {
                _player.PositionUpdateRate = rate;
                SyncRate();
                return Task.CompletedTask;
            });
            rates.Add(_rateButtons[i]);
        }

        stack.Children.Add(Labelled("Position events", rates));

        var jump = new HorizontalStackLayout { Spacing = 8 };
        jump.Add(_seekEntry);
        jump.Add(Action("Seek", SeekToEntryAsync));
        stack.Children.Add(Labelled("Jump to a time", jump));

        var toggles = Group("Options");
        toggles.Add(Action("Mute", () => { _player.IsMuted = !_player.IsMuted; Say(_player.IsMuted ? "Muted." : "Sound on."); return Task.CompletedTask; }));
        toggles.Add(Action("Loop", () => { _player.IsLooping = !_player.IsLooping; Say(_player.IsLooping ? "Looping." : "Looping off."); return Task.CompletedTask; }));
        toggles.Add(Action("Scaling", CycleScaling));
        toggles.Add(Action("Back to start", () => { _player.Stop(); return Task.CompletedTask; }));
        stack.Children.Add(toggles);

        return stack;
    }

    private View ZoomControls()
    {
        var stack = new VerticalStackLayout { Spacing = 14 };

        var factors = Group("Zoom");
        for (var i = 0; i < Zooms.Length; i++)
        {
            var factor = Zooms[i];
            _zoomButtons[i] = Segment(FormatZoom(factor), () =>
            {
                _player.ZoomTo(factor);
                Say(factor > 1
                    ? "Magnified about the middle. Drag the picture to move it."
                    : "Back to the whole picture.");
                return Task.CompletedTask;
            });

            // The speed row carries the same labels, so the two are told apart
            // here rather than by which heading they happen to sit under.
            SemanticProperties.SetDescription(_zoomButtons[i], $"Magnify {FormatZoom(factor)}");
            factors.Add(_zoomButtons[i]);
        }

        stack.Children.Add(Labelled("Magnification", factors));

        var steps = Group("Steps");
        steps.Add(Action("Zoom out", () => { Nudge(1 / 1.5); return Task.CompletedTask; }));
        steps.Add(Action("Zoom in", () => { Nudge(1.5); return Task.CompletedTask; }));
        steps.Add(Action("Reset", () => { _player.ResetZoom(); Say("Reset to the whole picture."); return Task.CompletedTask; }));
        stack.Children.Add(Labelled("A step at a time, about the middle", steps));

        var gestures = Group("Gestures");
        _pinchButton = Action("Pinch to zoom", () =>
        {
            _player.IsPinchToZoomEnabled = !_player.IsPinchToZoomEnabled;
            SyncPinch();
            Say(_player.IsPinchToZoomEnabled
                ? "Pinch the picture to magnify, drag it to pan. Scrolling the page still works."
                : "Gestures off. The picture ignores touches again.");
            return Task.CompletedTask;
        });
        gestures.Add(_pinchButton);
        stack.Children.Add(Labelled("Off by default in the control; on here", gestures));

        stack.Children.Add(Detail(
            "The frame number is burnt into the picture. Magnify it and the digits get "
            + "sharper rather than blockier, up to the resolution of the footage, because "
            + "what is enlarged is the decoded frame and not the drawn one."));

        return stack;
    }

    private View FrameControls()
    {
        var stack = new VerticalStackLayout { Spacing = 14 };

        var jump = new HorizontalStackLayout { Spacing = 8 };
        jump.Add(_frameEntry);
        jump.Add(Action("Go", SeekToFrameAsync));
        stack.Children.Add(Labelled("Jump to a frame number", jump));

        var grab = new HorizontalStackLayout { Spacing = 10 };
        grab.Add(Action("Capture frame", CaptureAsync));
        grab.Add(_still);
        stack.Children.Add(Labelled("The frame as recorded, not as drawn", grab));
        stack.Children.Add(_grab);

        stack.Children.Add(Detail(
            "The captured PNG is the full resolution of the footage and ignores the "
            + "magnification, so the thumbnail stays the whole frame however far the "
            + "picture is zoomed in. Tap the picture above to read the pixel under "
            + "your finger; it follows the zoom and the pan."));

        return stack;
    }

    private View SourceControls()
    {
        var stack = new VerticalStackLayout { Spacing = 14 };

        var bundled = Group("Bundled");
        bundled.Add(Action("Three chapters", LoadBundledChaptersAsync));
        bundled.Add(Action("Single clip", LoadSingleBundledClipAsync));
        stack.Children.Add(Labelled("Ships with the sample", bundled));

        var picked = Group("Picked");
        picked.Add(Action(_picker.Labels.Photos, () => PickAsync(fromPhotos: true)));
        picked.Add(Action(_picker.Labels.Files, () => PickAsync(fromPhotos: false)));
        stack.Children.Add(Labelled("Played by reference, never copied", picked));

        var saved = Group("Saved");
        saved.Add(Action("Replay saved", ReplaySavedAsync));
        saved.Add(Action("Forget saved", () =>
        {
            PickedClipStore.Clear();
            Say("Cleared the saved references.");
            return Task.CompletedTask;
        }));
#if IOS
        saved.Add(Action("Put a clip in Files", CopyToDocumentsAsync));
#endif
        stack.Children.Add(Labelled("Survives an app restart", saved));

        stack.Children.Add(_source);
        return stack;
    }

    private View FailureControls()
    {
        var group = Group("Failures");
        group.Add(Action("Missing file", () =>
        {
            _player.Source = VideoPlayerSource.FromFile("/does/not/exist.mp4");
            return Task.CompletedTask;
        }));
        group.Add(Action("Wrong kind for this platform", () =>
        {
            _player.Source = VideoPlayerSource.FromClip(VideoClip.FromPhotoAsset("no-such-asset"));
            return Task.CompletedTask;
        }));
        group.Add(Action("Unload", () =>
        {
            _player.Source = null;
            _empty.IsVisible = true;
            _source.Text = "No source loaded";
            Say("Released the player.");
            return Task.CompletedTask;
        }));

        return new VerticalStackLayout
        {
            Spacing = 10,
            Children = { Labelled("Each one reports, none of them crash", group), _message },
        };
    }

    private View Diagnostics() => new VerticalStackLayout
    {
        Spacing = 8,
        Children =
        {
            Reading("State", _state),
            Reading("Media", _format),
            Reading("Event rate", _events),
            Reading("Zoom", _zoomReadout),
            Reading("From the clock", _derived),
            Reading("Picked pixel", _picked),
        },
    };

    // ── Pieces ───────────────────────────────────────────────────────────────

    private static View Section(string heading, View body) => new VerticalStackLayout
    {
        Padding = new Thickness(16, 14, 16, 16),
        Spacing = 12,
        Children =
        {
            new Label
            {
                Text = heading,
                FontFamily = Type.SansSemi,
                FontSize = 15,
                TextColor = Ink.Text,
            },
            body,
        },
    };

    private static View Labelled(string caption, View body) => new VerticalStackLayout
    {
        Spacing = 6,
        Children =
        {
            new Label { Text = caption, FontFamily = Type.Sans, FontSize = 12, TextColor = Ink.Faint },
            body,
        },
    };

    private static View Reading(string caption, Label value)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(110)),
                new ColumnDefinition(GridLength.Star),
            },
        };

        grid.Add(new Label
        {
            Text = caption,
            FontFamily = Type.Sans,
            FontSize = 13,
            TextColor = Ink.Faint,
        }, 0);

        grid.Add(value, 1);
        return grid;
    }

    private static FlexLayout Group(string name) => new()
    {
        Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
        JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
        AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Start,
        Margin = new Thickness(0, 0, -8, -8),
    };

    private static View Hairline() => new BoxView
    {
        HeightRequest = 1,
        Color = Ink.Hairline,
        Margin = new Thickness(16, 0),
    };

    private static Label Numeric(double size, string family, Color? color = null, string text = "—") => new()
    {
        FontFamily = family,
        FontSize = size,
        TextColor = color ?? Ink.Text,
        Text = text,
    };

    private static Label Detail(string text) => new()
    {
        Text = text,
        FontFamily = Type.Sans,
        FontSize = 13,
        TextColor = Ink.Muted,
        LineHeight = 1.35,
    };

    private Button Action(string text, Func<Task> handler)
    {
        var button = new Button
        {
            Text = text,
            FontFamily = Type.Sans,
            FontSize = 14,
            TextColor = Ink.Text,
            BackgroundColor = Ink.Raised,
            BorderColor = Ink.RaisedEdge,
            BorderWidth = 1,
            CornerRadius = 8,
            Padding = new Thickness(14, 0),
            MinimumWidthRequest = 0,
            HeightRequest = 44,
            Margin = new Thickness(0, 0, 8, 8),
        };

        button.Clicked += async (_, _) =>
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                // A sample that dies on a failed action teaches nothing.
                Say($"{ex.GetType().Name}: {ex.Message}");
            }
        };

        return button;
    }

    private Button Segment(string text, Func<Task> handler)
    {
        var button = Action(text, handler);
        button.FontFamily = Type.Mono;
        button.Padding = new Thickness(10, 0);
        button.MinimumWidthRequest = 62;
        return button;
    }

    private void SyncSpeed()
    {
        for (var i = 0; i < _speedButtons.Length; i++)
            Mark(_speedButtons[i], Math.Abs(_player.Speed - Speeds[i]) < 0.001);
    }

    private void SyncRate()
    {
        for (var i = 0; i < _rateButtons.Length; i++)
            Mark(_rateButtons[i], _player.PositionUpdateRate == Rates[i]);
    }

    // The readout is the point of the section: a pinch writes back to the same
    // properties the buttons set, and this is where that can be watched.
    private void SyncZoom()
    {
        var offset = _player.ZoomOffset;

        _zoomReadout.Text = _player.ZoomFactor > 1.0001
            ? $"{_player.ZoomFactor:0.00}×   offset {offset.X:0;-0;0}, {offset.Y:0;-0;0}"
            : $"{_player.ZoomFactor:0.00}×   whole picture";

        for (var i = 0; i < _zoomButtons.Length; i++)
            Mark(_zoomButtons[i], Math.Abs(_player.ZoomFactor - Zooms[i]) < 0.001);
    }

    private void SyncPinch()
    {
        if (_pinchButton is not null)
            Mark(_pinchButton, _player.IsPinchToZoomEnabled);
    }

    // Red marks what is live. It is the only thing on the page allowed to use
    // it, which is what keeps it meaning something.
    private static void Mark(Button button, bool active)
    {
        button.BackgroundColor = active ? Ink.Tally : Ink.Raised;
        button.BorderColor = active ? Ink.Tally : Ink.RaisedEdge;
        button.TextColor = active ? Colors.White : Ink.Text;
    }

    // ── Behaviour ────────────────────────────────────────────────────────────

    private static string FormatSpeed(double speed) =>
        speed == Math.Floor(speed) ? $"{speed:0}×" : $"{speed:0.##}×";

    private static string FormatZoom(double factor) =>
        factor == Math.Floor(factor) ? $"{factor:0}×" : $"{factor:0.##}×";

    private void Nudge(double by)
    {
        _player.ZoomTo(_player.ZoomFactor * by);
        Say($"Magnification {_player.ZoomFactor:0.##}× of at most {_player.MaximumZoom:0.##}×.");
    }

    // Counted from the frame number rather than from the clock, so the
    // timecode and the frame readout cannot disagree: a frame boundary is not
    // representable in ticks at every rate, and reading the seconds off a
    // position that sits a few microseconds below one shows the second before.
    // Non-drop-frame, so at 29.97 it drifts from the wall clock by design.
    private static string Timecode(long frame, double frameRate)
    {
        var fps = Math.Max(1, (long)Math.Round(FrameMath.EffectiveFrameRate(frameRate)));
        var seconds = frame / fps;

        return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}:{frame % fps:00}";
    }

    private void Say(string message) => _message.Text = message;

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

        _source.Text = "Three files, one timeline. Frame numbers run 0 to 1799 without a break.";
        Say("Seek to 45 s: the picture should read 1350.");
        _player.Source = VideoPlayerSource.FromChapters(clips);
    }

    private async Task LoadSingleBundledClipAsync()
    {
        _source.Text = $"One file, {BundledChapters[0]}.";
        Say("Loaded a single clip.");
        _player.Source = VideoPlayerSource.FromFile(await EnsureCachedAsync(BundledChapters[0]));
    }

#if IOS
    private async Task CopyToDocumentsAsync()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var target = Path.Combine(documents, BundledChapters[0]);

        if (!File.Exists(target))
        {
            await using var source = await FileSystem.OpenAppPackageFileAsync(BundledChapters[0]);
            await using var destination = File.Create(target);
            await source.CopyToAsync(destination);
        }

        Say($"{BundledChapters[0]} is now in Files, under On My iPhone.");
    }
#endif

    private async Task PickAsync(bool fromPhotos)
    {
        var picked = fromPhotos
            ? await _picker.PickFromPhotosAsync()
            : await _picker.PickFromFilesAsync();

        if (picked.Count == 0)
        {
            Say("Nothing picked.");
            return;
        }

        PickedClipStore.Save(picked);
        Apply(picked, "Picked");
        Say("Stored. Restart the app and press Replay saved to prove the reference survives.");
    }

    private Task ReplaySavedAsync()
    {
        var saved = PickedClipStore.Load();

        if (saved.Count == 0)
        {
            Say("Nothing saved yet. Pick something first.");
            return Task.CompletedTask;
        }

        Apply(saved, "Replayed from storage");
        Say("Loaded from storage, with nothing picked this time.");
        return Task.CompletedTask;
    }

    private void Apply(IReadOnlyList<PickedClip> clips, string what)
    {
        _source.Text =
            $"{what}: {string.Join(", ", clips.Select(c => $"{c.Label} ({Describe(c.Kind)})"))}";

        _player.Source = VideoPlayerSource.FromChapters(clips.Select(c => c.ToClip()));
    }

    private static string Describe(VideoClipKind kind) => kind switch
    {
        VideoClipKind.FilePath => "file path",
        VideoClipKind.ContentUri => "content URI",
        VideoClipKind.PhotoAsset => "photo library",
        VideoClipKind.SecurityScopedBookmark => "bookmark",
        _ => kind.ToString(),
    };

    private Task CycleScaling()
    {
        _player.ScalingMode = _player.ScalingMode switch
        {
            VideoScalingMode.Fit => VideoScalingMode.Fill,
            VideoScalingMode.Fill => VideoScalingMode.Stretch,
            _ => VideoScalingMode.Fit,
        };

        Say($"Scaling: {_player.ScalingMode}.");
        return Task.CompletedTask;
    }

    private async Task Step(int frames)
    {
        var reached = await _player.StepFrameAsync(frames);

        if (!reached)
            Say("The step was overtaken or there is nothing loaded.");
    }

    private View Step(string text, int frames)
    {
        var button = Action(text, () => Step(frames));
        button.FontFamily = Type.Mono;
        button.Padding = new Thickness(0);
        button.HeightRequest = 52;
        button.Margin = new Thickness(0);
        SemanticProperties.SetDescription(button, $"Step {Math.Abs(frames)} frames {(frames < 0 ? "back" : "forward")}");
        return button;
    }

    private async Task SeekToFrameAsync()
    {
        if (!long.TryParse(_frameEntry.Text, out var frame))
        {
            Say("Type a frame number to jump to.");
            return;
        }

        var reached = await _player.SeekToFrameAsync(frame);
        Say(reached
            ? $"Asked for frame {frame}. The picture should read the same."
            : "A newer seek overtook this one.");
    }

    private async Task CaptureAsync()
    {
        var png = await _player.CaptureFrameAsync();

        if (png is null)
        {
            _still.IsVisible = false;
            _grab.Text = "nothing captured";
            Say("No frame to capture — load a source first.");
            return;
        }

        _still.Source = ImageSource.FromStream(() => new MemoryStream(png));
        _still.IsVisible = true;
        _grab.Text = $"PNG, {png.Length / 1024.0:0.#} kB, frame {_player.CurrentFrame}";
        Say("Captured the frame at full resolution.");
    }

    private void ShowPickedPixel(Point? viewPoint)
    {
        if (viewPoint is not { } point)
        {
            _picked.Text = "no position";
            return;
        }

        if (_player.ToPicturePoint(point) is not { } pixel)
        {
            _picked.Text = "not measured yet";
            return;
        }

        var size = _player.NaturalSize;
        var inside = pixel.X >= 0 && pixel.Y >= 0 && pixel.X <= size.Width && pixel.Y <= size.Height;

        _picked.Text = $"{pixel.X:0}, {pixel.Y:0}{(inside ? string.Empty : "   outside")}";
    }

    private async Task SeekToEntryAsync()
    {
        if (!double.TryParse(_seekEntry.Text, out var seconds))
        {
            Say("Type a number of seconds to jump to.");
            return;
        }

        var reached = await _player.SeekExactAsync(TimeSpan.FromSeconds(seconds));
        Say(reached
            ? $"Landed on {seconds:0.###} s. Compare the frame number with the picture."
            : "A newer seek overtook this one.");
    }

    // ── Reporting ────────────────────────────────────────────────────────────

    private void OnMediaOpened(object? sender, VideoOpenedEventArgs e)
    {
        _empty.IsVisible = false;
        _chapterCount = e.ChapterDurations.Count;

        var size = e.NaturalSize.Width > 0 ? $"{e.NaturalSize.Width:0}×{e.NaturalSize.Height:0}" : "size unknown";
        var rate = e.FrameRate > 0 ? $"{e.FrameRate:0.###} fps" : "frame rate unknown";

        _format.Text = $"{rate}   {size}   {e.ChapterDurations.Count} chapter{(e.ChapterDurations.Count == 1 ? "" : "s")}";
        _duration.Text = $"of {Timecode(FrameMath.FrameIndexAt(e.Duration, e.FrameRate), e.FrameRate)}";

        if (e.ChapterDurations.Count > 1 && e.Duration > TimeSpan.Zero)
        {
            var boundaries = new List<double>();
            var cursor = TimeSpan.Zero;

            foreach (var chapter in e.ChapterDurations.Take(e.ChapterDurations.Count - 1))
            {
                cursor += chapter;
                boundaries.Add(cursor / e.Duration);
            }

            _timeline.ChapterBoundaries = boundaries;
        }
        else
        {
            _timeline.ChapterBoundaries = [];
        }

        _timelineView.Invalidate();
    }

    private void OnMediaFailed(object? sender, VideoFailedEventArgs e)
    {
        var where = e.ChapterIndex >= 0 ? $" in chapter {e.ChapterIndex + 1}" : string.Empty;
        Say($"{Explain(e.ErrorCode)}{where}");
    }

    // The code is what an app would log; this is what a person can act on.
    private static string Explain(VideoErrorCode code) => code switch
    {
        VideoErrorCode.NotFound => "That file is gone. Pick it again.",
        VideoErrorCode.PermissionDenied => "No access to the photo library. Grant it in Settings, or pick from files instead.",
        VideoErrorCode.PermissionLost => "The permission for this clip was revoked. Pick it again.",
        VideoErrorCode.BookmarkInvalid => "This bookmark no longer resolves. Pick the file again.",
        VideoErrorCode.NetworkRequired => "Only an iCloud copy exists. Allow network access to download it.",
        VideoErrorCode.UnsupportedSource => "That kind of reference does not exist on this platform.",
        VideoErrorCode.UnsupportedFormat => "This file cannot be decoded here.",
        _ => "Playback failed.",
    };

    private void OnStateChanged(object? sender, VideoStateChangedEventArgs e)
    {
        _state.Text = e.NewState.ToString();

        var live = e.NewState == VideoPlayerState.Playing;
        _tally.Color = live ? Ink.Tally : Ink.Hairline;
        _timeline.IsLive = live;
        _glyph.ShowPause = live;
        _glyphView.Invalidate();
        _timelineView.Invalidate();
    }

    private void OnPositionChanged(object? sender, VideoPositionChangedEventArgs e)
    {
        var fps = _player.FrameRate;

        _timecode.Text = Timecode(e.Frame, fps);

        // Reported by the handler rather than derived here. The row below shows
        // what the old calculation would have said, which is how the half-frame
        // the two platforms disagree about becomes visible instead of silent.
        _frame.Text = e.Frame.ToString();
        _derived.Text = fps > 0
            ? $"frame {FrameMath.FrameIndexAt(e.Position, fps)}   of {e.Frame} reported"
            : "—";
        _chapter.Text = _chapterCount > 1
            ? $"chapter {e.ChapterIndex + 1} of {_chapterCount}"
            : "one chapter";

        if (!_scrubbing && _player.Duration > TimeSpan.Zero)
        {
            var progress = Math.Clamp(e.Position / _player.Duration, 0, 1);
            _scrub.Value = progress;
            _timeline.Progress = progress;
            _timelineView.Invalidate();
        }

        MeasureEventRate();
    }

    private void MeasureEventRate()
    {
        var now = DateTime.UtcNow;
        _eventTimes.Enqueue(now);

        while (_eventTimes.Count > 0 && now - _eventTimes.Peek() > TimeSpan.FromSeconds(1))
            _eventTimes.Dequeue();

        _events.Text = $"{_eventTimes.Count}/s of {_player.PositionUpdateRate} asked for";
    }
}
