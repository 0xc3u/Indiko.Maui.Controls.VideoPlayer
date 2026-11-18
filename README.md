# Indiko.Maui.Controls.VideoPlayer

A native video control for .NET MAUI built for *analysing* footage rather than
watching it: frame-accurate seeking, frame-by-frame stepping, slow motion,
magnification of the frame on screen, and position events fast enough to drive
a map or a timeline alongside the picture.

It draws no playback controls of its own. The app builds those, because an app
that steps through frames needs a transport bar that a system player will not
give it.

```
<PackageReference Include="Indiko.Maui.Controls.VideoPlayer" Version="1.0.0" />
```

```csharp
builder.UseIndikoVideoPlayer();
```

Targets `net10.0-ios` and `net10.0-android`, minimum iOS 15 and Android 30.
AVFoundation on iOS, Media3 ExoPlayer on Android.

## Why not MediaElement

`CommunityToolkit.Maui.MediaElement` plays video well. It cannot do the things
this control exists for: seeking on iOS is not frame-accurate, there is no
frame stepping, and there is no way to name a photo-library asset or a
security-scoped bookmark as a source. Those are the whole job here.

## Quick start

```xml
<ContentPage xmlns:video="clr-namespace:Indiko.Maui.Controls.VideoPlayer;assembly=Indiko.Maui.Controls.VideoPlayer">
    <video:VideoPlayerView x:Name="Player"
                           HeightRequest="220"
                           Speed="1.0"
                           PositionUpdateRate="15" />
</ContentPage>
```

```csharp
Player.MediaOpened += (_, e) =>
    Console.WriteLine($"{e.Duration}, {e.FrameRate} fps, {e.ChapterDurations.Count} chapters");

Player.MediaFailed += (_, e) => ShowError(e.ErrorCode);

Player.Source = VideoPlayerSource.FromFile(path);
Player.Play();

await Player.SeekExactAsync(TimeSpan.FromSeconds(45));   // frame accurate
await Player.StepFrameAsync(1);                          // one frame forward
```

## Sources: by reference, never copied

Training footage runs to gigabytes. A control that copies a recording into the
app container to play it doubles the user's storage and leaves them wondering
where the space went. Every source here is a reference that is read in place.

| Kind | Reference | Platform |
|---|---|---|
| `FilePath` | absolute path | both |
| `ContentUri` | `content://…` | Android |
| `PhotoAsset` | `PHAsset.LocalIdentifier` | iOS |
| `SecurityScopedBookmark` | bookmark data, Base64 | iOS |

```csharp
VideoPlayerSource.FromFile("/path/to/clip.mp4");
VideoPlayerSource.FromClip(VideoClip.FromContentUri(uri.ToString()));
VideoPlayerSource.FromChapters([clipA, clipB, clipC]);
```

A kind that does not exist on the running platform raises `MediaFailed` with
`UnsupportedSource`. It does not throw.

### What the app has to do

The control presents no pickers and requests no permissions. When to show a
picker, and when it is reasonable to ask for photo-library access, are
decisions only the app can make — a permission prompt raised while a view is
appearing is the kind users deny.

**iOS, files.** Pick with `UIDocumentPickerViewController` and `asCopy: false`,
then store a bookmark. A plain path to a file outside the container stops
working after a restart.

```csharp
var bookmark = VideoReferences.CreateBookmark(url);   // Base64, store this
```

Bookmarks go stale when the file is moved or renamed, or after an OS update.
The control renews one when it can and reports the new value through
`BookmarkRefreshed`; store it, or the renewal repeats on every open until it
eventually fails. No `Info.plist` key is needed for the document picker.

**iOS, photo library.** Pick with `PHPickerViewController` and keep the
`AssetIdentifier`. Before the first playback the app must hold read access; the
control only checks it, and reports `PermissionDenied` otherwise. The app needs
`NSPhotoLibraryUsageDescription` in its `Info.plist`. Under limited access an
asset the user never shared is indistinguishable from a deleted one, and both
report `NotFound` — the remedy is the same, so offer the picker again.

Assets are requested in their **original** version. A slow-motion recording
delivered as an edited composition arrives already slowed down, which would
make the frame rate and every frame number wrong.

