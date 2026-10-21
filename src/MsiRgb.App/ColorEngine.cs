using System;

namespace MsiRgb;

/// <summary>Pure colour maths: settings + time + inputs -> colour. No hardware, unit-tested.</summary>
internal static class ColorEngine
{
    public static bool IsAnimated(AppSettings s) => s.Mode == LightMode.Effect;

    /// <summary>Effect period in seconds for speed 1..10 (10 s .. 1 s).</summary>
    public static double Period(int speed) => 11 - Math.Clamp(speed, 1, 10);

    /// <param name="t">seconds since start (effects)</param>
    /// <param name="temperature">smoothed sensor value, null if unavailable</param>
    /// <param name="fanPercent">FanControl curve output, null if no live link</param>
    public static Rgb Compute(AppSettings s, double t, float? temperature, float? fanPercent)
    {
        Rgb c = s.Mode switch
        {
            LightMode.Static => Rgb.Parse(s.StaticColor),
            LightMode.Effect => Effect(s, t),
            LightMode.Temperature => Rgb.Lerp(Rgb.Parse(s.TempColorFrom), Rgb.Parse(s.TempColorTo),
                temperature is float v ? (v - s.TempFrom) / (s.TempTo - s.TempFrom) : 0f),
            LightMode.FanControl => Rgb.Lerp(Rgb.Parse(s.FanColorFrom), Rgb.Parse(s.FanColorTo),
                fanPercent is float p ? p / 100f : 0f),
            _ => Rgb.Parse(s.StaticColor),
        };
        return c.Scale(s.Brightness / 100f);
    }

    private static Rgb Effect(AppSettings s, double t)
    {
        double period = Period(s.EffectSpeed);
        double phase = t % period / period;                     // 0..1
        float wave = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * phase)); // 0..1..0
        var c1 = Rgb.Parse(s.EffectColor1);
        return s.Effect switch
        {
            EffectKind.Breathing => c1.Scale(0.04f + 0.96f * wave),
            EffectKind.Rainbow => Rgb.FromHsv((float)(phase * 360), 1, 1),
            EffectKind.Blink => phase < 0.5 ? c1 : new Rgb(0, 0, 0),
            EffectKind.TwoColor => Rgb.Lerp(c1, Rgb.Parse(s.EffectColor2), wave),
            _ => c1,
        };
    }
}
