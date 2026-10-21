using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MsiRgb;

internal enum IconMode { Off, Static, Effect, Temperature, Error }

/// <summary>What the notification-area icon shows: the live LED colour plus a hint of the mode.</summary>
/// <param name="Heat">temperature mode: 0..1 between the thresholds, drawn as a gauge</param>
/// <param name="Spin">effect mode: ring rotation in degrees, advanced by the caller</param>
internal readonly record struct IconState(IconMode Mode, Rgb Color, float Heat, float Spin);

/// <summary>
/// Draws the tray icon: a dark rounded tile with a glowing orb of the current LED colour.
/// Effect = rotating segmented ring, temperature = 270° gauge, off = hollow ring.
/// Rendered 4x and downscaled for soft anti-aliased edges at 16..40 px.
/// </summary>
internal static class TrayIconArt
{
    private const int Supersample = 4;

    public static Bitmap Render(int size, IconState st)
    {
        int big = size * Supersample;
        using var hi = new Bitmap(big, big);
        using (var g = Graphics.FromImage(hi))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.ScaleTransform(big / 16f, big / 16f); // draw on a 16-unit grid whatever the real size
            Draw(g, st);
        }
        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(hi, new Rectangle(0, 0, size, size));
        }
        return bmp;
    }

    private static void Draw(Graphics g, IconState st)
    {
        // tile: soft dark glass, readable on both light and dark taskbars
        using (var tile = RoundRect(new RectangleF(0.4f, 0.4f, 15.2f, 15.2f), 4f))
        {
            using var fill = new LinearGradientBrush(new PointF(0, 0), new PointF(0, 16), Color.FromArgb(255, 48, 48, 52), Color.FromArgb(255, 22, 22, 26));
            g.FillPath(fill, tile);
            using var edge = new Pen(Color.FromArgb(70, 255, 255, 255), 0.45f);
            g.DrawPath(edge, tile);
        }

        var c = new PointF(8, 8);
        var live = st.Color;
        var (h, s, v) = live.ToHsv();
        // the hue at full brightness: rings and rims stay visible while breathing dims the orb
        var pure = v < 0.01f ? new Rgb(120, 120, 120) : Rgb.FromHsv(h, s, 1);

        if (st.Mode == IconMode.Off)
        {
            using var ring = new Pen(Color.FromArgb(150, 160, 160, 165), 1.1f);
            ring.StartCap = ring.EndCap = LineCap.Round;
            g.DrawArc(ring, Circle(c, 3.8f), 300, 300); // power symbol: ring open at the top
            using var tick = new Pen(Color.FromArgb(150, 160, 160, 165), 1.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(tick, 8, 3.4f, 8, 7.6f);
            return;
        }

        float orbR = st.Mode is IconMode.Effect or IconMode.Temperature ? 3.3f : 4.1f;

        // glow behind the orb, proportional to the real LED brightness
        if (v > 0.02f)
        {
            using var glowPath = new GraphicsPath();
            glowPath.AddEllipse(Circle(c, orbR + 3.2f));
            using var glow = new PathGradientBrush(glowPath)
            {
                CenterColor = Color.FromArgb((int)(170 * v), live.R, live.G, live.B),
                SurroundColors = new[] { Color.FromArgb(0, live.R, live.G, live.B) },
            };
            g.FillPath(glow, glowPath);
        }

        if (st.Mode == IconMode.Temperature)
        {
            const float start = 135, sweep = 270;
            var rect = Circle(c, 6.0f);
            using var track = new Pen(Color.FromArgb(55, 255, 255, 255), 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(track, rect, start, sweep);
            float f = Math.Clamp(st.Heat, 0, 1);
            using var val = new Pen(ToColor(pure), 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(val, rect, start, Math.Max(sweep * f, 6));
        }
        else if (st.Mode == IconMode.Effect)
        {
            var rect = Circle(c, 6.0f);
            using var seg = new Pen(Color.FromArgb(220, pure.R, pure.G, pure.B), 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (int i = 0; i < 3; i++) g.DrawArc(seg, rect, st.Spin + i * 120, 70);
        }

        // orb: lit from the top-left, rim in the pure hue so a dimmed orb never disappears
        using (var orbPath = new GraphicsPath())
        {
            var orb = Circle(c, orbR);
            orbPath.AddEllipse(orb);
            var core = st.Mode == IconMode.Error ? new Rgb(110, 110, 115) : live;
            using var shade = new PathGradientBrush(orbPath)
            {
                CenterPoint = new PointF(c.X - orbR * 0.35f, c.Y - orbR * 0.4f),
                CenterColor = ToColor(Rgb.Lerp(core, new Rgb(255, 255, 255), 0.35f * Math.Max(v, 0.15f))),
                SurroundColors = new[] { ToColor(core) },
            };
            g.FillPath(shade, orbPath);
            using var rim = new Pen(Color.FromArgb(st.Mode == IconMode.Error ? 120 : 200, pure.R, pure.G, pure.B), 0.55f);
            g.DrawEllipse(rim, orb);
        }

        if (st.Mode == IconMode.Error)
        {
            using var dot = new SolidBrush(Color.FromArgb(255, 252, 185, 0));
            g.FillEllipse(dot, Circle(new PointF(12.2f, 12.2f), 2.4f));
            using var bd = new Pen(Color.FromArgb(255, 26, 26, 30), 0.6f);
            g.DrawEllipse(bd, Circle(new PointF(12.2f, 12.2f), 2.4f));
        }
    }

    private static RectangleF Circle(PointF c, float r) => new(c.X - r, c.Y - r, 2 * r, 2 * r);
    private static Color ToColor(Rgb c) => Color.FromArgb(255, c.R, c.G, c.B);

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
