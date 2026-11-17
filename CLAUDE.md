# CLAUDE.md

Guidance for agents working in this repository.

## What this is

`Indiko.Maui.Controls.VideoPlayer` — a native video control for .NET MAUI
(iOS and Android) built for analysing footage: frame-accurate seeking,
frame-by-frame stepping, slow motion, 10–30 Hz position events, camera chapters
played as one timeline, and sources played by reference without copying.

It is a general-purpose package. It carries no reference to, and no concept
from, any app that happens to consume it.

## A merge to main is a release

`.github/workflows/release.yml` runs on every push to `main`. A `feat` or `fix`
commit in that push produces a tag, a GitHub release and a push to nuget.org,
with no further confirmation. There is no staging step between merging and
publishing, and a version on nuget.org can never be deleted or reused.

So: **do not merge or push to `main` without the user explicitly asking for a
release.** Work on a branch, open a PR, and leave the merge to them.

`chore`, `docs`, `ci`, `refactor`, `test` and `style` commits release nothing,
and a push touching only `**.md` does not even start the workflow.

## Rules

1. **Build locally for both platforms**, including iOS. CI is not a substitute
   for a build you did not run. Fix what breaks or say that it is broken.
2. **Tests first** for the logic under `Timeline/`: write the test, watch it
   fail, implement, watch it pass.
3. **Conventional commits**, small. `feat!:` or a `BREAKING CHANGE:` footer
   only when a break is meant — it is a major version.
4. **Report what actually ran.** Commands, results, test counts, which
   simulator or device. Nothing is "verified" because it compiled.
5. **No secrets** in the repository, in logs or in a PR. Trusted publishing
   needs no API key; `NUGET_USER` is a profile name and the user sets it.
6. **No force push, no rewritten history.**
7. **Nothing from a consuming app** gets copied in — no app names, no domain
   vocabulary, no dependency on another repository.

## Build and test

```sh
dotnet test tests/Indiko.Maui.Controls.VideoPlayer.Tests
dotnet build src/Indiko.Maui.Controls.VideoPlayer.sln -c Debug
dotnet build src/Indiko.Maui.Controls.VideoPlayer.sln -c Release

dotnet build samples/Indiko.Maui.Controls.VideoPlayer.Sample/Indiko.Maui.Controls.VideoPlayer.Sample.csproj -f net10.0-android -t:Run
dotnet pack src/Indiko.Maui.Controls.VideoPlayer/Indiko.Maui.Controls.VideoPlayer.csproj -c Release -p:PackageVersion=0.0.0-local -o artifacts

.github/scripts/packable-projects.sh   # must print only the package project
.github/scripts/next-version.sh        # prints the next version, changes nothing
```

### iOS when the workload wants an Xcode you do not have

The iOS SDK states a **minimum** Xcode. The failure is an SDK newer than the
installed Xcode; a newer Xcode than the SDK asks for is fine (the 26.0 SDK
builds against Xcode 26.6). When a plain `-f net10.0-ios` fails with "requires
Xcode …", use the opt-in TFM pin rather than `ValidateXcodeVersion=false`,
which only moves the failure into the trimmer:

```sh
export DEVELOPER_DIR=/Applications/Xcode-26.0.0.app/Contents/Developer
dotnet build src/Indiko.Maui.Controls.VideoPlayer/Indiko.Maui.Controls.VideoPlayer.csproj \
  -p:IndikoIosSdk26=true -f net10.0-ios26.0
```

Local builds only. **Never pack with it**: the TFM decides the published asset
folder, and CI pins the workload manifest instead so releases carry
`lib/net10.0-ios26.0`, matching `Indiko.Maui.Controls.MapBox` 1.15.0.

### Running the sample

```sh
scripts/make-test-videos.sh --quick --install-sample
```

The footage has its frame number burnt into each frame, which is what makes
frame accuracy checkable by eye: seek to a time, read the number, compare it
with position × frame rate. Generated files are git-ignored; **no video file
is ever committed.**

## Layout

| | |
|---|---|
| `src/…/Timeline/` | pure logic, no MAUI or platform types — the test project compiles these files in, so keep it that way |
| `src/…/Platforms/{iOS,Android}/` | the handlers; everything touching AVFoundation or Media3 lives here |
| `src/…/Models/` | sources, enums, event args |
| `tests/` | xUnit, `net10.0`, runs without the MAUI workload |
| `samples/` | demo app; also builds against `Indiko.Maui.Controls.MapBox` as a proof the AndroidX graphs resolve together |

## Things that will bite

- **The platforms disagree about which frame a seek shows**, by exactly one.
  AVFoundation shows the frame containing the position; Media3 shows the first
  frame at or after it. `FrameSeekSemantics` names both and each handler picks
  its own. Media3 also reports the position the seek asked for rather than the
  frame it displays, so the Android handler tracks the displayed frame itself.
- **Media3 is pinned to 1.8.0.** From 1.9 it needs AndroidX Lifecycle 2.11,
  outside what MAUI 10.0.110 brings, which puts 18–21 NU1608 warnings into
  every consuming app. Check for them before raising it.
- **`Microsoft.Maui.Controls` is pinned to 10.0.110**, not `$(MauiVersion)`:
  otherwise packing would raise the floor for consumers to whatever the build
  machine had installed.
- **Error messages must never contain a path, URI, asset id or file name.**
  They reach crash reporters. Log the code.
- The package requests **no Android permission and no iOS authorisation**. It
  checks what the app already holds. Keep it that way — a media permission
  would force every consuming app into Google Play's photo and video
  declaration.
