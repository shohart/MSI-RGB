using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MsiRgb;

internal sealed class SettingsForm : Form
{
    private readonly TrayApp _app;
    private AppSettings _s;
    private bool _loading;

    private readonly Dictionary<LightMode, RadioButton> _modeRadios = new();
    private readonly Dictionary<LightMode, Control> _modePanels = new();

    private readonly ColorButton _static = new();
    private readonly ComboBox _effect = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ColorButton _fx1 = new(), _fx2 = new();
    private readonly Label _fx2Label = Caption("Второй цвет");
    private readonly TrackBar _speed = new() { Minimum = 1, Maximum = 10, TickFrequency = 1, Width = 220 };
    private readonly ComboBox _sensor = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
    private readonly NumericUpDown _tFrom = Temp(), _tTo = Temp();
    private readonly ColorButton _tcFrom = new(), _tcTo = new();
    private readonly GradientBar _tBar = new();
    private readonly Label _tNow = Caption("");
    private readonly ColorButton _fcFrom = new(), _fcTo = new();
    private readonly GradientBar _fcBar = new();
    private readonly Label _fcNow = Caption("");
    private readonly TrackBar _bright = new() { Minimum = 5, Maximum = 100, TickFrequency = 10, Width = 220 };
    private readonly Label _brightLabel = Caption("");
    private readonly CheckBox _autostart = new() { Text = "Запускать при входе в Windows", AutoSize = true };
    private readonly Panel _swatch = new() { Width = 28, Height = 28, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(4, 7, 0, 0) };

    private static readonly (EffectKind Kind, string Text)[] Effects =
    {
        (EffectKind.Breathing, "Дыхание"),
        (EffectKind.Rainbow, "Радуга"),
        (EffectKind.Blink, "Мигание"),
        (EffectKind.TwoColor, "Переливание двух цветов"),
    };

    public SettingsForm(AppSettings s, TrayApp app)
    {
        _app = app;
        _s = s;
        Text = "MSI RGB";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(12);

        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        Controls.Add(root);

        // --- mode
        var modes = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        foreach (var (mode, text) in new[]
                 {
                     (LightMode.Static, "Цвет"), (LightMode.Effect, "Эффект"),
                     (LightMode.Temperature, "По температуре"), (LightMode.FanControl, "По кривой FanControl"),
                 })
        {
            var rb = new RadioButton { Text = text, AutoSize = true, Margin = new Padding(0, 0, 14, 0) };
            rb.CheckedChanged += (_, _) => { if (rb.Checked) Change(_s with { Mode = mode }); };
            _modeRadios[mode] = rb;
            modes.Controls.Add(rb);
        }
        root.Controls.Add(modes);

        // --- per-mode panels
        _modePanels[LightMode.Static] = Grid(
            (Caption("Цвет"), _static));

        foreach (var e in Effects) _effect.Items.Add(e.Text);
        _modePanels[LightMode.Effect] = Grid(
            (Caption("Эффект"), _effect),
            (Caption("Цвет"), _fx1),
            (_fx2Label, _fx2),
            (Caption("Скорость"), _speed));

        _modePanels[LightMode.Temperature] = Grid(
            (Caption("Датчик"), _sensor),
            (Caption("От"), Row(_tFrom, Caption("°C  цвет"), _tcFrom)),
            (Caption("До"), Row(_tTo, Caption("°C  цвет"), _tcTo)),
            (Caption(""), _tBar),
            (Caption("Сейчас"), _tNow));

        _modePanels[LightMode.FanControl] = Grid(
            (Caption("0 %"), _fcFrom),
            (Caption("100 %"), _fcTo),
            (Caption(""), _fcBar),
            (Caption("Сейчас"), _fcNow),
            (Caption(""), new Label
            {
                AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText,
                Text = "Нужен плагин FanControl.MsiRgb.dll в папке Plugins. В FanControl появится контрол " +
                       "«MSI RGB (цвет из приложения)»: назначьте ему любую кривую. Пока связи нет, горит цвет 0 %.",
            }));

        var panels = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, MinimumSize = new Size(480, 175) };
        foreach (var p in _modePanels.Values) panels.Controls.Add(p);
        root.Controls.Add(panels);