**iOS, iCloud.** If only a cloud copy exists, playback reports
`NetworkRequired` unless `AllowNetworkAccess` is set. Ask the user first — the
download can be gigabytes — then set it and follow `DownloadProgress`.

**Android.** Pick with the photo picker or `ACTION_OPEN_DOCUMENT`, then take a
persisted grant:

```csharp
VideoReferences.TakePersistablePermission(context, uri);
VideoReferences.HasPersistedPermission(context, uri);
VideoReferences.ReleasePersistablePermission(context, uri);   // when unlinking
```

Without it the grant dies with the activity: the clip plays now and reports
`PermissionLost` after the next app start. The system caps how many grants an
app may hold, so release the ones you no longer need.

Measured on API 36: grants from **both** the photo picker and the storage
access framework can be persisted, and both still play after a cold restart.
Older versions and third-party providers may refuse, so check the return value
rather than assuming it.

One difference worth knowing if you group chapters by file name: a URI from the
storage access framework reports the real name (`GH010001.MP4`), while the
photo picker reports a MediaStore name (`23.mp4`). The original is not
available through that route.

**The package contributes no Android permission and no service.** It needs
neither `READ_MEDIA_VIDEO` nor `READ_EXTERNAL_STORAGE`; the picker and the
storage access framework hand back a readable URI without them. Check your
merged manifest — an app that declares `READ_MEDIA_VIDEO` has to answer Google
Play's photo and video permissions declaration.

## Chapters

An action camera splits one recording into several files — a GoPro writes
`GH010123.MP4`, `GH020123.MP4`, `GH030123.MP4` for a single take — and the
viewer thinks of that as one video. Hand the clips over in order and they
become one timeline:

```csharp
Player.Source = VideoPlayerSource.FromChapters(
    [VideoClip.FromFile(a), VideoClip.FromFile(b), VideoClip.FromFile(c)]);
```

`Duration` is their sum, `Position` is global, and seeking and stepping cross
the seams without the caller ever naming a file. `CurrentChapterIndex` and the
`ChapterPosition` on `PositionChanged` say where you are, when that matters.

iOS builds an `AVMutableComposition`, Android a `ConcatenatingMediaSource2`:
one item and one window, so neither platform needs position arithmetic at
playback time.

## Frame accuracy

Seeking is exact, not to the nearest keyframe. On footage with a two-second
keyframe interval that is the difference between the frame you asked for and
one up to sixty frames away.

Verified on both platforms with generated footage that has its frame number
burnt into the picture (`scripts/make-test-videos.sh`), three 20 s chapters
at 30 fps numbered 0–1799 continuously:

| | iOS simulator | Android emulator |
|---|---|---|
| `SeekExactAsync(45.000 s)` | frame **1350**, chapter 2 at 5.000 s | frame **1350**, chapter 2 at 5.000 s |
| `StepFrameAsync(±1)` | 1350 → 1351 → 1350 | 1350 → 1351 → 1352 → 1353 |
| End of timeline | frame 1799 of 1800 | — |

### The platforms disagree, and the control hides it

They differ by exactly one frame, which is worth knowing if you ever compute
seek targets yourself:

- **AVFoundation** shows the frame whose span *contains* the seek position.
- **Media3** shows the first frame whose timestamp is *at or after* it.

Aiming at the middle of the wanted frame is right for the first and lands one
frame too far on the second, every time. `FrameSeekSemantics` names the two
rules and each handler asks for its own. Media3 then reports the position the
seek asked for rather than the frame it displays, so the Android handler tracks
the frame on screen and reports that frame's start — otherwise a frame counter
driven from `Position` disagrees with what the viewer can read.

## Zoom

`ZoomFactor` magnifies the picture, from 1 — the whole picture, as
`ScalingMode` presents it — up to `MaximumZoom`, four by default and at most
sixteen. There is deliberately nothing below 1: zooming out past the fitted
picture would only letterbox a letterbox.

```csharp
player.ZoomTo(4);               // about the middle, keeping what is in it there
player.ZoomFactor = 2;          // or set it directly; values outside are clamped
player.ResetZoom();             // back to the whole picture, centred
```

