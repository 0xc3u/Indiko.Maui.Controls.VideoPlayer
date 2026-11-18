#!/usr/bin/env bash
#
# Generates synthetic footage for testing frame-accurate seeking and stepping.
#
# Every frame carries its own number burnt into the picture, which is what makes
# the behaviour checkable by eye: seek to a time, read the number on screen, and
# compare it with position x frame rate. Without that, "frame accurate" is a
# claim nobody can verify on a device.
#
# Output goes to test-videos/ (git-ignored). Real footage is never committed: it
# is large, and the licence of a borrowed sample clip is one more thing to get
# wrong.
#
# Text rendering: ffmpeg's drawtext filter is used when the local build has it.
# Homebrew's current ffmpeg bottle is built without freetype, so the fallback
# renders the frames with ImageMagick and encodes the sequence. Both produce the
# same numbering; the fallback is slower (about 25 s for the quick set).
#
# Usage:
#   scripts/make-test-videos.sh                  everything except the 4 GB file
#   scripts/make-test-videos.sh --quick          only cfr30 and the chapter set
#   scripts/make-test-videos.sh --large          also a >4 GB file (co64 atoms)
#   scripts/make-test-videos.sh --install-sample copy the chapter set into the sample app
#
set -euo pipefail

cd "$(dirname "$0")/.."

OUT="test-videos"
SAMPLE_RAW="samples/Indiko.Maui.Controls.VideoPlayer.Sample/Resources/Raw"
FONT="${FONT:-/System/Library/Fonts/Supplemental/Arial.ttf}"
JOBS="${JOBS:-8}"

QUICK=false
LARGE=false
INSTALL_SAMPLE=false
for arg in "$@"; do
    case "$arg" in
        --quick) QUICK=true ;;
        --large) LARGE=true ;;
        --install-sample) INSTALL_SAMPLE=true ;;
        *) echo "unknown option: $arg" >&2; exit 2 ;;
    esac
done

command -v ffmpeg >/dev/null || { echo "ffmpeg is required (brew install ffmpeg)" >&2; exit 1; }
command -v ffprobe >/dev/null || { echo "ffprobe is required (brew install ffmpeg)" >&2; exit 1; }
[[ -f "$FONT" ]] || { echo "font not found: $FONT (override with FONT=...)" >&2; exit 1; }

if ffmpeg -hide_banner -filters 2>/dev/null | grep -q " drawtext "; then
    RENDERER="drawtext"
else
    command -v magick >/dev/null || {
        echo "this ffmpeg has no drawtext filter and ImageMagick is not installed" >&2
        echo "install either: brew install imagemagick" >&2
        exit 1
    }
    RENDERER="imagemagick"
fi

mkdir -p "$OUT"
echo "Writing to $OUT/  (text renderer: $RENDERER)"

# A beep per second, so slow motion can be checked for pitch shifting by ear.
audio_args() {
    printf '%s\n' -f lavfi -i "sine=frequency=880:sample_rate=48000" \
        -af "volume='if(lt(mod(t,1),0.1),0.5,0)':eval=frame" -c:a aac -b:a 96k -shortest
}

drawtext_filter() {
    local start_number="$1"
    printf "drawtext=fontfile=%s:text='%%{eif\\:n+%s\\:d}':x=(w-tw)/2:y=(h/2)-140:fontsize=200:fontcolor=0x50C8FF:box=1:boxcolor=black@0.5:boxborderw=20," "$FONT" "$start_number"
    printf "drawtext=fontfile=%s:text='%%{pts\\:hms}':x=(w-tw)/2:y=(h/2)+120:fontsize=72:fontcolor=yellow:box=1:boxcolor=black@0.5:boxborderw=14" "$FONT"
}

