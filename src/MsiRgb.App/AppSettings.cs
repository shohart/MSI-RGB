using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MsiRgb;

internal enum LightMode { Static, Effect, Temperature, FanControl }

internal enum EffectKind { Breathing, Rainbow, Blink, TwoColor }

/// <summary>Everything the user picks in the window. Immutable snapshots are handed to the engine.</summary>
internal sealed record AppSettings
{
    public LightMode Mode { get; init; } = LightMode.Temperature;
    /// <summary>Software brightness 5..100 %.</summary>
    public int Brightness { get; init; } = 100;

    public string StaticColor { get; init; } = "#0040FF";

    public EffectKind Effect { get; init; } = EffectKind.Breathing;
    public string EffectColor1 { get; init; } = "#0040FF";
    public string EffectColor2 { get; init; } = "#FF0000";
    /// <summary>1 (slow) .. 10 (fast).</summary>
    public int EffectSpeed { get; init; } = 5;

    public string? SensorId { get; init; } = "/intelcpu/0/temperature/10"; // CPU Package
    public float TempFrom { get; init; } = 40;
    public float TempTo { get; init; } = 85;
    public string TempColorFrom { get; init; } = "#0040FF";
    public string TempColorTo { get; init; } = "#FF0000";

    public string FanColorFrom { get; init; } = "#0040FF";
    public string FanColorTo { get; init; } = "#FF0000";

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MsiRgb", "settings.json");

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings()).Sanitized();
        }
        catch { /* broken file: start from defaults, it is overwritten on the next change */ }
        return new AppSettings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Clamp ranges and replace unparsable colours so the engine never throws on user input.</summary>
    public AppSettings Sanitized()
    {
        static string C(string? s, string fallback)
        {
            try { return s != null ? Rgb.Parse(s).ToString() : fallback; }
            catch (FormatException) { return fallback; }
        }
        float from = TempFrom, to = TempTo;
        if (to <= from) to = from + 1;
        return this with
        {
            Brightness = Math.Clamp(Brightness, 5, 100),
            EffectSpeed = Math.Clamp(EffectSpeed, 1, 10),
            TempFrom = from,
            TempTo = to,
            StaticColor = C(StaticColor, "#0040FF"),
            EffectColor1 = C(EffectColor1, "#0040FF"),
            EffectColor2 = C(EffectColor2, "#FF0000"),
            TempColorFrom = C(TempColorFrom, "#0040FF"),
            TempColorTo = C(TempColorTo, "#FF0000"),
            FanColorFrom = C(FanColorFrom, "#0040FF"),
            FanColorTo = C(FanColorTo, "#FF0000"),
        };
    }
}
