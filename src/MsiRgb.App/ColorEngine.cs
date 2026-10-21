using System;

namespace MsiRgb;

/// <summary>Pure colour maths: settings + animation phase + inputs -> colour. No hardware, unit-tested.</summary>
internal static class ColorEngine
{
    /// <summary>Breathing rates the UI offers, breaths per minute.</summary>
    public const int MinBreathRate = 4, MaxBreathRate = 60;

    public static bool IsAnimated(AppSettings s) => s.Enabled &&
        (s.Mode == LightMode.Effect || s.Mode == LightMode.Temperature && s.TempBreathing);

    /// <summary>Effect period in seconds for speed 1..10 (10 s .. 1 s).</summary>
    public static double Period(int speed) => 11 - Math.Clamp(speed, 1, 10);

    /// <summary>0..1 position of the sensor between the two thresholds; cold when the sensor is missing.</summary>
    public static float Heat(AppSettings s, float? temperature)
        => temperature is float v ? Math.Clamp((v - s.TempFrom) / (s.TempTo - s.TempFrom), 0f, 1f) : 0f;

    /// <summary>Breaths per minute right now: fixed for the effect, cold..hot rate for temperature mode.</summary>
    public static double BreathRate(AppSettings s, float? temperature) => s.Mode == LightMode.Temperature
        ? s.TempBreathRateCold + (s.TempBreathRateHot - s.TempBreathRateCold) * Heat(s, temperature)
        : s.BreathRate;

    /// <summary>
    /// How fast the animation phase advances. The engine integrates this over time instead of using t % period,
    /// so a rate that changes with temperature (or a dragged slider) never makes the light jump.
    /// </summary>
    public static double CyclesPerSecond(AppSettings s, float? temperature)
    {
        if (!IsAnimated(s)) return 0;
        if (s.Mode == LightMode.Temperature || s.Effect == EffectKind.Breathing) return BreathRate(s, temperature) / 60;
        return 1 / Period(s.EffectSpeed);
    }

    /// <summary>
    /// One breath, 0..1..0, smooth at every joint. Slow breathing is symmetric; the faster it gets, the shorter
    /// the inhale compared with the exhale (at the top rate the light rises in a quarter of the cycle).
    /// </summary>
    public static float Breath(double phase, double ratePerMinute)
    {
        double k = Math.Clamp((ratePerMinute - 12) / (MaxBreathRate - 12), 0, 1);
        double rise = 0.5 - 0.25 * k;
        phase -= Math.Floor(phase);
        double w = phase < rise
            ? 0.5 - 0.5 * Math.Cos(Math.PI * phase / rise)
            : 0.5 + 0.5 * Math.Cos(Math.PI * (phase - rise) / (1 - rise));
        return (float)w;
    }

    /// <param name="phase">animation cycles elapsed (integrated <see cref="CyclesPerSecond"/>)</param>
    /// <param name="temperature">smoothed sensor value, null if unavailable</param>
    public static Rgb Compute(AppSettings s, double phase, float? temperature)
    {
        if (!s.Enabled) return new Rgb(0, 0, 0);
        Rgb c = s.Mode switch
        {
            LightMode.Static => Rgb.Parse(s.StaticColor),
            LightMode.Effect => Effect(s, phase),
            LightMode.Temperature => Breathe(
                Rgb.Lerp(Rgb.Parse(s.TempColorFrom), Rgb.Parse(s.TempColorTo), Heat(s, temperature)),
                s.TempBreathing, phase, BreathRate(s, temperature)),
            _ => Rgb.Parse(s.StaticColor),
        };
        return c.Scale(s.Brightness / 100f);
    }

    private static Rgb Breathe(Rgb c, bool on, double phase, double rate)
        => on ? c.Scale(0.04f + 0.96f * Breath(phase, rate)) : c;

    private static Rgb Effect(AppSettings s, double phase)
    {
        phase -= Math.Floor(phase);                                       // 0..1
        float wave = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * phase)); // 0..1..0
        var c1 = Rgb.Parse(s.EffectColor1);
        return s.Effect switch
        {
            EffectKind.Breathing => Breathe(c1, true, phase, s.BreathRate),
            EffectKind.Rainbow => Rgb.FromHsv((float)(phase * 360), 1, 1),
            EffectKind.Blink => phase < 0.5 ? c1 : new Rgb(0, 0, 0),
            EffectKind.TwoColor => Rgb.Lerp(c1, Rgb.Parse(s.EffectColor2), wave),
            _ => c1,
        };
    }
}
