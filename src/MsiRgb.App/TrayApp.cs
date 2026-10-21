using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MsiRgb;

internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly LightEngine _engine;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly RegisteredWaitHandle _showWait;
    private readonly ToolStripMenuItem[] _modeItems;
    private SettingsForm? _form;
    private AppSettings _settings;
    private Rgb? _iconColor;
    private IntPtr _iconHandle;

    private static readonly (LightMode Mode, string Text)[] Modes =
    {
        (LightMode.Static, "Цвет"),
        (LightMode.Effect, "Эффект"),
        (LightMode.Temperature, "По температуре"),
        (LightMode.FanControl, "По кривой FanControl"),
    };

    public TrayApp(EventWaitHandle showEvent, bool startHidden)
    {
        _settings = AppSettings.Load(AppSettings.DefaultPath);
        _engine = new LightEngine(_settings);
        _engine.Start();

        var menu = new ContextMenuStrip();
        _modeItems = new ToolStripMenuItem[Modes.Length];
        for (int i = 0; i < Modes.Length; i++)
        {
            var mode = Modes[i].Mode;
            _modeItems[i] = new ToolStripMenuItem(Modes[i].Text, null, (_, _) => Update(_settings with { Mode = mode }));
            menu.Items.Add(_modeItems[i]);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Настройки…", null, (_, _) => ShowForm());
        menu.Items.Add("Выход", null, (_, _) => ExitThread());

        _tray = new NotifyIcon { Text = "MSI RGB", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowForm();
        SyncMenu();

        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();

        var ui = SynchronizationContext.Current!;
        _showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => ui.Post(_ => ShowForm(), null), null, -1, false);

        if (!startHidden) ShowForm();
    }

    /// <summary>Single place where settings change: engine, disk, tray menu, window.</summary>
    public void Update(AppSettings s)
    {
        _settings = s.Sanitized();
        _engine.Apply(_settings);
        try { _settings.Save(AppSettings.DefaultPath); } catch { }
        SyncMenu();
        _form?.LoadSettings(_settings);
    }

    private void SyncMenu()
    {
        for (int i = 0; i < Modes.Length; i++) _modeItems[i].Checked = Modes[i].Mode == _settings.Mode;
    }

    private void ShowForm()
    {
        if (_form == null || _form.IsDisposed)
        {
            _form = new SettingsForm(_settings, this);
            _form.FormClosed += (_, _) => _form = null;
        }
        _form.Show();
        if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
        _form.Activate();
    }

    private void Tick()
    {
        var st = _engine.Status;
        _form?.ShowStatus(st);
        string mode = Array.Find(Modes, m => m.Mode == _settings.Mode).Text;
        string text = st.Error != null ? $"MSI RGB: {mode} — {st.Error}" : $"MSI RGB: {mode} {st.Color}";
        if (st.Temperature is float t && _settings.Mode == LightMode.Temperature) text += $" ({t:0} °C)";
        _tray.Text = text.Length > 63 ? text[..63] : text;
        // effects change colour every frame; the icon follows at the timer rate, that's enough
        if (_iconColor != st.Color) SetIcon(st.Color);
    }

    private void SetIcon(Rgb c)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(Color.FromArgb(c.R, c.G, c.B));
            g.FillEllipse(fill, 1, 1, 14, 14);
            using var pen = new Pen(Color.FromArgb(160, 128, 128, 128));
            g.DrawEllipse(pen, 1, 1, 14, 14);
        }
        var h = bmp.GetHicon();
        var old = _tray.Icon;
        _tray.Icon = Icon.FromHandle(h);
        old?.Dispose();
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
        _iconHandle = h;
        _iconColor = c;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _showWait.Unregister(null);
        _tray.Visible = false;
        _tray.Dispose();
        _engine.Dispose(); // the MCU keeps the last colour
        _form?.Dispose();
        base.ExitThreadCore();
    }
}
