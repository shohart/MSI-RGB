using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MsiRgb;

internal readonly record struct Rgb(byte R, byte G, byte B)
{
    private static readonly Dictionary<string, Rgb> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = new(255, 0, 0), ["красный"] = new(255, 0, 0),
        ["orange"] = new(255, 96, 0), ["оранжевый"] = new(255, 96, 0),
        ["yellow"] = new(255, 192, 0), ["жёлтый"] = new(255, 192, 0), ["желтый"] = new(255, 192, 0),
        ["green"] = new(0, 255, 0), ["зелёный"] = new(0, 255, 0), ["зеленый"] = new(0, 255, 0),
        ["cyan"] = new(0, 255, 255), ["голубой"] = new(0, 255, 255),
        ["blue"] = new(0, 64, 255), ["синий"] = new(0, 64, 255),
        ["purple"] = new(160, 0, 255), ["фиолетовый"] = new(160, 0, 255),
        ["pink"] = new(255, 0, 128), ["розовый"] = new(255, 0, 128),
        ["white"] = new(255, 255, 255), ["белый"] = new(255, 255, 255),
        ["off"] = new(0, 0, 0), ["выкл"] = new(0, 0, 0), ["чёрный"] = new(0, 0, 0), ["черный"] = new(0, 0, 0),
    };

    /// <summary>#RRGGBB, RRGGBB or a colour name (English / Russian).</summary>
    public static Rgb Parse(string s)
    {
        if (Named.TryGetValue(s.Trim(), out var named)) return named;
        var hex = s.Trim().TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"colour '{s}' must be #RRGGBB");
        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    public static Rgb Lerp(Rgb a, Rgb b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Rgb(L(a.R, b.R, t), L(a.G, b.G, t), L(a.B, b.B, t));
    }

    /// <summary>Software brightness: the MCU's own brightness register has no visible effect.</summary>
    public Rgb Scale(float k)
    {
        k = Math.Clamp(k, 0f, 1f);
        return new Rgb((byte)Math.Round(R * k), (byte)Math.Round(G * k), (byte)Math.Round(B * k));
    }

    /// <summary>h in degrees, s and v 0..1.</summary>
    public static Rgb FromHsv(float h, float s, float v)
    {
        h = ((h % 360f) + 360f) % 360f;
        float c = v * s, x = c * (1 - Math.Abs(h / 60f % 2 - 1)), m = v - c;
        (float r, float g, float b) = (int)(h / 60f) switch
        {
            0 => (c, x, 0f), 1 => (x, c, 0f), 2 => (0f, c, x),
            3 => (0f, x, c), 4 => (x, 0f, c), _ => (c, 0f, x),
        };
        return new Rgb(B8(r + m), B8(g + m), B8(b + m));
    }

    /// <summary>Hue in degrees 0..360, saturation and value 0..1.</summary>
    public (float H, float S, float V) ToHsv()
    {
        float r = R / 255f, g = G / 255f, b = B / 255f;
        float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        float h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    private static byte L(byte a, byte b, float t) => (byte)Math.Round(a + (b - a) * t);
    private static byte B8(float f) => (byte)Math.Round(Math.Clamp(f, 0f, 1f) * 255f);
}

/// <summary>Zone bitmask registers 0x80..0x83 of the MSI Renesas MCU.</summary>
internal readonly record struct ZoneMask(byte M0, byte M1, byte M2, byte M3)
{
    // From MSI's board table for MS-7B43: JRGB1 = 0x80 bit0, "MB Mystic Light" 1..7 = 0x80 bits 1..7, 8 = 0x81 bit0
    public static readonly ZoneMask All = new(0xFF, 0xFF, 0xFF, 0xFF);
    public static readonly ZoneMask Jrgb = new(0x01, 0x00, 0x00, 0x00);
    public static readonly ZoneMask Board = new(0xFE, 0x01, 0x00, 0x00);

    public static ZoneMask Parse(string s) => s.Trim().ToLowerInvariant() switch
    {
        "all" => All,
        "jrgb" => Jrgb,
        "board" => Board,
        _ => throw new FormatException($"zones '{s}' must be all, jrgb or board"),
    };
}

/// <summary>Piecewise-linear colour scale over 0..100 %.</summary>
internal sealed class Gradient
{
    private readonly (float At, Rgb Color)[] _stops;

    public Gradient(IEnumerable<(float At, Rgb Color)> stops)
    {
        _stops = stops.OrderBy(s => s.At).ToArray();
        if (_stops.Length == 0) throw new ArgumentException("gradient needs at least one stop");
    }

    public Rgb At(float percent)
    {
        if (float.IsNaN(percent)) percent = 0;
        if (percent <= _stops[0].At) return _stops[0].Color;
        for (int i = 1; i < _stops.Length; i++)
        {
            var (x1, c1) = _stops[i];
            if (percent <= x1)
            {
                var (x0, c0) = _stops[i - 1];
                float t = x1 > x0 ? (percent - x0) / (x1 - x0) : 1f;
                return Rgb.Lerp(c0, c1, t);
            }
        }
        return _stops[^1].Color;
    }
}