What is magnified is the decoded frame, not the drawn one, so up to the
resolution of the footage the digits in a burnt-in frame number get *sharper*
rather than blockier — which is the point of having it in a control meant for
reading detail out of a frame.

`ZoomOffset` moves the magnified picture, in device-independent units, and is
clamped to the picture's own edges: a magnified frame can never show black
beside it, and at factor 1 the offset is always `Point.Zero` because there is
nowhere to go. The handler writes the clamped value back, so the property is
what is actually on screen rather than what was asked for. `ZoomChanged`
reports both, whoever changed them.

### Gestures

`IsPinchToZoomEnabled` is **off by default**, like every other interaction: the
control draws no controls and claims no gestures unless asked, so an app's own
recognisers on the player keep working. Turn it on and a pinch magnifies about
the point between the fingers — whatever is under them stays under them — and a
one-finger drag pans, but only once there is something to pan. Unmagnified, a
drag across the picture still scrolls the page the player sits on.

### What this costs on Android

Media3's `PlayerView` draws into a `SurfaceView` by default, which is
composited outside the view hierarchy: a transform on it moves the hole in the
window rather than reliably the picture in it, and clipping is the window's
business rather than the parent view's. This control therefore inflates its
player view from a layout that asks for a `TextureView`, which draws like any
other view, so magnifying is an ordinary transform and the player view clips
it. A `TextureView` costs a little more to draw. That is the price of a zoom
that behaves the same on every device, and it is paid whether or not an app
uses the zoom.

iOS needs no such trade: the player layer backs an inner view that carries the
transform, inside a clipping container.

## API

### Properties

| Property | Type | Default | |
|---|---|---|---|
| `Source` | `VideoPlayerSource?` | `null` | assigning tears down and reloads |
| `Speed` | `double` | `1.0` | clamped to 0.1–2.0 |
| `Volume` | `double` | `1.0` | clamped to 0–1 |
| `IsMuted` | `bool` | `false` | |
| `IsLooping` | `bool` | `false` | |
| `ScalingMode` | `VideoScalingMode` | `Fit` | `Fit`, `Fill`, `Stretch` |
| `ZoomFactor` | `double` | `1.0` | clamped to 1–`MaximumZoom`, two-way |
| `ZoomOffset` | `Point` | `Zero` | clamped to the picture's edges, two-way |
| `MaximumZoom` | `double` | `4.0` | clamped to 1–16 |
| `IsPinchToZoomEnabled` | `bool` | `false` | pinch to magnify, drag to pan |
| `PositionUpdateRate` | `int` | `15` | Hz, clamped to 10–30 |
| `AutoPlay` | `bool` | `false` | play once open |
| `PauseWhenInBackground` | `bool` | `true` | never resumes by itself |
| `AllowNetworkAccess` | `bool` | `false` | iOS, iCloud downloads |

Read-only: `Position`, `Duration`, `State`, `IsPlaying`, `FrameRate`,
`NaturalSize`, `CurrentChapterIndex`, `ChapterDurations`.

`State` is `Idle`, `Opening`, `Ready`, `Playing`, `Paused`, `Buffering`,
`Ended` or `Failed`.

### Methods

| | |
|---|---|
| `Play()` `Pause()` `Stop()` | `Stop` also returns to the start |
| `Task<bool> SeekExactAsync(TimeSpan, CancellationToken = default)` | `false` when overtaken or cancelled |
| `Task<bool> StepFrameAsync(int frames)` | pauses first, crosses chapters |
| `BeginScrub()` `ScrubTo(TimeSpan)` `EndScrub()` | fast while dragging, exact on release |
| `ZoomTo(double factor)` | magnifies about the middle, carrying the pan with it |
| `ResetZoom()` | back to the whole picture, centred |

All of them must be called on the main thread; both native players require it,
and the control says so with an `InvalidOperationException` rather than letting
it surface later as a frozen picture.

`SeekExactAsync` returning `false` is not an error. Dragging a scrub bar starts
a seek per movement and neither platform promises a callback for one a newer
seek replaced, so an overtaken seek resolves as `false`: the position you asked
for is not where the player ended up, and a playhead drawn from it would be
wrong.

