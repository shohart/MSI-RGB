using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FanControl.MsiRgb;

/// <summary>FanControl.MsiRgb.json next to the plugin DLL. Created with defaults on first run.</summary>
internal sealed class PluginConfig
{
    public List<ChannelConfig> Channels { get; set; } = new()
    {
        new ChannelConfig(),
    };

    /// <summary>Re-send the current colour this often (seconds), in case something reset the MCU.</summary>
    public int ReassertSeconds { get; set; } = 60;

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep "синий" readable
    };

    public static PluginConfig LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var def = new PluginConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(def, Json));
            return def;
        }
        return Parse(File.ReadAllText(path));
    }

    public static PluginConfig Parse(string json)
    {
        var cfg = JsonSerializer.Deserialize<PluginConfig>(json, Json) ?? new PluginConfig();
        if (cfg.Channels.Count == 0) throw new FormatException("config: channels is empty");
        var dupe = cfg.Channels.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
        if (dupe != null) throw new FormatException($"config: duplicate channel id '{dupe.Key}'");
        foreach (var c in cfg.Channels) c.Validate();
        if (cfg.ReassertSeconds < 5) cfg.ReassertSeconds = 5;
        return cfg;
    }
}

internal sealed class ChannelConfig
{
    /// <summary>Stable id: FanControl stores your curve assignment by it. Don't rename once used.</summary>
    public string Id { get; set; } = "all";
    public string Name { get; set; } = "MSI RGB";
    /// <summary>all | jrgb | board</summary>
    public string Zones { get; set; } = "all";
    /// <summary>gradient: one control 0..100 % mapped From -> To (or through Stops). rgb: three controls (R, G, B in %).</summary>
    public string Mode { get; set; } = "gradient";
    /// <summary>Colour at 0 % (curve's lower temperature). Name ("синий") or #RRGGBB.</summary>
    public string? From { get; set; } = "синий";
    /// <summary>Colour at 100 % (curve's upper temperature).</summary>
    public string? To { get; set; } = "красный";
    /// <summary>Advanced: multi-colour scale; used instead of From/To when set.</summary>
    public List<StopConfig>? Stops { get; set; }
    /// <summary>Colour shown before FanControl sets anything and after the control is released (default: From).</summary>
    public string? Default { get; set; }

    internal ZoneMask Mask => ZoneMask.Parse(Zones);
    internal bool IsRgb => Mode.Equals("rgb", StringComparison.OrdinalIgnoreCase);
    internal Rgb DefaultColor => Rgb.Parse(Default ?? From ?? "#000000");

    internal Gradient Gradient => Stops is { Count: > 0 }
        ? new(Stops.Select(s => (s.At, Rgb.Parse(s.Color))))
        : new(new[] { (0f, Rgb.Parse(From ?? "#000000")), (100f, Rgb.Parse(To ?? "#000000")) });

    /// <summary>What FanControl shows on the card, e.g. "MSI RGB: синий → красный".</summary>
    internal string DisplayName => IsRgb || Stops is { Count: > 0 } ? Name : $"{Name}: {From} → {To}";

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new FormatException("config: channel id is empty");
        _ = Mask;
        _ = DefaultColor;
        if (!IsRgb && !Mode.Equals("gradient", StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"config: channel '{Id}' mode must be gradient or rgb");
        if (!IsRgb) _ = Gradient;
    }
}

internal sealed class StopConfig
{
    public float At { get; set; }
    public string Color { get; set; } = "#000000";
}