# ImageMagick fallback: render each frame, then encode the sequence. A moving
# bar is drawn as well, so that playback is visibly running even when the camera
# is paused on a single number.
render_frames() {
    local dir="$1" count="$2" fps="$3" size="$4" start_number="$5"
    local width="${size%x*}" height="${size#*x}"

    mkdir -p "$dir"
    seq 0 $((count - 1)) | xargs -P "$JOBS" -I{} env \
        DIR="$dir" W="$width" H="$height" FPS="$fps" START="$start_number" FONT="$FONT" \
        sh -c '
            n="$1"
            number=$((n + START))
            seconds=$(awk -v n="$n" -v f="$FPS" "BEGIN { printf \"%.3f\", n / f }")
            bar=$(awk -v n="$n" -v w="$W" "BEGIN { printf \"%d\", (n * 17) % (w - 120) }")
            magick -size "${W}x${H}" "xc:rgb(18,18,26)" \
                -font "$FONT" \
                -fill "rgb(80,200,255)" -pointsize $((H / 4)) -gravity center -annotate +0-40 "$number" \
                -fill yellow -pointsize $((H / 12)) -gravity center -annotate +0+120 "${seconds}s" \
                -fill "rgb(255,120,0)" -draw "rectangle ${bar},$((H - 90)) $((bar + 120)),$((H - 40))" \
                "$DIR/f$(printf %06d "$n").png"
        ' sh {}
}

make_clip() {
    local name="$1" fps="$2" seconds="$3" size="${4:-1280x720}" start_number="${5:-0}" gop="${6:-60}" codec="${7:-h264}"

    local -a vcodec=()
    case "$codec" in
        h264) vcodec=(-c:v libx264 -preset veryfast -pix_fmt yuv420p -g "$gop" -keyint_min "$gop" -sc_threshold 0) ;;
        hevc) vcodec=(-c:v libx265 -preset veryfast -x265-params "bframes=4:keyint=$gop" -tag:v hvc1 -pix_fmt yuv420p) ;;
    esac

    echo "  $name  $size @ $fps fps, ${seconds}s"

    if [[ "$RENDERER" == "drawtext" ]]; then
        # shellcheck disable=SC2046
        ffmpeg -nostdin -y -loglevel error \
            -f lavfi -i "testsrc2=size=${size}:rate=${fps}:duration=${seconds}" \
            $(audio_args) \
            -vf "$(drawtext_filter "$start_number")" \
            "${vcodec[@]}" -movflags +faststart "$OUT/$name"
        return
    fi

    local dir count
    dir=$(mktemp -d)
    count=$(awk -v f="$fps" -v s="$seconds" 'BEGIN { printf "%d", (f * s) + 0.5 }')
    render_frames "$dir" "$count" "$fps" "$size" "$start_number"

    # shellcheck disable=SC2046
    ffmpeg -nostdin -y -loglevel error \
        -framerate "$fps" -i "$dir/f%06d.png" \
        $(audio_args) \
        "${vcodec[@]}" -r "$fps" -movflags +faststart "$OUT/$name"

    rm -rf "$dir"
}

# Exact seeking only differs from keyframe seeking between keyframes, so this
# one gets a 2 s keyframe interval: seeking to frame 45 of a 30 fps clip must
# show frame 45, not the keyframe at frame 0.
make_clip "cfr30.mp4" 30 60 1920x1080 0 60

# One take split into three files, the way an action camera writes it. The frame
# counter continues across the files (0.., 600.., 1200..), so a step over a seam
# can be checked by reading the number on screen.
echo "  chapter set GH010001 / GH020001 / GH030001 (numbering continues across files)"
make_clip "GH010001.MP4" 30 20 1280x720 0 60
make_clip "GH020001.MP4" 30 20 1280x720 600 60
make_clip "GH030001.MP4" 30 20 1280x720 1200 60
for i in 1 2 3; do
    ffmpeg -nostdin -y -loglevel error -i "$OUT/GH0${i}0001.MP4" -c copy \
        -metadata creation_time="2026-01-15T10:1${i}:00Z" "$OUT/.tmp.MP4"
    mv "$OUT/.tmp.MP4" "$OUT/GH0${i}0001.MP4"
done

