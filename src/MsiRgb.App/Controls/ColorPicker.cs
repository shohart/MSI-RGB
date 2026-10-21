using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace MsiRgb.Controls;

/// <summary>
/// LED colour picker: preset swatches, hue and saturation sliders, #HEX field.
/// No "value" slider on purpose: brightness is a separate global control, and dark colours on an LED are just dim.
/// </summary>
internal sealed class ColorPicker : StackPanel
{
    private static readonly string[] Presets =
    {
        "#FF0000", "#FF4000", "#FF8000", "#FFC000", "#FFFF00", "#80FF00", "#00FF00", "#00FF80",
        "#00FFFF", "#0080FF", "#0040FF", "#4000FF", "#8000FF", "#FF00FF", "#FF0080", "#FFFFFF",
    };

    private readonly GradientSlider _hue = new() { Step = 1 / 72.0 };
    private readonly GradientSlider _sat = new();
    private readonly Wpf.Ui.Controls.TextBox _hex = new() { Width = 120, MaxLength = 7, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
    private readonly WrapPanel _swatches = new() { Margin = new Thickness(0, 0, 0, 8) };
    private float _h, _s = 1;
    private bool _updating;

    public event Action<Rgb>? ColorChanged;

    public ColorPicker()
    {
        foreach (var p in Presets)
        {
            var c = Rgb.Parse(p);
            var b = new Button
            {
                Width = 28, Height = 28, Margin = new Thickness(0, 0, 5, 6), Padding = new Thickness(0),
                Cursor = Cursors.Hand, ToolTip = p, Focusable = true,
                Template = SwatchTemplate(), Background = new SolidColorBrush(ToWpf(c)),
            };
            b.Click += (_, _) => SetAndRaise(c);
            _swatches.Children.Add(b);
        }
        Children.Add(_swatches);

        Children.Add(Label("Оттенок"));
        _hue.TrackBrush = GradientSlider.Horizontal(
            WpfColor.FromRgb(255, 0, 0), WpfColor.FromRgb(255, 255, 0), WpfColor.FromRgb(0, 255, 0),
            WpfColor.FromRgb(0, 255, 255), WpfColor.FromRgb(0, 0, 255), WpfColor.FromRgb(255, 0, 255), WpfColor.FromRgb(255, 0, 0));
        _hue.UserChanged += v => { _h = (float)(v * 360); FromSliders(); };
        Children.Add(_hue);

        Children.Add(Label("Насыщенность"));
        _sat.UserChanged += v => { _s = (float)v; FromSliders(); };
        Children.Add(_sat);

        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        hexRow.Children.Add(new TextBlock { Text = "HEX", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Opacity = 0.8 });
        hexRow.Children.Add(_hex);
        _hex.LostKeyboardFocus += (_, _) => FromHex();
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) FromHex(); };
        Children.Add(hexRow);

        Color = Rgb.Parse("#0040FF");
    }

    public Rgb Color
    {
        get => Rgb.FromHsv(_h, _s, 1);
        set
        {
            var (h, s, _) = value.ToHsv();
            // keep the hue when the user drags saturation to 0 (white has no hue)
            if (s > 0.001f) _h = h;
            _s = s;
            Sync();
        }
    }

    private void SetAndRaise(Rgb c)
    {
        Color = c;
        ColorChanged?.Invoke(Color);
    }

    private void FromSliders()
    {
        Sync();
        ColorChanged?.Invoke(Color);
    }

    private void FromHex()
    {
        if (_updating) return;
        try { SetAndRaise(Rgb.Parse(_hex.Text)); }
        catch (FormatException) { Sync(); }
    }

    private void Sync()
    {
        _updating = true;
        try
        {
            var pure = ToWpf(Rgb.FromHsv(_h, 1, 1));
            var current = ToWpf(Color);
            _hue.Value = _h / 360.0;
            _hue.ThumbFill = new SolidColorBrush(pure);
            _sat.Value = _s;
            _sat.TrackBrush = GradientSlider.Horizontal(Colors.White, pure);
            _sat.ThumbFill = new SolidColorBrush(current);
            _hex.Text = Color.ToString();
        }
        finally { _updating = false; }
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text, Margin = new Thickness(0, 6, 0, 4), Opacity = 0.8,
    };

    internal static WpfColor ToWpf(Rgb c) => WpfColor.FromRgb(c.R, c.G, c.B);

    /// <summary>Round swatch with a hover/focus ring.</summary>
    internal static ControlTemplate SwatchTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var ring = new FrameworkElementFactory(typeof(Border));
        ring.Name = "ring";
        ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        ring.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        ring.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        ring.SetValue(Border.PaddingProperty, new Thickness(2));
        var dot = new FrameworkElementFactory(typeof(Border));
        dot.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        dot.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        dot.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        dot.SetValue(Border.BorderBrushProperty, new SolidColorBrush(WpfColor.FromArgb(70, 128, 128, 128)));
        ring.AppendChild(dot);
        t.VisualTree = ring;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("TextFillColorSecondaryBrush"), "ring"));
        t.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentFillColorDefaultBrush"), "ring"));
        t.Triggers.Add(focus);
        return t;
    }
}
