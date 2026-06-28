using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using Ultraudio.Services;

namespace Ultraudio.Views.Controls;

/// <summary>
/// Custom Avalonia control that draws a real-time FFT spectrum visualizer.
/// 64 bars with logarithmic bin grouping, green→teal gradient, and peak-hold dots.
/// Runs at 60fps via DispatcherTimer.
/// </summary>
public class SpectrumVisualizerControl : Control
{
    private SpectrumAnalyzer? _analyzer;
    private DispatcherTimer? _timer;

    // ── Visual config ─────────────────────────────────────────────────────────
    private static readonly Color ColorLow  = Color.FromRgb(0x00, 0xE5, 0x76); // Green
    private static readonly Color ColorMid  = Color.FromRgb(0x00, 0xC8, 0xC8); // Teal
    private static readonly Color ColorHigh = Color.FromRgb(0x00, 0x8B, 0xFF); // Blue
    private static readonly Color ColorPeak = Colors.White;

    private const double BarGap = 2.0;
    private const double CornerRadius = 2.0;
    private const double PeakDotHeight = 2.0;

    private float[]? _bars;
    private float[]? _peaks;

    // ── Styled properties ─────────────────────────────────────────────────────

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<SpectrumVisualizerControl, bool>(nameof(IsActive), defaultValue: true);

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Initialize(SpectrumAnalyzer analyzer)
    {
        _analyzer = analyzer;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) }; // ~60fps
        _timer.Tick += (_, _) =>
        {
            if (IsActive && _analyzer != null)
            {
                (_bars, _peaks) = _analyzer.Update();
                InvalidateVisual();
            }
        };
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _bars = null;
        _peaks = null;
        InvalidateVisual();
    }

    public void Resume()
    {
        _timer?.Start();
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width  = Bounds.Width;
        double height = Bounds.Height;

        if (width <= 0 || height <= 0) return;

        // Background
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)),
            new Rect(0, 0, width, height));

        if (_bars == null || _bars.Length == 0)
        {
            DrawPlaceholder(context, width, height);
            return;
        }

        int barCount = _bars.Length;
        double totalGap = BarGap * (barCount - 1);
        double barWidth = Math.Max(1, (width - totalGap) / barCount);

        for (int i = 0; i < barCount; i++)
        {
            double x = i * (barWidth + BarGap);
            float val = Math.Clamp(_bars[i], 0f, 1f);
            double barHeight = val * (height - 6);

            if (barHeight < 1) barHeight = 1;

            // ── Gradient color based on height ────────────────────────────
            Color barColor = LerpColor(
                LerpColor(ColorLow, ColorMid, val * 2),
                ColorHigh,
                Math.Max(0, val * 2 - 1));

            var rect = new Rect(x, height - barHeight, barWidth, barHeight);

            // Rounded top corners
            context.FillRectangle(new SolidColorBrush(barColor), rect, (float)CornerRadius);

            // ── Peak hold dot ──────────────────────────────────────────────
            if (_peaks != null && i < _peaks.Length)
            {
                float peak = _peaks[i];
                if (peak > 0.01f)
                {
                    double peakY = height - peak * (height - 6) - PeakDotHeight - 2;
                    var peakRect = new Rect(x, peakY, barWidth, PeakDotHeight);
                    context.FillRectangle(new SolidColorBrush(ColorPeak), peakRect);
                }
            }
        }
    }

    private static void DrawPlaceholder(DrawingContext ctx, double width, double height)
    {
        // Draw a flat idle line
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0xE5, 0x76)), 1.5);
        ctx.DrawLine(pen, new Point(0, height / 2), new Point(width, height / 2));
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
