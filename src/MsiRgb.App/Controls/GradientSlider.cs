using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MsiRgb.Controls;

/// <summary>
/// Fluent-style slider whose track is any brush (hue rainbow, white→colour, dim→bright).
/// Value is 0..1; drag, click, wheel and arrow keys.
/// </summary>
internal sealed class GradientSlider : FrameworkElement
{
    private const double TrackHeight = 14;
    private const double ThumbSize = 24;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(GradientSlider),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, null, (_, v) => Math.Clamp((double)v, 0, 1)));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(GradientSlider),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThumbFillProperty = DependencyProperty.Register(
        nameof(ThumbFill), typeof(Brush), typeof(GradientSlider),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Raised only for user input, not when Value is set from code.</summary>
    public event Action<double>? UserChanged;

    public GradientSlider()
    {
        Focusable = true;
        Height = ThumbSize + 4;
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
    }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush ThumbFill { get => (Brush)GetValue(ThumbFillProperty); set => SetValue(ThumbFillProperty, value); }

    /// <summary>Keyboard / wheel step.</summary>
    public double Step { get; set; } = 0.02;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        double left = ThumbSize / 2, right = w - ThumbSize / 2;
        var track = new Rect(left - TrackHeight / 2, (h - TrackHeight) / 2, Math.Max(0, right - left + TrackHeight), TrackHeight);
        var stroke = new Pen(TryBrush("ControlStrokeColorDefaultBrush", Brushes.Gray), 1);
        dc.DrawRoundedRectangle(TrackBrush, stroke, track, TrackHeight / 2, TrackHeight / 2);

        double x = left + (right - left) * Value;
        var c = new Point(x, h / 2);
        // soft shadow + white ring + fill: readable on any track colour
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), null, new Point(x, h / 2 + 1), ThumbSize / 2 + 1, ThumbSize / 2 + 1);
        dc.DrawEllipse(Brushes.White, null, c, ThumbSize / 2, ThumbSize / 2);
        dc.DrawEllipse(ThumbFill, null, c, ThumbSize / 2 - 4, ThumbSize / 2 - 4);
        if (IsKeyboardFocused)
            dc.DrawEllipse(null, new Pen(TryBrush("AccentFillColorDefaultBrush", Brushes.DodgerBlue), 2), c, ThumbSize / 2 + 3, ThumbSize / 2 + 3);
    }

    private Brush TryBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private void SetFromPoint(Point p)
    {
        double left = ThumbSize / 2, right = ActualWidth - ThumbSize / 2;
        if (right <= left) return;
        Commit((p.X - left) / (right - left));
    }

    private void Commit(double v)
    {
        v = Math.Clamp(v, 0, 1);
        if (Math.Abs(v - Value) < 1e-6) return;
        Value = v;
        UserChanged?.Invoke(v);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        CaptureMouse();
        SetFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) SetFromPoint(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => ReleaseMouseCapture();

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Commit(Value + Math.Sign(e.Delta) * Step);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Down) { Commit(Value - Step); e.Handled = true; }
        else if (e.Key is Key.Right or Key.Up) { Commit(Value + Step); e.Handled = true; }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) => InvalidateVisual();
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) => InvalidateVisual();

    public static LinearGradientBrush Horizontal(params Color[] colors)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int i = 0; i < colors.Length; i++) b.GradientStops.Add(new GradientStop(colors[i], colors.Length == 1 ? 0 : (double)i / (colors.Length - 1)));
        b.Freeze();
        return b;
    }
}
