using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MsiRgb;

/// <summary>Notification-area icon: a dot in the current colour, quick mode switch, on/off.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _static, _effect, _temp, _enabled;
    private Rgb? _shown;
    private IntPtr _handle;
    private DateTime _lastIcon;

    public TrayIcon(Action show, Action<LightMode> setMode, Action<bool> setEnabled, Action quit)
    {
        var menu = new ContextMenuStrip { Font = new Font("Segoe UI", 10f), ShowImageMargin = false, Padding = new Padding(4) };
        menu.Items.Add(new ToolStripMenuItem("Открыть MSI RGB", null, (_, _) => show()) { Font = new Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        _static = new ToolStripMenuItem("Цвет", null, (_, _) => setMode(LightMode.Static));
        _effect = new ToolStripMenuItem("Эффект", null, (_, _) => setMode(LightMode.Effect));
        _temp = new ToolStripMenuItem("По температуре", null, (_, _) => setMode(LightMode.Temperature));
        menu.Items.AddRange(new ToolStripItem[] { _static, _effect, _temp, new ToolStripSeparator() });
        _enabled = new ToolStripMenuItem("Подсветка включена", null, (_, _) => setEnabled(!_enabled!.Checked));
        menu.Items.Add(_enabled);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => quit());

        _icon = new NotifyIcon { Text = "MSI RGB", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) show(); };
        SetIcon(new Rgb(0, 64, 255));
    }

    public void Sync(AppSettings s)
    {
        // a check mark in front of the active mode, like in Windows 11's own tray menus
        _static.Text = (s.Mode == LightMode.Static ? "✓  " : "     ") + "Цвет";
        _effect.Text = (s.Mode == LightMode.Effect ? "✓  " : "     ") + "Эффект";
        _temp.Text = (s.Mode == LightMode.Temperature ? "✓  " : "     ") + "По температуре";
        _enabled.Checked = s.Enabled;
        _enabled.Text = s.Enabled ? "Выключить подсветку" : "Включить подсветку";
    }

    public void ShowStatus(EngineStatus st, AppSettings s)
    {
        string text = !s.Enabled ? "MSI RGB — выключено"
            : st.Error != null ? $"MSI RGB — {st.Error}"
            : s.Mode == LightMode.Temperature && st.Temperature is float t ? $"MSI RGB — {t:0} °C"
            : $"MSI RGB — {st.Color}";
        if (_icon.Text != text) _icon.Text = text.Length > 63 ? text[..63] : text;
        // effects change colour every frame; the icon follows at ~3 Hz, plenty for a 16 px dot
        if (_shown != st.Color && DateTime.UtcNow - _lastIcon > TimeSpan.FromMilliseconds(300)) SetIcon(st.Color);
    }

    private void SetIcon(Rgb c)
    {
        int size = SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, size - 2.5f, size - 2.5f);
            using var fill = new SolidBrush(c == new Rgb(0, 0, 0) ? Color.FromArgb(60, 60, 60) : Color.FromArgb(c.R, c.G, c.B));
            g.FillEllipse(fill, r);
            using var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.2f);
            g.DrawEllipse(pen, r);
        }
        var h = bmp.GetHicon();
        var old = _icon.Icon;
        _icon.Icon = Icon.FromHandle(h);
        old?.Dispose();
        if (_handle != IntPtr.Zero) DestroyIcon(_handle);
        _handle = h;
        _shown = c;
        _lastIcon = DateTime.UtcNow;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_handle != IntPtr.Zero) DestroyIcon(_handle);
    }
}
