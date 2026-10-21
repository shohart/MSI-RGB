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
    /// <summary>gradient: one control 0..100 % mapped through Stops. rgb: three controls (R, G, B in %).</summary>
    public string Mode { get; set; } = "gradient";
    /// <summary>Colour shown before FanControl sets anything and after the control is released.</summary>
    public string Default { get; set; } = "#0040FF";
    public List<StopConfig> Stops { get; set; } = new()
    {
        new() { At = 0, Color = "#0040FF" },
        new() { At = 40, Color = "#00FF40" },
        new() { At = 70, Color = "#FFC000" },
        new() { At = 100, Color = "#FF0000" },
    };

    internal ZoneMask Mask => ZoneMask.Parse(Zones);
    internal bool IsRgb => Mode.Equals("rgb", StringComparison.OrdinalIgnoreCase);
    internal Rgb DefaultColor => Rgb.Parse(Default);
    internal Gradient Gradient => new(Stops.Select(s => (s.At, Rgb.Parse(s.Color))));

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
