using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MsiRgb;

/// <summary>Notification-area icon mirroring the live LED state (see <see cref="TrayIconArt"/>), quick mode switch, on/off.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _static, _effect, _temp, _enabled;
    private IconState? _shown;
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
        SetIcon(new IconState(IconMode.Static, new Rgb(0, 64, 255), 0, 0));
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
        string breath = $"дыхание {ColorEngine.BreathRate(s, st.Temperature):0}/мин";
        string text = !s.Enabled ? "MSI RGB — выключено"
            : st.Error != null ? $"MSI RGB — {st.Error}"
            : s.Mode == LightMode.Temperature && st.Temperature is float t ? $"MSI RGB — {t:0} °C" + (s.TempBreathing ? $" · {breath}" : "")
            : s.Mode == LightMode.Effect ? "MSI RGB — " + s.Effect switch
            {
                EffectKind.Breathing => breath,
                EffectKind.Rainbow => "радуга",
                EffectKind.Blink => "мигание",
                _ => "переливание",
            }
            : $"MSI RGB — {st.Color}";
        if (_icon.Text != text) _icon.Text = text.Length > 63 ? text[..63] : text;

        var mode = !s.Enabled ? IconMode.Off
            : st.Error != null ? IconMode.Error
            : s.Mode switch { LightMode.Effect => IconMode.Effect, LightMode.Temperature => IconMode.Temperature, _ => IconMode.Static };
        // the effect ring turns a quarter turn per second; static states compare equal and aren't redrawn
        float spin = mode == IconMode.Effect ? (float)(Environment.TickCount64 / 1000.0 * 90 % 360) : 0;
        var state = new IconState(mode, st.Color, mode == IconMode.Temperature ? ColorEngine.Heat(s, st.Temperature) : 0, spin);
        // effects change colour every frame; the icon follows at ~10 Hz, the poll rate
        if (_shown != state && DateTime.UtcNow - _lastIcon > TimeSpan.FromMilliseconds(90)) SetIcon(state);
    }

    private void SetIcon(IconState state)
    {
        using var bmp = TrayIconArt.Render(SystemInformation.SmallIconSize.Width, state);
        var h = bmp.GetHicon();
        var old = _icon.Icon;
        _icon.Icon = Icon.FromHandle(h);
        old?.Dispose();
        if (_handle != IntPtr.Zero) DestroyIcon(_handle);
        _handle = h;
        _shown = state;
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
