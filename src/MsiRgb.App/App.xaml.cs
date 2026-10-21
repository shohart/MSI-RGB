using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace MsiRgb;

public partial class App : Application
{
    private const string InstanceName = @"Global\MsiRgbApp";
    private const string ShowEventName = @"Global\MsiRgbApp.Show";

    private Mutex? _instance;
    private EventWaitHandle? _showEvent;
    private LightEngine? _engine;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private AppSettings _settings = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplicationThemeManager.ApplySystemTheme();

        int shot = Array.FindIndex(e.Args, a => a == "--screenshot");
        if (shot >= 0)
        {
            Screenshots(e.Args.Length > shot + 1 ? e.Args[shot + 1] : ".");
            Shutdown();
            return;
        }

        _instance = new Mutex(true, InstanceName, out bool first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(ShowWindow), null, -1, false);

        _settings = AppSettings.Load(AppSettings.DefaultPath);
        _engine = new LightEngine(_settings);
        _engine.Start();

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            try { _settings.Save(AppSettings.DefaultPath); } catch { }
        };

        _tray = new TrayIcon(ShowWindow, mode => Update(_settings with { Mode = mode }), on => Update(_settings with { Enabled = on }), Quit);
        _tray.Sync(_settings);

        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        poll.Tick += (_, _) =>
        {
            var st = _engine.Status;
            _tray.ShowStatus(st, _settings);
            if (_window?.IsVisible == true) _window.ShowStatus(st);
        };
        poll.Start();

        if (!e.Args.Contains("--tray")) ShowWindow();
    }

    /// <summary>Single place where settings change: engine now, disk debounced, tray and window.</summary>
    private void Update(AppSettings s)
    {
        _settings = s.Sanitized();
        _engine?.Apply(_settings);
        _saveTimer.Stop();
        _saveTimer.Start();
        _tray?.Sync(_settings);
        if (_window != null && !ReferenceEquals(s, _settings)) _window.LoadSettings(_settings);
    }

    private void ShowWindow()
    {
        if (_window == null)
        {
            _window = new MainWindow(_settings, s => { _settings = s; Update(s); });
        }
        else _window.LoadSettings(_settings);
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void Quit()
    {
        _saveTimer.Stop();
        try { _settings.Save(AppSettings.DefaultPath); } catch { }
        _tray?.Dispose();
        _engine?.Dispose(); // the MCU keeps the last colour
        _window?.CloseForReal();
        Shutdown();
    }

    /// <summary>--screenshot DIR: renders every page to PNG without touching hardware (for docs and design review).</summary>
    private void Screenshots(string dir)
    {
        Directory.CreateDirectory(dir);
        var sensors = new[]
        {
            new SensorInfo("/intelcpu/0/temperature/10", "CPU — Package", 62),
            new SensorInfo("/gpu-nvidia/0/temperature/0", "RTX 4070 — GPU Core", 48),
            new SensorInfo("/memory/dimm/0/temperature/0", "DIMM #0", 33),
        };
        var pages = new (string Name, AppSettings S, Rgb Color, float? T)[]
        {
            ("temperature", new AppSettings(), new Rgb(0x8F, 0x20, 0x8F), 62),
            ("temperature-breathing", new AppSettings { TempBreathing = true }, new Rgb(0x8F, 0x20, 0x8F), 62),
            ("breathing", new AppSettings { Mode = LightMode.Effect, Effect = EffectKind.Breathing }, new Rgb(0, 0x30, 0xC0), null),
            ("static", new AppSettings { Mode = LightMode.Static, StaticColor = "#00FF80" }, new Rgb(0, 255, 128), null),
            ("effect", new AppSettings { Mode = LightMode.Effect, Effect = EffectKind.TwoColor }, new Rgb(0x80, 0x20, 0xA0), null),
            ("off", new AppSettings { Enabled = false, Mode = LightMode.Static }, new Rgb(0, 0, 0), null),
        };
        foreach (var (name, s, color, t) in pages)
        {
            var w = new MainWindow(s, _ => { }) { Height = name == "temperature-breathing" ? 1300 : 830, Left = -20000, Top = -20000, ShowActivated = false, WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None };
            w.Show();
            w.ShowStatus(new EngineStatus(color, t, null, sensors));
            w.UpdateLayout();
            Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var root = (System.Windows.Controls.Panel)w.Content;
            // Mica doesn't exist offscreen: paint the theme's base background instead
            root.Background = TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.Black;
            root.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(Path.Combine(dir, name + ".png"))) enc.Save(f);
            w.CloseForReal();
        }

        // tray icon in every state, at the real small-icon size and a 16 px one, side by side on dark and light strips
        var states = new[]
        {
            new IconState(IconMode.Static, new Rgb(0, 64, 255), 0, 0),
            new IconState(IconMode.Static, new Rgb(0, 255, 128), 0, 0),
            new IconState(IconMode.Effect, new Rgb(255, 0, 128), 0, 20),
            new IconState(IconMode.Effect, new Rgb(10, 3, 0), 0, 80),
            new IconState(IconMode.Temperature, new Rgb(0x8F, 0x20, 0x8F), 0.5f, 0),
            new IconState(IconMode.Temperature, new Rgb(255, 0, 0), 1f, 0),
            new IconState(IconMode.Off, new Rgb(0, 0, 0), 0, 0),
            new IconState(IconMode.Error, new Rgb(0, 0, 0), 0, 0),
        };
        const int big = 40; // small-icon size at 250 %
        using var sheet = new System.Drawing.Bitmap(states.Length * (big + 12) + 12, 2 * (big + 16 + 24));
        using (var g = System.Drawing.Graphics.FromImage(sheet))
        {
            g.Clear(System.Drawing.Color.FromArgb(32, 32, 32));
            g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(238, 238, 238)), 0, sheet.Height / 2, sheet.Width, sheet.Height / 2);
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < states.Length; i++)
                {
                    int x = 12 + i * (big + 12), y = row * sheet.Height / 2 + 8;
                    using var a = TrayIconArt.Render(big, states[i]);
                    using var b = TrayIconArt.Render(16, states[i]);
                    g.DrawImage(a, x, y);
                    g.DrawImage(b, x, y + big + 4);
                }
        }
        sheet.Save(Path.Combine(dir, "tray.png"), System.Drawing.Imaging.ImageFormat.Png);
    }
}
