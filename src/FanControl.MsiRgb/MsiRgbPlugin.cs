using System;
using System.Runtime.Versioning;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FanControl.Plugins;

namespace FanControl.MsiRgb;

/// <summary>
/// Exposes MSI motherboard RGB as FanControl "controls": assign any curve (graph, mix, linear...)
/// to it and the percentage becomes a colour.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiRgbPlugin : IPlugin2
{
    private readonly IPluginLogger? _logger;
    private RenesasLed? _led;
    private RgbWorker? _worker;
    private List<Channel> _channels = new();

    public MsiRgbPlugin() { }
    public MsiRgbPlugin(IPluginLogger logger) => _logger = logger;

    public string Name => "MSI RGB";

    private void Log(string msg)
    {
        try { _logger?.Log("[MSI RGB] " + msg); } catch { }
    }

    public void Initialize()
    {
        try
        {
            var dir = Path.GetDirectoryName(typeof(MsiRgbPlugin).Assembly.Location) ?? ".";
            var cfgPath = Path.Combine(dir, "FanControl.MsiRgb.json");
            var cfg = PluginConfig.LoadOrCreate(cfgPath);
            _channels = cfg.Channels.Select(c => new Channel(c)).ToList();

            _led = RenesasLed.Open(Log);
            _worker = new RgbWorker(_led, _channels, TimeSpan.FromSeconds(cfg.ReassertSeconds), Log);
            _worker.Start();
            _worker.Poke();
            Log($"ready, {_channels.Count} channel(s), config {cfgPath}");
        }
        catch (Exception e)
        {
            Log("disabled: " + e.Message);
            Close();
        }
    }

    public void Load(IPluginSensorsContainer container)
    {
        if (_worker == null) return;
        foreach (var ch in _channels)
        {
            if (ch.Gradient != null)
            {
                container.ControlSensors.Add(new RgbControl($"MsiRgb/{ch.Config.Id}", ch.Config.Name,
                    v => { ch.SetGradient(v); _worker.Poke(); }));
            }
            else
            {
                string[] names = { "Red", "Green", "Blue" };
                for (int i = 0; i < 3; i++)
                {
                    int idx = i;
                    container.ControlSensors.Add(new RgbControl($"MsiRgb/{ch.Config.Id}/{names[i].ToLowerInvariant()}",
                        $"{ch.Config.Name} {names[i]}", v => { ch.SetComponent(idx, v); _worker.Poke(); }));
                }
            }
        }
    }

    public void Update() { }

    public void Close()
    {
        try { _worker?.Dispose(); } catch { }
        try { _led?.Dispose(); } catch { }
        _worker = null;
        _led = null;
    }
}

internal sealed class RgbControl : IPluginControlSensor
{
    private readonly Action<float?> _apply;

    public RgbControl(string id, string name, Action<float?> apply)
    {
        Id = id;
        Name = name;
        _apply = apply;
    }

    public string Id { get; }
    public string Name { get; }
    public float? Value { get; private set; }

    public void Set(float val)
    {
        Value = val;
        try { _apply(val); } catch { }
    }

    // Control released by FanControl: fall back to the channel's default colour (never "off").
    public void Reset()
    {
        Value = null;
        try { _apply(null); } catch { }
    }

    public void Update() { }
}