if ! $QUICK; then
    make_clip "cfr60.mp4" 60 10 1280x720
    make_clip "cfr120.mp4" 120 10 1280x720
    make_clip "cfr240.mp4" 240 10 960x540
    make_clip "pal25.mp4" 25 10 1280x720
    make_clip "ntsc2997.mp4" 30000/1001 10 1280x720

    # B-frames make backward stepping the interesting case: the decoder has to
    # walk back past frames it has not displayed yet.
    make_clip "hevc_bframes.mp4" 30 15 1280x720 0 60 hevc

    # Variable frame rate: stepping by a fixed frame duration cannot be exact
    # here. That is a documented limit of the control, not a bug to chase.
    echo "  vfr.mp4  variable frame rate (15/30/60 spliced)"
    : > "$OUT/.vfr-list.txt"
    for fps in 15 30 60; do
        make_clip ".vfr-${fps}.mp4" "$fps" 5 1280x720
        echo "file '.vfr-${fps}.mp4'" >> "$OUT/.vfr-list.txt"
    done
    ffmpeg -nostdin -y -loglevel error -f concat -safe 0 -i "$OUT/.vfr-list.txt" \
        -fps_mode vfr -c copy "$OUT/vfr.mp4"
    rm -f "$OUT/.vfr-list.txt" "$OUT"/.vfr-*.mp4

    # Rotation lives in the track transform, not in the pixels: this checks that
    # portrait footage is not played on its side.
    echo "  rotated.mp4  90 degree rotation metadata"
    ffmpeg -nostdin -y -loglevel error -i "$OUT/cfr30.mp4" -t 10 -c copy \
        -metadata:s:v rotate=90 "$OUT/rotated.mp4"
fi

if $LARGE; then
    # Past 4 GB a file needs 64-bit atoms (co64); a reader that assumes 32-bit
    # offsets fails only here.
    echo "  large.mp4  >4 GB, this takes a while"
    make_clip "large.mp4" 60 600 3840x2160
fi

echo
echo "Verifying with ffprobe"
fail=0
check() {
    local file="$1" expect_fps="$2" expect_seconds="$3"
    local fps duration fps_value
    fps=$(ffprobe -v error -select_streams v:0 -show_entries stream=r_frame_rate -of csv=p=0 "$OUT/$file")
    duration=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$OUT/$file")
    fps_value=$(awk -F/ '{ if (NF==2) printf "%.3f", $1/$2; else printf "%.3f", $1 }' <<< "$fps")

    if ! awk -v a="$fps_value" -v b="$expect_fps" 'BEGIN { exit !(a-b < 0.05 && b-a < 0.05) }'; then
        echo "  FAIL $file: frame rate $fps_value, expected $expect_fps" >&2
        fail=1
        return
    fi
    if ! awk -v a="$duration" -v b="$expect_seconds" 'BEGIN { exit !(a-b < 0.5 && b-a < 0.5) }'; then
        echo "  FAIL $file: duration $duration, expected $expect_seconds" >&2
        fail=1
        return
    fi
    printf "  ok   %-18s %8s fps  %6.2fs\n" "$file" "$fps_value" "$duration"
}

check "cfr30.mp4" 30 60
check "GH010001.MP4" 30 20
check "GH020001.MP4" 30 20
check "GH030001.MP4" 30 20
if ! $QUICK; then
    check "cfr60.mp4" 60 10
    check "cfr120.mp4" 120 10
    check "cfr240.mp4" 240 10
    check "pal25.mp4" 25 10
    check "ntsc2997.mp4" 29.97 10
    check "hevc_bframes.mp4" 30 15
fi

[[ $fail -eq 0 ]] || { echo "ffprobe verification failed" >&2; exit 1; }

if $INSTALL_SAMPLE; then
    # The sample app bundles the chapter set so it has something to play on a
    # simulator with an empty photo library and no granted file access.
    mkdir -p "$SAMPLE_RAW"
    cp "$OUT/GH010001.MP4" "$OUT/GH020001.MP4" "$OUT/GH030001.MP4" "$SAMPLE_RAW/"
    echo
    echo "Copied the chapter set into $SAMPLE_RAW (git-ignored)"

    # The variable frame rate clip is what the frame index is for: without it
    # the sample can only show that frame numbers are right where they were
    # never in doubt. It only exists after a full run.
    if [[ -f "$OUT/vfr.mp4" ]]; then
        cp "$OUT/vfr.mp4" "$SAMPLE_RAW/"
        echo "Copied vfr.mp4 as well"
    else
        echo "No vfr.mp4 yet; run without --quick to build it"
    fi
fi

echo
echo "Done."
