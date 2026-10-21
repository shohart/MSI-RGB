using System;
using System.IO;
using FanControl.Plugins;
using LinkFile = global::MsiRgb.LinkFile;

namespace FanControl.MsiRgb;

/// <summary>
/// Optional bridge for the MSI RGB tray app: exposes a FanControl control whose curve
/// output (0..100 %) is forwarded to the app through a small file. It never touches hardware.
/// </summary>
public sealed class MsiRgbPlugin : IPlugin2
{
    private readonly IPluginLogger? _logger;
    private LinkControl? _control;

    public MsiRgbPlugin() { }
    public MsiRgbPlugin(IPluginLogger logger) => _logger = logger;

    public string Name => "MSI RGB";

    public void Initialize() => _control ??= new LinkControl(LinkFile.DefaultPath, m => { try { _logger?.Log("[MSI RGB] " + m); } catch { } });

    public void Load(IPluginSensorsContainer container)
    {
        if (_control != null) container.ControlSensors.Add(_control);
    }

    public void Update() { }
    public void Close() { }
}

internal sealed class LinkControl : IPluginControlSensor
{
    private readonly string _path;
    private readonly Action<string> _log;
    private float? _written;
    private DateTime _writtenAt;

    public LinkControl(string path, Action<string> log)
    {
        _path = path;
        _log = log;
    }

    public string Id => "MsiRgb/link";
    public string Name => "MSI RGB (цвет из приложения)";
    public float? Value { get; private set; }

    public void Set(float val)
    {
        Value = val;
        // the app treats a file older than a few seconds as "FanControl gone", so refresh it anyway
        if (_written is float w && Math.Abs(w - val) < 0.5f && DateTime.UtcNow - _writtenAt < TimeSpan.FromSeconds(3)) return;
        try
        {
            LinkFile.Write(_path, val);
            _written = val;
            _writtenAt = DateTime.UtcNow;
        }
        catch (Exception e) { _log("link write failed: " + e.Message); }
    }

    public void Reset()
    {
        Value = null;
        _written = null;
        try { File.Delete(_path); } catch { }
    }

    public void Update() { }
}
