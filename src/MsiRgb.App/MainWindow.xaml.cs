using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MsiRgb.Controls;
using Wpf.Ui.Appearance;
using WpfColor = System.Windows.Media.Color;

namespace MsiRgb;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly Action<AppSettings> _onChange;
    private AppSettings _s;
    private bool _loading;
    private bool _reallyClose;
    private List<string> _sensorIds = new();

    internal MainWindow(AppSettings s, Action<AppSettings> onChange)
    {
        _onChange = onChange;
        _s = s;
        InitializeComponent();
        Loaded += (_, _) => SystemThemeWatcher.Watch(this);
        SourceInitialized += (_, _) => FitToWorkArea();

        Brightness.TrackBrush = GradientSlider.Horizontal(WpfColor.FromRgb(40, 40, 40), Colors.White);
        Speed.TrackBrush = GradientSlider.Horizontal(WpfColor.FromRgb(90, 90, 90), WpfColor.FromRgb(0, 120, 212));

        EnabledSwitch.Checked += (_, _) => Change(_s with { Enabled = true });
        EnabledSwitch.Unchecked += (_, _) => Change(_s with { Enabled = false });
        Brightness.UserChanged += v => Change(_s with { Brightness = (int)Math.Round(5 + v * 95) });
        AutostartSwitch.Checked += (_, _) => SetAutostart(true);
        AutostartSwitch.Unchecked += (_, _) => SetAutostart(false);

        ModeStatic.Checked += (_, _) => Change(_s with { Mode = LightMode.Static });
        ModeEffect.Checked += (_, _) => Change(_s with { Mode = LightMode.Effect });
        ModeTemp.Checked += (_, _) => Change(_s with { Mode = LightMode.Temperature });

        StaticPicker.ColorChanged += c => Change(_s with { StaticColor = c.ToString() });

        FxBreathing.Checked += (_, _) => Change(_s with { Effect = EffectKind.Breathing });
        FxRainbow.Checked += (_, _) => Change(_s with { Effect = EffectKind.Rainbow });
        FxBlink.Checked += (_, _) => Change(_s with { Effect = EffectKind.Blink });
        FxTwoColor.Checked += (_, _) => Change(_s with { Effect = EffectKind.TwoColor });
        Speed.UserChanged += v => Change(_s.Effect == EffectKind.Breathing
            ? _s with { BreathRate = RateFromSlider(v) }
            : _s with { EffectSpeed = 1 + (int)Math.Round(v * 9) });
        FxEdit1.Checked += (_, _) => LoadSettings(_s);
        FxEdit2.Checked += (_, _) => LoadSettings(_s);
        FxPicker.ColorChanged += c => Change(FxEditingSecond ? _s with { EffectColor2 = c.ToString() } : _s with { EffectColor1 = c.ToString() });

        SensorBox.SelectionChanged += (_, _) => { if (SensorBox.SelectedItem is SensorInfo si) Change(_s with { SensorId = si.Id }); };
        TempFrom.ValueChanged += (_, _) => { if (TempFrom.Value is double v) Change(_s with { TempFrom = (float)v }); };
        TempTo.ValueChanged += (_, _) => { if (TempTo.Value is double v) Change(_s with { TempTo = (float)v }); };
        TempEditFrom.Checked += (_, _) => LoadSettings(_s);
        TempEditTo.Checked += (_, _) => LoadSettings(_s);
        TempPicker.ColorChanged += c => Change(TempEditTo.IsChecked == true ? _s with { TempColorTo = c.ToString() } : _s with { TempColorFrom = c.ToString() });
        TempBreathSwitch.Checked += (_, _) => Change(_s with { TempBreathing = true });
        TempBreathSwitch.Unchecked += (_, _) => Change(_s with { TempBreathing = false });
        BreathCold.UserChanged += v => Change(_s with { TempBreathRateCold = RateFromSlider(v) });
        BreathHot.UserChanged += v => Change(_s with { TempBreathRateHot = RateFromSlider(v) });
        TempMarkerCanvas.SizeChanged += (_, _) => PlaceMarker(_lastTemp);

        LoadSettings(s);
        _loading = true;
        try { AutostartSwitch.IsChecked = Autostart.IsEnabled(); } catch { }
        _loading = false;
    }

    /// <summary>High-DPI laptops / 4K at 250 % have ~800 px of work area: never open taller than the screen.</summary>
    private void FitToWorkArea()
    {
        var wa = SystemParameters.WorkArea;
        Height = Math.Min(Height, wa.Height - 24);
        Width = Math.Min(Width, wa.Width - 24);
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 2;
    }

    private bool FxEditingSecond => _s.Effect == EffectKind.TwoColor && FxEdit2.IsChecked == true;

    private void Change(AppSettings s)
    {
        if (_loading) return;
        _s = s.Sanitized();
        _onChange(_s);
        LoadSettings(_s);
    }

    private void SetAutostart(bool on)
    {
        if (_loading) return;
        try { Autostart.Set(on); }
        catch (Exception e)
        {
            System.Windows.MessageBox.Show(this, e.Message, "Автозапуск");
            _loading = true;
            AutostartSwitch.IsChecked = !on;
            _loading = false;
        }
    }

    internal void LoadSettings(AppSettings s)
    {
        _s = s;
        _loading = true;
        try
        {
            EnabledSwitch.IsChecked = s.Enabled;
            Brightness.Value = (s.Brightness - 5) / 95.0;
            BrightnessValue.Text = $"{s.Brightness} %";

            ModeStatic.IsChecked = s.Mode == LightMode.Static;
            ModeEffect.IsChecked = s.Mode == LightMode.Effect;
            ModeTemp.IsChecked = s.Mode == LightMode.Temperature;
            PageStatic.Visibility = Vis(s.Mode == LightMode.Static);
            PageEffect.Visibility = Vis(s.Mode == LightMode.Effect);
            PageTemp.Visibility = Vis(s.Mode == LightMode.Temperature);

            StaticPicker.Color = Rgb.Parse(s.StaticColor);

            FxBreathing.IsChecked = s.Effect == EffectKind.Breathing;
            FxRainbow.IsChecked = s.Effect == EffectKind.Rainbow;
            FxBlink.IsChecked = s.Effect == EffectKind.Blink;
            FxTwoColor.IsChecked = s.Effect == EffectKind.TwoColor;
            bool breathing = s.Effect == EffectKind.Breathing;
            SpeedTitle.Text = breathing ? "Частота дыхания" : "Скорость";
            Speed.Value = breathing ? SliderFromRate(s.BreathRate) : (s.EffectSpeed - 1) / 9.0;
            SpeedValue.Text = breathing ? Breaths(s.BreathRate) : $"{s.EffectSpeed} из 10";
            SpeedHint.Visibility = Vis(breathing);
            FxColorCard.Visibility = Vis(s.Effect != EffectKind.Rainbow);
            FxWhich.Visibility = Vis(s.Effect == EffectKind.TwoColor);
            FxDot1.Fill = Brush(s.EffectColor1);
            FxDot2.Fill = Brush(s.EffectColor2);
            FxColorTitle.Text = s.Effect == EffectKind.TwoColor ? (FxEditingSecond ? "Цвет 2" : "Цвет 1") : "Цвет эффекта";
            FxPicker.Color = Rgb.Parse(FxEditingSecond ? s.EffectColor2 : s.EffectColor1);

            if (!TempFrom.IsKeyboardFocusWithin) TempFrom.Value = s.TempFrom;
            if (!TempTo.IsKeyboardFocusWithin) TempTo.Value = s.TempTo;
            TempDotFrom.Fill = Brush(s.TempColorFrom);
            TempDotTo.Fill = Brush(s.TempColorTo);
            bool editTo = TempEditTo.IsChecked == true;
            TempColorTitle.Text = editTo ? $"Цвет «горячо» ({s.TempTo:0} °C и выше)" : $"Цвет «холодно» ({s.TempFrom:0} °C и ниже)";
            TempPicker.Color = Rgb.Parse(editTo ? s.TempColorTo : s.TempColorFrom);
            TempScale.Background = GradientSlider.Horizontal(ColorPicker.ToWpf(Rgb.Parse(s.TempColorFrom)), ColorPicker.ToWpf(Rgb.Parse(s.TempColorTo)));
            ScaleFrom.Text = $"{s.TempFrom:0} °C";
            ScaleTo.Text = $"{s.TempTo:0} °C";
            SelectSensor();

            TempBreathSwitch.IsChecked = s.TempBreathing;
            TempBreathRates.Visibility = Vis(s.TempBreathing);
            BreathCold.Value = SliderFromRate(s.TempBreathRateCold);
            BreathHot.Value = SliderFromRate(s.TempBreathRateHot);
            BreathColdValue.Text = Breaths(s.TempBreathRateCold);
            BreathHotValue.Text = Breaths(s.TempBreathRateHot);
            BreathColdDot.Fill = Brush(s.TempColorFrom);
            BreathHotDot.Fill = Brush(s.TempColorTo);
            BreathCold.TrackBrush = RateTrack(s.TempColorFrom);
            BreathHot.TrackBrush = RateTrack(s.TempColorTo);
        }
        finally { _loading = false; }
    }

    private static int RateFromSlider(double v)
        => ColorEngine.MinBreathRate + (int)Math.Round(v * (ColorEngine.MaxBreathRate - ColorEngine.MinBreathRate));

    private static double SliderFromRate(int r)
        => (r - ColorEngine.MinBreathRate) / (double)(ColorEngine.MaxBreathRate - ColorEngine.MinBreathRate);

    private static string Breaths(double rate)
    {
        int n = (int)Math.Round(rate);
        string word = (n % 100 is >= 11 and <= 14) ? "вдохов" : (n % 10) switch { 1 => "вдох", 2 or 3 or 4 => "вдоха", _ => "вдохов" };
        return $"{n} {word}/мин";
    }

    private static Brush RateTrack(string hex)
        => GradientSlider.Horizontal(WpfColor.FromRgb(70, 70, 70), ColorPicker.ToWpf(Rgb.Parse(hex)));

    private float? _lastTemp;

    internal void ShowStatus(EngineStatus st)
    {
        var c = ColorPicker.ToWpf(st.Color);
        bool dark = st.Color.R + st.Color.G + st.Color.B < 30;
        Orb.Fill = dark ? (Brush)FindResource("ControlFillColorDefaultBrush") : new SolidColorBrush(c);
        Glow.Fill = new SolidColorBrush(c);
        Glow.Opacity = dark ? 0 : 0.6;
        OffIcon.Visibility = Vis(!_s.Enabled);

        PreviewTitle.Text = !_s.Enabled ? "Выключено" : _s.Mode switch
        {
            LightMode.Static => "Цвет",
            LightMode.Effect => _s.Effect switch
            {
                EffectKind.Breathing => "Дыхание",
                EffectKind.Rainbow => "Радуга",
                EffectKind.Blink => "Мигание",
                _ => "Переливание",
            },
            _ => "По температуре",
        };
        var sub = _s.Enabled ? st.Color.ToString() : "светодиоды погашены";
        if (_s.Mode == LightMode.Temperature && st.Temperature is float t) sub = $"{t:0.0} °C  ·  {sub}";
        PreviewSub.Text = sub;

        ErrorBar.IsOpen = st.Error != null;
        ErrorBar.Message = st.Error ?? "";

        _lastTemp = st.Temperature;
        TempNow.Text = st.Temperature is float tv ? $"{tv:0} °C" : "— °C";
        PlaceMarker(st.Temperature);
        BreathNow.Text = $"Сейчас: {Breaths(ColorEngine.BreathRate(_s, st.Temperature))}. Между порогами частота меняется плавно; " +
                         "на высокой свет разгорается быстрее, чем гаснет.";

        var ids = st.Sensors.Select(x => x.Id).ToList();
        if (!SensorBox.IsDropDownOpen && !ids.SequenceEqual(_sensorIds))
        {
            _sensorIds = ids;
            _loading = true;
            try
            {
                SensorBox.ItemsSource = st.Sensors.ToList();
                SelectSensor();
            }
            finally { _loading = false; }
        }
    }

    private void PlaceMarker(float? t)
    {
        double w = TempMarkerCanvas.ActualWidth;
        TempMarker.Visibility = Vis(t != null && w > 0);
        if (t is not float v || w <= 0) return;
        double f = Math.Clamp((v - _s.TempFrom) / (_s.TempTo - _s.TempFrom), 0, 1);
        Canvas.SetLeft(TempMarker, f * (w - TempMarker.Width));
    }

    private void SelectSensor()
    {
        if (SensorBox.ItemsSource is IEnumerable<SensorInfo> items)
            SensorBox.SelectedItem = items.FirstOrDefault(x => x.Id == _s.SensorId);
    }

    private static Visibility Vis(bool v) => v ? Visibility.Visible : Visibility.Collapsed;
    private static SolidColorBrush Brush(string hex) => new(ColorPicker.ToWpf(Rgb.Parse(hex)));

    internal void CloseForReal()
    {
        _reallyClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // the close button hides to the tray; "Выход" in the tray menu really quits
        if (!_reallyClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
