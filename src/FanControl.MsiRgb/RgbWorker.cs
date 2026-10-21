using System;
using System.Runtime.Versioning;
using System.Collections.Generic;
using System.Threading;

namespace FanControl.MsiRgb;

/// <summary>Runtime state of one configured channel. Set() is called from FanControl's thread.</summary>
internal sealed class Channel
{
    private readonly object _gate = new();
    private float? _gradient;
    private readonly float?[] _rgb = new float?[3];

    public Channel(ChannelConfig cfg)
    {
        Config = cfg;
        Mask = cfg.Mask;
        Gradient = cfg.IsRgb ? null : cfg.Gradient;
        Default = cfg.DefaultColor;
    }

    public ChannelConfig Config { get; }
    public ZoneMask Mask { get; }
    public Gradient? Gradient { get; }
    public Rgb Default { get; }

    /// <summary>Last colour actually written to the MCU (worker thread only).</summary>
    public Rgb? Written { get; set; }

    public void SetGradient(float? percent) { lock (_gate) _gradient = percent; }
    public void SetComponent(int i, float? percent) { lock (_gate) _rgb[i] = percent; }

    public Rgb Target()
    {
        lock (_gate)
        {
            if (Gradient != null)
                return _gradient is float p ? Gradient.At(p) : Default;
            return new Rgb(
                _rgb[0] is float r ? ToByte(r) : Default.R,
                _rgb[1] is float g ? ToByte(g) : Default.G,
                _rgb[2] is float b ? ToByte(b) : Default.B);
        }
    }

    internal static byte ToByte(float percent) => (byte)Math.Round(Math.Clamp(percent, 0f, 100f) * 2.55f);
}

/// <summary>Single thread that owns all SMBus traffic; FanControl threads only flag changes.</summary>
[SupportedOSPlatform("windows")]
internal sealed class RgbWorker : IDisposable
{
    private static readonly TimeSpan SuspendGap = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);

    private readonly RenesasLed _led;
    private readonly IReadOnlyList<Channel> _channels;
    private readonly TimeSpan _reassert;
    private readonly Action<string> _log;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private volatile bool _stop;

    public RgbWorker(RenesasLed led, IReadOnlyList<Channel> channels, TimeSpan reassert, Action<string> log)
    {
        _led = led;
        _channels = channels;
        _reassert = reassert;
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "MsiRgb SMBus" };
    }

    public void Start() => _thread.Start();
    public void Poke() => _wake.Set();

    private void Run()
    {
        bool initialized = false;
        DateTime lastLoop = DateTime.UtcNow;
        DateTime lastReassert = DateTime.MinValue;
        string? lastError = null;

        while (!_stop)
        {
            _wake.WaitOne(TimeSpan.FromSeconds(1));
            if (_stop) break;
            Thread.Sleep(50); // coalesce R/G/B Set() calls from one FanControl tick

            var now = DateTime.UtcNow;
            if (now - lastLoop > SuspendGap && initialized)
            {
                _log("resume detected, re-initialising RGB controller");
                initialized = false;
            }
            lastLoop = now;

            try
            {
                if (!initialized)
                {
                    foreach (var ch in _channels)
                    {
                        _led.Init(ch.Mask);
                        ch.Written = null;
                    }
                    initialized = true;
                }

                bool reassert = now - lastReassert >= _reassert;
                foreach (var ch in _channels)
                {
                    var target = ch.Target();
                    if (reassert || ch.Written != target)
                    {
                        _led.SetColor(ch.Mask, target);
                        ch.Written = target;
                    }
                }
                if (reassert) lastReassert = now;
                lastError = null;
            }
            catch (Exception e)
            {
                if (e.Message != lastError) _log("RGB write failed: " + e.Message);
                lastError = e.Message;
                initialized = false;
                _wake.WaitOne(ErrorBackoff);
            }
        }
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(3));
        _wake.Dispose();
    }
}