        // --- common
        root.Controls.Add(Grid(
            (Caption("Яркость"), Row(_bright, _brightLabel)),
            (Caption(""), _autostart)));
        root.Controls.Add(Row(_swatch, _status));

        // --- events
        _static.Changed += c => Change(_s with { StaticColor = c.ToString() });
        _effect.SelectedIndexChanged += (_, _) => { if (_effect.SelectedIndex >= 0) Change(_s with { Effect = Effects[_effect.SelectedIndex].Kind }); };
        _fx1.Changed += c => Change(_s with { EffectColor1 = c.ToString() });
        _fx2.Changed += c => Change(_s with { EffectColor2 = c.ToString() });
        _speed.ValueChanged += (_, _) => Change(_s with { EffectSpeed = _speed.Value });
        _sensor.SelectedIndexChanged += (_, _) => { if (_sensor.SelectedItem is SensorItem it) Change(_s with { SensorId = it.Id }); };
        _tFrom.ValueChanged += (_, _) => Change(_s with { TempFrom = (float)_tFrom.Value });
        _tTo.ValueChanged += (_, _) => Change(_s with { TempTo = (float)_tTo.Value });
        _tcFrom.Changed += c => Change(_s with { TempColorFrom = c.ToString() });
        _tcTo.Changed += c => Change(_s with { TempColorTo = c.ToString() });
        _fcFrom.Changed += c => Change(_s with { FanColorFrom = c.ToString() });
        _fcTo.Changed += c => Change(_s with { FanColorTo = c.ToString() });
        _bright.ValueChanged += (_, _) => Change(_s with { Brightness = _bright.Value });
        _autostart.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            try { Autostart.Set(_autostart.Checked); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Автозапуск"); }
        };

        LoadSettings(s);
        _loading = true;
        try { _autostart.Checked = Autostart.IsEnabled(); } catch { }
        _loading = false;
    }

    private void Change(AppSettings s)
    {
        if (_loading) return;
        _app.Update(s);
    }

    public void LoadSettings(AppSettings s)
    {
        _s = s;
        _loading = true;
        try
        {
            _modeRadios[s.Mode].Checked = true;
            foreach (var (mode, panel) in _modePanels) panel.Visible = mode == s.Mode;
            _static.Color = Rgb.Parse(s.StaticColor);
            _effect.SelectedIndex = Array.FindIndex(Effects, e => e.Kind == s.Effect);
            _fx1.Color = Rgb.Parse(s.EffectColor1);
            _fx2.Color = Rgb.Parse(s.EffectColor2);
            _fx2.Visible = _fx2Label.Visible = s.Effect == EffectKind.TwoColor;
            _fx1.Enabled = s.Effect != EffectKind.Rainbow;
            _speed.Value = s.EffectSpeed;
            _tFrom.Value = (decimal)Math.Clamp(s.TempFrom, 0, 120);
            _tTo.Value = (decimal)Math.Clamp(s.TempTo, 0, 120);
            _tcFrom.Color = Rgb.Parse(s.TempColorFrom);
            _tcTo.Color = Rgb.Parse(s.TempColorTo);
            _tBar.Set(_tcFrom.Color, _tcTo.Color, $"{s.TempFrom:0} °C", $"{s.TempTo:0} °C");
            _fcFrom.Color = Rgb.Parse(s.FanColorFrom);
            _fcTo.Color = Rgb.Parse(s.FanColorTo);
            _fcBar.Set(_fcFrom.Color, _fcTo.Color, "0 %", "100 %");
            _bright.Value = s.Brightness;
            _brightLabel.Text = $"{s.Brightness} %";
            SelectSensor(s.SensorId);
        }
        finally { _loading = false; }
    }

    public void ShowStatus(EngineStatus st)
    {
        _swatch.BackColor = Color.FromArgb(st.Color.R, st.Color.G, st.Color.B);
        _status.Text = st.Error != null ? $"{st.Color}  ⚠ {st.Error}" : st.Color.ToString();
        _tNow.Text = st.Temperature is float t ? $"{t:0.0} °C" : "—";
        _fcNow.Text = st.FanPercent is float p ? $"{p:0} %" : "нет связи";
        if (st.Temperature is float tv) _tBar.Marker = (tv - _s.TempFrom) / (_s.TempTo - _s.TempFrom);
        _fcBar.Marker = st.FanPercent / 100f;

        // rebuild the sensor list only when the set of sensors changes, keep the dropdown usable
        var ids = st.Sensors.Select(x => x.Id).ToList();
        var have = _sensor.Items.Cast<SensorItem>().Select(x => x.Id).ToList();
        if (!_sensor.DroppedDown && !ids.SequenceEqual(have))
        {
            _loading = true;
            try
            {
                _sensor.Items.Clear();
                foreach (var x in st.Sensors) _sensor.Items.Add(new SensorItem(x.Id, x.Name));
                if (_s.SensorId != null && !ids.Contains(_s.SensorId))
                    _sensor.Items.Add(new SensorItem(_s.SensorId, _s.SensorId + " (нет)"));
                SelectSensor(_s.SensorId);
            }
            finally { _loading = false; }
        }
    }

    private void SelectSensor(string? id)
    {
        for (int i = 0; i < _sensor.Items.Count; i++)
            if (((SensorItem)_sensor.Items[i]!).Id == id) { _sensor.SelectedIndex = i; return; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // the close box hides to the tray; "Выход" in the tray menu really quits
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    private sealed record SensorItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(0, 6, 8, 0) };

    private static NumericUpDown Temp() => new() { Minimum = 0, Maximum = 120, Width = 60 };

    private static FlowLayoutPanel Row(params Control[] items)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        p.Controls.AddRange(items);
        return p;
    }

    private static TableLayoutPanel Grid(params (Control Label, Control Value)[] rows)
    {
        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var (l, v) in rows)
        {
            t.RowCount++;
            t.Controls.Add(l);
            t.Controls.Add(v);
        }
        return t;
    }
}

