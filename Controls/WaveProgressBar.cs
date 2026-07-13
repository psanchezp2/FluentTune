using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FluentTune.Controls;

/// <summary>
/// Android-style "squiggly" progress bar: the played portion is an animated sine wave in the
/// accent colour, the remaining portion a faint straight line, with a draggable thumb. The wave
/// flattens when paused and scrolls while playing.
/// </summary>
public sealed class WaveProgressBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(WaveProgressBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(WaveProgressBar),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(WaveProgressBar),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(WaveProgressBar),
        new FrameworkPropertyMetadata(false));

    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    /// <summary>User started interacting (grabbed the bar).</summary>
    public event Action? Interacting;

    /// <summary>User released — the value (in the same units as Maximum) to seek to.</summary>
    public event Action<double>? Seek;

    /// <summary>Optional live audio level (0..1-ish RMS) — makes the wave pulse with the music.</summary>
    public Func<float>? LevelProvider { get; set; }

    private double _phase;
    private double _amp;
    private float _levelAgc = 1e-4f;
    private bool _dragging;
    private double _dragFrac;

    public WaveProgressBar()
    {
        Height = 24;
        Loaded += (_, _) => CompositionTarget.Rendering += OnFrame;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (!IsVisible) return;

        // Reactive amplitude: auto-gain the live audio level so the wave swells on loud
        // passages and calms on quiet ones. Falls back to a steady swell if no provider.
        double reactive;
        if (LevelProvider is not null)
        {
            float level = LevelProvider();
            _levelAgc = Math.Max(level, _levelAgc * 0.99f);
            if (_levelAgc < 1e-4f) _levelAgc = 1e-4f;
            double norm = Math.Clamp(level / _levelAgc, 0, 1);
            reactive = 0.8 + 4.4 * norm;
        }
        else
        {
            reactive = 4.0;
        }

        double targetAmp = IsActive ? reactive : 0.0;
        _amp += (targetAmp - _amp) * 0.18;
        if (IsActive) _phase += 0.12; // gentle rolling motion
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragging = true;
        CaptureMouse();
        _dragFrac = Frac(e.GetPosition(this).X);
        Interacting?.Invoke();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        _dragFrac = Frac(e.GetPosition(this).X);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        Seek?.Invoke(Frac(e.GetPosition(this).X) * Maximum);
    }

    private double Frac(double x) => ActualWidth <= 0 ? 0 : Math.Clamp(x / ActualWidth, 0, 1);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, midY = h / 2;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // hit area
        if (w <= 1) return;

        double frac = _dragging ? _dragFrac : (Maximum > 0 ? Math.Clamp(Value / Maximum, 0, 1) : 0);
        double played = frac * w;

        var playedPen = new Pen(Fill, 3.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        playedPen.Freeze();
        var remainPen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 3.0)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        remainPen.Freeze();

        if (played > 1)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, midY + WaveY(0, played)), false, false);
                for (double x = 1.5; x <= played; x += 1.5)
                    ctx.LineTo(new Point(x, midY + WaveY(x, played)), true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(null, playedPen, geo);
        }

        if (played < w - 1)
            dc.DrawLine(remainPen, new Point(Math.Min(played + 2, w), midY), new Point(w, midY));

        dc.DrawEllipse(Brushes.White, null, new Point(Math.Clamp(played, 3, w - 3), midY), 5, 5);
    }

    private double WaveY(double x, double played)
    {
        // Taper amplitude to zero at both ends so the wave meets the flat segments smoothly.
        const double edge = 16;
        double env = Math.Clamp(x / edge, 0, 1) * Math.Clamp((played - x) / edge, 0, 1);

        // A broad ocean swell plus a faint secondary ripple => smooth, organic "sea wave".
        double swell = Math.Sin(2 * Math.PI * x / 38.0 + _phase);
        double ripple = 0.28 * Math.Sin(2 * Math.PI * x / 17.0 + _phase * 1.5);
        return _amp * env * (swell + ripple);
    }
}
