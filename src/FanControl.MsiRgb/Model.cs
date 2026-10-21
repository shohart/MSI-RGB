using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FanControl.MsiRgb;

internal readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Parse(string s)
    {
        var hex = s.Trim().TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"colour '{s}' must be #RRGGBB");
        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
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
                return new Rgb(Lerp(c0.R, c1.R, t), Lerp(c0.G, c1.G, t), Lerp(c0.B, c1.B, t));
            }
        }
        return _stops[^1].Color;
    }

    private static byte Lerp(byte a, byte b, float t) => (byte)Math.Round(a + (b - a) * t);
}