/// <summary>Swatch button that opens the colour picker.</summary>
internal sealed class ColorButton : Button
{
    private Rgb _color;
    public event Action<Rgb>? Changed;

    public ColorButton()
    {
        Width = 120;
        Height = 28;
        FlatStyle = FlatStyle.Flat;
        Click += (_, _) =>
        {
            using var dlg = new ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(_color.R, _color.G, _color.B) };
            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
            Color = new Rgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
            Changed?.Invoke(_color);
        };
    }

    public Rgb Color
    {
        get => _color;
        set
        {
            _color = value;
            BackColor = System.Drawing.Color.FromArgb(value.R, value.G, value.B);
            ForeColor = value.R * 0.3 + value.G * 0.59 + value.B * 0.11 > 140 ? System.Drawing.Color.Black : System.Drawing.Color.White;
            Text = value.ToString();
        }
    }
}

/// <summary>Preview of the from→to scale with a marker for the current value.</summary>
internal sealed class GradientBar : Control
{
    private Rgb _a, _b;
    private string _left = "", _right = "";
    private float? _marker;

    public GradientBar()
    {
        Width = 340;
        Height = 34;
        DoubleBuffered = true;
    }

    public float? Marker
    {
        get => _marker;
        set { if (_marker != value) { _marker = value; Invalidate(); } }
    }

    public void Set(Rgb a, Rgb b, string left, string right)
    {
        (_a, _b, _left, _right) = (a, b, left, right);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var bar = new Rectangle(0, 0, Width - 1, 16);
        using (var br = new LinearGradientBrush(bar, Color.FromArgb(_a.R, _a.G, _a.B), Color.FromArgb(_b.R, _b.G, _b.B), 0f))
            g.FillRectangle(br, bar);
        g.DrawRectangle(SystemPens.ControlDark, bar);
        if (_marker is float m)
        {
            int x = (int)(Math.Clamp(m, 0f, 1f) * (Width - 1));
            using var pen = new Pen(Color.Black, 3);
            g.DrawLine(pen, x, 0, x, 16);
            g.DrawLine(Pens.White, x, 0, x, 16);
        }
        TextRenderer.DrawText(g, _left, Font, new Point(0, 18), ForeColor);
        var sz = TextRenderer.MeasureText(_right, Font);
        TextRenderer.DrawText(g, _right, Font, new Point(Width - sz.Width, 18), ForeColor);
    }
}