### Events

| | |
|---|---|
| `PositionChanged` | at `PositionUpdateRate`, plus once after every seek or step |
| `MediaOpened` | duration, frame rate, size, chapter durations |
| `MediaFailed` | error code, chapter, message |
| `PlaybackEnded` `StateChanged` | |
| `ZoomChanged` | factor and clamped offset, from a pinch or from the app |
| `BookmarkRefreshed` | iOS, store the renewed bookmark |
| `DownloadProgress` | iOS, iCloud download, 0–1 |

Error codes: `NotFound`, `PermissionDenied`, `PermissionLost`,
`BookmarkInvalid`, `NetworkRequired`, `UnsupportedSource`,
`UnsupportedFormat`, `Unknown`.

**Messages never contain a path, a URI, an asset identifier or a file name.**
They reach crash reporters and logs, where any of those would be a privacy leak
about the user's own library. Log the code.

## Position events

`PositionChanged` fires at `PositionUpdateRate` while playing, and once more
after every completed seek or frame step so a playhead cannot lag the picture.
Both platforms are observed at about 30 Hz underneath and thinned to the rate
you ask for.

Measured on an Android emulator (Pixel 7, API 36) with software decoding: **19
events/s with 30 Hz requested**. Within the supported range, below the target,
and emulator overhead is the likely reason. Measure on hardware before
depending on the upper end. The sample app shows the live rate.

## Limits

- **Variable frame rate.** Stepping assumes a fixed frame duration, so on VFR
  material it is approximate. The frame rate is read from the format; when the
  container does not report one the control falls back to 30 fps.
- **HEVC backwards.** Stepping back through B-frames makes the decoder walk
  past frames it has not displayed. It works, but it is the slowest path.
- **Teardown is complete and immediate.** Leaving the page releases the player,
  stops the timers, cancels pending photo requests and closes security-scoped
  access. There is no background playback, no media session and no foreground
  service — by design, not by omission.
- **`AVAudioSession` is left alone.** Whether this app's audio ducks, mixes or
  interrupts is the app's decision, not a video control's.

## Building from source

```sh
dotnet test tests/Indiko.Maui.Controls.VideoPlayer.Tests
dotnet build src/Indiko.Maui.Controls.VideoPlayer.sln
```

The timeline logic is compiled into the test assembly rather than referenced,
so the tests run on a plain `net10.0` SDK with no MAUI workload.

If the installed .NET for iOS wants an Xcode you do not have — it pins an exact
major.minor and a newer Xcode does not satisfy it — there is an opt-in TFM pin:

```sh
export DEVELOPER_DIR=/Applications/Xcode-26.0.0.app/Contents/Developer
dotnet build src/Indiko.Maui.Controls.VideoPlayer/Indiko.Maui.Controls.VideoPlayer.csproj \
  -p:IndikoIosSdk26=true -f net10.0-ios26.0
```

It is for local builds only. Releases are packed without it — CI pins the iOS
workload manifest instead — and the published package carries
`lib/net10.0-ios26.0`, the same platform version as
`Indiko.Maui.Controls.MapBox` 1.15.0, so an app can consume both.
`-p:ValidateXcodeVersion=false` is not an alternative: it only moves the
failure into the trimmer.

The Xcode requirement is a minimum, not an exact match: the 26.0 iOS SDK
builds fine against Xcode 26.6. The failure is the other direction, an SDK
newer than the Xcode present.

`scripts/make-test-videos.sh` generates the test footage with ffmpeg. Where
ffmpeg was built without freetype and has no `drawtext` filter, it renders the
numbered frames with ImageMagick instead. Nothing it writes is committed.

## Sample

`samples/Indiko.Maui.Controls.VideoPlayer.Sample` exercises every part of the
API and shows the numbers needed to check the claims: position, duration,
chapter, frame rate, size, the frame number expected on screen, the magnification
and pan, and the measured event rate. Generate the footage first:

```sh
scripts/make-test-videos.sh --quick --install-sample
```

## Licence

MIT.
