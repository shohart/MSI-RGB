using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace MsiRgb;

internal sealed record SensorInfo(string Id, string Name, float? Value);

internal sealed record EngineStatus(Rgb Color, float? Temperature, string? Error, IReadOnlyList<SensorInfo> Sensors);

/// <summary>
/// The only writer of the RGB MCU. One background thread owns PawnIO, LibreHardwareMonitor and timing;
/// the UI swaps in new settings and polls Status.
/// </summary>
internal sealed class LightEngine : IDisposable
{
    private static readonly TimeSpan SensorInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Reassert = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SuspendGap = TimeSpan.FromSeconds(10);
    private const int StaticTickMs = 200;
    private const int FrameMs = 40;

    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private volatile AppSettings _settings;
    private volatile EngineStatus _status = new(new Rgb(0, 0, 0), null, "запуск…", Array.Empty<SensorInfo>());
    private volatile bool _stop;

    public LightEngine(AppSettings settings)
    {
        _settings = settings;
        _thread = new Thread(Run) { IsBackground = true, Name = "MsiRgb engine" };
    }

    public EngineStatus Status => _status;
    public void Start() => _thread.Start();

    public void Apply(AppSettings s)
    {
        _settings = s;
        _wake.Set();
    }

    private void Run()
    {
        RenesasLed? led = null;
        Computer? computer = null;
        var clock = Stopwatch.StartNew();
        DateTime lastLoop = DateTime.UtcNow, lastSensors = DateTime.MinValue, lastWrite = DateTime.MinValue;
        Rgb? written = null;
        bool initialized = false;
        float? smoothTemp = null;
        string? sensorFor = null;
        List<SensorInfo> sensors = new();
        string? error = null;

        while (!_stop)
        {
            var s = _settings;
            var now = DateTime.UtcNow;
            if (now - lastLoop > SuspendGap) initialized = false; // woke from sleep: MCU may have reset
            lastLoop = now;

            try
            {
                led ??= RenesasLed.Open(_ => { });
                if (computer == null)
                {
                    computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true };
                    computer.Open();
                }

                if (now - lastSensors >= SensorInterval)
                {
                    lastSensors = now;
                    sensors = ReadSensors(computer);
                    if (sensorFor != s.SensorId) { smoothTemp = null; sensorFor = s.SensorId; }
                    var raw = sensors.FirstOrDefault(x => x.Id == s.SensorId)?.Value;
                    // light smoothing so the colour doesn't jitter with every 1 °C spike
                    smoothTemp = raw is float r ? (smoothTemp is float p ? p + (r - p) * 0.5f : r) : null;
                }

                var target = ColorEngine.Compute(s, clock.Elapsed.TotalSeconds, smoothTemp);

                if (!initialized)
                {
                    led.Init(ZoneMask.All);
                    initialized = true;
                    written = null;
                }

                bool animated = ColorEngine.IsAnimated(s);
                if (written != target || now - lastWrite >= Reassert)
                {
                    led.SetColor(ZoneMask.All, target, fast: animated);
                    written = target;
                    lastWrite = now;
                }
                error = s.Mode switch
                {
                    LightMode.Temperature when s.Enabled && smoothTemp == null => "датчик не найден",
                    _ => null,
                };
                _status = new EngineStatus(target, smoothTemp, error, sensors);
            }
            catch (Exception e)
            {
                error = e.Message;
                _status = _status with { Error = error };
                initialized = false;
                if (e is NotSupportedException || e is InvalidOperationException) { led?.Dispose(); led = null; }
                _wake.WaitOne(TimeSpan.FromSeconds(5));
                continue;
            }

            _wake.WaitOne(ColorEngine.IsAnimated(_settings) ? FrameMs : StaticTickMs);
        }

        try { computer?.Close(); } catch { }
        led?.Dispose();
    }

    private static List<SensorInfo> ReadSensors(Computer c)
    {
        var list = new List<SensorInfo>();
        void Walk(IHardware h)
        {
            h.Update();
            foreach (var s in h.Sensors)
            {
                if (s.SensorType != SensorType.Temperature) continue;
                if (s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase) ||
                    s.Name.Contains("Limit", StringComparison.OrdinalIgnoreCase) ||
                    s.Name.Contains("Resolution", StringComparison.OrdinalIgnoreCase)) continue;
                if (s.Value is float v && (v <= 0 || v >= 110)) continue; // unconnected Super I/O inputs read 110 / -
                list.Add(new SensorInfo(s.Identifier.ToString(), $"{h.Name} — {s.Name}", s.Value));
            }
            foreach (var sub in h.SubHardware) Walk(sub);
        }
        foreach (var h in c.Hardware) Walk(h);
        return list;
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(3));
    }
}
