using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using WinForms = System.Windows.Forms;

namespace FluentTune;

/// <summary>
/// A centered, click-through OSD that briefly appears when Caps / Num / Scroll Lock is toggled
/// (like the notifications the paid app shows).
/// </summary>
public partial class LockKeyWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static readonly Color CapsColor = Color.FromRgb(0x4C, 0xC2, 0xFF);
    private static readonly Color NumColor = Color.FromRgb(0xB4, 0x7C, 0xFF);
    private static readonly Color ScrollColor = Color.FromRgb(0x4C, 0xD9, 0xA0);
    private static readonly SolidColorBrush OffBrush = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    private bool _caps, _num, _scroll;

    public LockKeyWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));

        // Seed current states so we don't flash a notification at launch.
        _caps = IsLocked(WinForms.Keys.CapsLock);
        _num = IsLocked(WinForms.Keys.NumLock);
        _scroll = IsLocked(WinForms.Keys.Scroll);

        _poll.Tick += Poll;
        _poll.Start();
        _hide.Tick += (_, _) => { _hide.Stop(); FadeOut(); };
    }

    private static bool IsLocked(WinForms.Keys key) => WinForms.Control.IsKeyLocked(key);

    private void Poll(object? sender, EventArgs e)
    {
        bool c = IsLocked(WinForms.Keys.CapsLock);
        if (c != _caps) { _caps = c; Notify(c ? "Mayúsculas activadas" : "Mayúsculas desactivadas", c, CapsColor); return; }

        bool n = IsLocked(WinForms.Keys.NumLock);
        if (n != _num) { _num = n; Notify(n ? "Bloq Num activado" : "Bloq Num desactivado", n, NumColor); return; }

        bool s = IsLocked(WinForms.Keys.Scroll);
        if (s != _scroll) { _scroll = s; Notify(s ? "Bloq Despl activado" : "Bloq Despl desactivado", s, ScrollColor); }
    }

    private void Notify(string text, bool on, Color onColor)
    {
        LockText.Text = text;
        LockIcon.Symbol = on ? SymbolRegular.LockClosed24 : SymbolRegular.LockOpen24;
        IconChip.Background = on ? Frozen(onColor) : OffBrush;

        // Size to the new content, then centre horizontally / place in the upper-middle.
        UpdateLayout();
        CentreOnScreen();

        Show();
        BeginAnimation(OpacityProperty, null);
        RootTransform.BeginAnimation(TranslateTransform.YProperty, null);
        Opacity = 0;
        RootTransform.Y = 14;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        RootTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        _hide.Stop();
        _hide.Start();
    }

    private void FadeOut()
    {
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(260));
        fade.Completed += (_, _) => { if (Opacity < 0.02) Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }

    private void CentreOnScreen()
    {
        var src = PresentationSource.FromVisual(this);
        double dpiX = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        var b = WinForms.Screen.PrimaryScreen!.Bounds;

        Left = (b.Left + b.Width / 2.0) / dpiX - ActualWidth / 2.0;
        Top = (b.Top + b.Height * 0.32) / dpiY - ActualHeight / 2.0; // upper-middle, like an OSD
    }

    protected override void OnClosed(EventArgs e)
    {
        _poll.Stop();
        _hide.Stop();
        base.OnClosed(e);
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
