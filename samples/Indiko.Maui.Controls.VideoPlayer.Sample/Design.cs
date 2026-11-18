namespace Indiko.Maui.Controls.VideoPlayer.Sample;

/// <summary>
/// The design tokens, in one place.
/// </summary>
/// <remarks>
/// The surround is a calibrated neutral grey rather than black, which is why
/// every editing suite sits in mid-grey: a black or tinted interface around a
/// picture biases how you judge the picture. Two signal colours, each with one
/// job - red means the transport is live, cyan means a measured value. Cyan is
/// not arbitrary either: it is the colour the test footage burns into its own
/// frames, so the number this app computes and the number on screen can be
/// compared at a glance.
/// </remarks>
public static class Ink
{
    public static readonly Color Panel = Color.FromArgb("#26282C");
    public static readonly Color Well = Color.FromArgb("#1A1C1F");
    public static readonly Color Raised = Color.FromArgb("#32353B");
    public static readonly Color RaisedEdge = Color.FromArgb("#3D4149");
    public static readonly Color Hairline = Color.FromArgb("#3A3E45");
    public static readonly Color Text = Color.FromArgb("#ECEEF1");
    public static readonly Color Muted = Color.FromArgb("#969CA6");
    public static readonly Color Faint = Color.FromArgb("#6B7079");
    public static readonly Color Tally = Color.FromArgb("#E9454F");
    public static readonly Color Measure = Color.FromArgb("#5BC8F5");
}

public static class Type
{
    public const string Sans = "PlexSans";
    public const string SansSemi = "PlexSansSemi";
    public const string Mono = "PlexMono";
    public const string MonoSemi = "PlexMonoSemi";
}

/// <summary>
/// The scrubber: track, chapter seams, playhead. Drawn rather than themed
/// because the seams are the point - they show that several files are being
/// played as one timeline, which no stock slider can express.
/// </summary>
public sealed class TimelineDrawable : IDrawable
{
    public double Progress { get; set; }

    public IReadOnlyList<double> ChapterBoundaries { get; set; } = [];

    public bool IsLive { get; set; }

    /// <summary>The in and out points as fractions of the timeline, when set.</summary>
    public (double Start, double End)? Range { get; set; }

    public void Draw(ICanvas canvas, RectF rect)
    {
        var midY = rect.Center.Y;
        const float trackHeight = 3f;

        canvas.FillColor = Ink.Well;
        canvas.FillRoundedRectangle(0, midY - trackHeight / 2, rect.Width, trackHeight, 1.5f);

        var played = (float)Math.Clamp(Progress, 0, 1) * rect.Width;

        canvas.FillColor = Ink.Muted;
        canvas.FillRoundedRectangle(0, midY - trackHeight / 2, played, trackHeight, 1.5f);

        // The range is drawn on the track itself in the measuring colour: it is
        // a statement about the media, like the seams, rather than about where
        // the playhead happens to be.
        if (Range is { } range)
        {
            var start = (float)Math.Clamp(range.Start, 0, 1) * rect.Width;
            var end = (float)Math.Clamp(range.End, 0, 1) * rect.Width;

            canvas.FillColor = Ink.Measure;
            canvas.FillRoundedRectangle(start, midY - trackHeight / 2, Math.Max(2f, end - start), trackHeight, 1.5f);

            canvas.StrokeColor = Ink.Measure;
            canvas.StrokeSize = 1.5f;
            canvas.DrawLine(start, midY + 3, start, midY + 9);
            canvas.DrawLine(end, midY + 3, end, midY + 9);
        }

        // Seams sit above the track, not on it, so they stay readable once the
        // played portion passes them.
        canvas.StrokeColor = Ink.Faint;
        canvas.StrokeSize = 1f;

        foreach (var boundary in ChapterBoundaries)
        {
            var x = (float)Math.Clamp(boundary, 0, 1) * rect.Width;
            canvas.DrawLine(x, midY - 9, x, midY - 3);
        }

        canvas.FillColor = IsLive ? Ink.Tally : Ink.Text;
        canvas.FillRoundedRectangle(Math.Clamp(played - 1.5f, 0, rect.Width - 3), midY - 10, 3, 20, 1.5f);
    }
}

/// <summary>The play and pause glyphs, drawn so they do not depend on a font having them.</summary>
public sealed class TransportGlyph : IDrawable
{
    public bool ShowPause { get; set; }

    public void Draw(ICanvas canvas, RectF rect)
    {
        canvas.FillColor = Ink.Text;

        var cx = rect.Center.X;
        var cy = rect.Center.Y;

        if (ShowPause)
        {
            canvas.FillRoundedRectangle(cx - 7, cy - 9, 5, 18, 1.5f);
            canvas.FillRoundedRectangle(cx + 2, cy - 9, 5, 18, 1.5f);
            return;
        }

        var path = new PathF();
        path.MoveTo(cx - 6, cy - 10);
        path.LineTo(cx + 10, cy);
        path.LineTo(cx - 6, cy + 10);
        path.Close();
        canvas.FillPath(path);
    }
}
