using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using FluentTune.Services;
using FluentTune.ViewModels;
using WinForms = System.Windows.Forms;

namespace FluentTune;

/// <summary>
/// A pinned overlay on the LEFT of the taskbar: mini now-playing (art + title) plus a modern,
/// glowing, colour-graded audio spectrum. Clicking it toggles the main flyout.
/// </summary>
public partial class SpectrumWindow : Window
{
    private const int BarCount = 14;

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_LAYERED = 0x80000;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;

    // The shell taskbar owns mouse input in its band, so a normal click on our overlay is
    // swallowed by it. A global low-level mouse hook lets us detect clicks in our rectangle.
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? lpModuleName);
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    private IntPtr _mouseHook;
    private LowLevelMouseProc? _mouseProc;
    private int _rectL, _rectT, _rectR, _rectB; // widget bounds in physical pixels

    private readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private IntPtr _hwnd;

    private const int Half = (BarCount + 1) / 2;   // bands mirrored around the centre

    private readonly AudioSpectrumService _spectrum;
    private Rectangle[] _bars = Array.Empty<Rectangle>();
    private readonly float[] _band = new float[Half];
    private readonly float[] _smooth = new float[Half];
    private float _agc = 1e-4f;
    private Color _accent = Color.FromRgb(0x4C, 0xC2, 0xFF);

    /// <summary>Bounds of the widget in physical pixels (for click-outside detection).</summary>
    public (int L, int T, int R, int B) WidgetBounds => (_rectL, _rectT, _rectR, _rectB);

    /// <summary>Raised when the user clicks the taskbar widget.</summary>
    public event Action? WidgetClicked;

    public SpectrumWindow(AudioSpectrumService spectrum, NowPlayingViewModel vm)
    {
        _spectrum = spectrum;
        InitializeComponent();
        DataContext = vm;
        SetAccent(_accent);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        MakeOverlay();
        PositionOnTaskbarLeft();
        BuildBars();
        CompositionTarget.Rendering += OnRendering;

        ReassertTopmost();
        _topmostTimer.Tick += (_, _) => ReassertTopmost();
        _topmostTimer.Start();

        _mouseProc = MouseHookCallback;
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WM_LBUTTONDOWN && IsVisible)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (data.pt.X >= _rectL && data.pt.X <= _rectR && data.pt.Y >= _rectT && data.pt.Y <= _rectB)
                Dispatcher.BeginInvoke(new Action(() => WidgetClicked?.Invoke()));
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void MakeOverlay()
    {
        // Layered + no-activate tool window (stays out of Alt+Tab, never steals focus), but
        // NOT click-through — we want to receive the click that opens the flyout.
        long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    private void ReassertTopmost()
    {
        if (_hwnd != IntPtr.Zero && IsVisible)
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    private void PositionOnTaskbarLeft()
    {
        var src = PresentationSource.FromVisual(this);
        double dpiX = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        var bounds = WinForms.Screen.PrimaryScreen!.Bounds;
        var work = WinForms.Screen.PrimaryScreen!.WorkingArea;

        double tbTopPx = work.Bottom;
        double tbHeightPx = bounds.Bottom - work.Bottom;
        if (tbHeightPx < 10)
        {
            tbHeightPx = 48 * dpiY;
            tbTopPx = bounds.Bottom - tbHeightPx;
        }

        Height = tbHeightPx / dpiY;
        Left = bounds.Left / dpiX + 60;   // clear the left-pinned icons, sit in the empty zone
        Top = tbTopPx / dpiY;

        // Physical-pixel bounds for the global mouse hook hit-test.
        _rectL = (int)Math.Round(Left * dpiX);
        _rectT = (int)Math.Round(Top * dpiY);
        _rectR = (int)Math.Round((Left + Width) * dpiX);
        _rectB = (int)Math.Round((Top + Height) * dpiY);
    }

    private void BuildBars()
    {
        BarsCanvas.Children.Clear();
        double slot = BarsCanvas.Width / BarCount;
        double barW = slot * 0.55;

        _bars = new Rectangle[BarCount];
        for (int i = 0; i < BarCount; i++)
        {
            var r = new Rectangle
            {
                Width = barW,
                RadiusX = barW / 2,
                RadiusY = barW / 2,
                Height = 0,
            };
            Canvas.SetLeft(r, i * slot + (slot - barW) / 2);
            Canvas.SetTop(r, BarsCanvas.Height / 2);
            BarsCanvas.Children.Add(r);
            _bars[i] = r;
        }

        ApplyBarColors();
    }

    public void SetAccent(Color accent)
    {
        _accent = accent;
        ApplyBarColors();
        // Soft coloured glow => the modern "diffused" look.
        BarsCanvas.Effect = new DropShadowEffect
        {
            Color = accent,
            ShadowDepth = 0,
            BlurRadius = 9,
            Opacity = 0.8,
        };
    }

    /// <summary>Symmetric colour: bright at the centre, deeper toward the edges (matches the mirror).</summary>
    private void ApplyBarColors()
    {
        if (_bars.Length == 0) return;

        Color bright = Lighten(_accent, 55);
        Color deep = Deepen(_accent);
        double centre = (_bars.Length - 1) / 2.0;

        for (int i = 0; i < _bars.Length; i++)
        {
            double dc = centre <= 0 ? 0 : Math.Abs(i - centre) / centre; // 0 = centre, 1 = edge
            Color baseC = Lerp(bright, deep, dc);

            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            g.GradientStops.Add(new GradientStop(Lighten(baseC, 45), 0));
            g.GradientStops.Add(new GradientStop(baseC, 1));
            g.Freeze();
            _bars[i].Fill = g;
        }
    }

    public void SetActive(bool active)
    {
        if (active && !IsVisible) Show();
        else if (!active && IsVisible) Hide();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_spectrum.IsRunning || _bars.Length == 0) return;

        // Only half as many bands — they get mirrored, so bass sits in the centre.
        _spectrum.GetBars(_band);

        float max = 0;
        for (int i = 0; i < Half; i++)
            if (_band[i] > max) max = _band[i];

        _agc = Math.Max(max, _agc * 0.985f);
        if (_agc < 1e-4f) _agc = 1e-4f;

        bool quiet = max < 0.006f;

        for (int i = 0; i < Half; i++)
        {
            float target = quiet ? 0f : Math.Clamp(_band[i] / _agc, 0f, 1f);
            float rate = target > _smooth[i] ? 0.5f : 0.08f; // fast attack, slow release
            _smooth[i] += (target - _smooth[i]) * rate;
        }

        double h = BarsCanvas.ActualHeight > 0 ? BarsCanvas.ActualHeight : BarsCanvas.Height;
        int center = BarCount / 2;

        // Mirror: band 0 (bass) lands on the two centre bars, higher bands fan out to the edges.
        for (int j = 0; j < Half; j++)
        {
            double bh = _smooth[j] < 0.012 ? 0 : 3 + _smooth[j] * (h - 4);
            SetBar(center + j, bh, h);       // right side
            SetBar(center - 1 - j, bh, h);   // left side
        }
    }

    private void SetBar(int index, double barHeight, double canvasHeight)
    {
        if (index < 0 || index >= _bars.Length) return;
        _bars[index].Height = barHeight;
        Canvas.SetTop(_bars[index], (canvasHeight - barHeight) / 2); // grow from the centre line
    }

    protected override void OnClosed(EventArgs e)
    {
        _topmostTimer.Stop();
        CompositionTarget.Rendering -= OnRendering;
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        base.OnClosed(e);
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private static Color Lighten(Color c, int amt) => Color.FromRgb(Clamp(c.R + amt), Clamp(c.G + amt), Clamp(c.B + amt));
    private static Color Deepen(Color c) => Color.FromRgb((byte)(c.R * 0.6), (byte)(c.G * 0.6), (byte)(c.B * 0.75));
    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);
}
