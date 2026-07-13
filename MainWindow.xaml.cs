using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FluentTune.Services;
using FluentTune.ViewModels;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace FluentTune;

public partial class MainWindow : Window
{
    private readonly MediaService _media = new();
    private readonly VolumeService _volume = new();
    private readonly AudioSpectrumService _spectrumService = new();
    private readonly NowPlayingViewModel _vm = new();

    private SpectrumWindow? _spectrum;

    private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private WinForms.NotifyIcon? _tray;

    // Baseline for interpolating the playback position between updates.
    private TimeSpan _basePosition;
    private TimeSpan _baseDuration;
    private DateTime _baseTimestamp;
    private bool _isPlaying;
    private bool _canSeek;

    private bool _userSeeking;
    private bool _suppressVolume;
    private bool _reallyExit;

    private static readonly Color DefaultAccent = Color.FromRgb(0x4C, 0xC2, 0xFF);
    private Color _accent = DefaultAccent;

    private bool _shownOnce;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        Wave.Interacting += () => { _userSeeking = true; _hideTimer.Stop(); };
        Wave.Seek += async seconds =>
        {
            _userSeeking = false;
            RestartHideCountdown();
            if (_canSeek) await _media.SeekAsync(TimeSpan.FromSeconds(seconds));
        };
        Wave.LevelProvider = () => _spectrumService.GetLevel(); // wave pulses with the music

        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;
        Closing += (_, e) =>
        {
            if (!_reallyExit) { e.Cancel = true; HideWidget(); }
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetupTray();

        _positionTimer.Tick += (_, _) => UpdateInterpolatedPosition();
        _positionTimer.Start();
        _hideTimer.Tick += OnHideTick;

        MouseEnter += (_, _) => _hideTimer.Stop();
        MouseLeave += (_, _) => RestartHideCountdown();

        // Volume
        VolumeSlider.IsEnabled = _volume.IsAvailable;
        _volume.VolumeChanged += v => Dispatcher.Invoke(() => SetVolumeSlider(v));
        SetVolumeSlider(_volume.GetVolume());

        // Taskbar spectrum overlay + mini now-playing (left side of the taskbar)
        _spectrumService.Start();
        _spectrum = new SpectrumWindow(_spectrumService, _vm);
        _spectrum.WidgetClicked += ToggleWidget;   // click the taskbar widget to open/close the flyout
        _spectrum.Show();
        _spectrum.SetAccent(_accent);

        // Media
        _media.NowPlayingChanged += OnNowPlaying;
        _media.TimelineChanged += OnTimeline;
        await _media.InitializeAsync();
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (_shownOnce) return;
        _shownOnce = true;
        ShowWidget(); // initial peek so the user sees it come up
    }

    // ---------- Media ----------

    private void OnNowPlaying(NowPlayingInfo info)
    {
        // GSMTC events arrive on a background thread — marshal to the UI.
        Dispatcher.Invoke(() =>
        {
            _vm.HasMedia = info.HasMedia;
            _vm.Title = info.HasMedia ? info.Title : "Nada sonando";
            _vm.Artist = info.HasMedia ? info.Artist : "Reproduce algo para empezar";
            _vm.IsPlaying = info.IsPlaying;
            _vm.Thumbnail = info.Thumbnail;
            _vm.CanSeek = info.CanSeek;

            ApplyAccent(info.HasMedia
                ? ColorExtractor.GetAccent(info.Thumbnail, DefaultAccent)
                : DefaultAccent);

            _spectrum?.SetActive(info.HasMedia);

            SetTimelineBase(info.Position, info.Duration, info.IsPlaying, info.CanSeek);
            AnimateArt();
            // The flyout is opened manually (click the taskbar widget) — it no longer
            // auto-pops on every track change.
        });
    }

    private void OnTimeline(TimelineInfo t)
    {
        Dispatcher.Invoke(() =>
        {
            _vm.CanSeek = t.CanSeek;
            SetTimelineBase(t.Position, t.Duration, t.IsPlaying, t.CanSeek);
        });
    }

    private void SetTimelineBase(TimeSpan pos, TimeSpan dur, bool playing, bool canSeek)
    {
        _basePosition = pos;
        _baseDuration = dur;
        _baseTimestamp = DateTime.UtcNow;
        _isPlaying = playing;
        _canSeek = canSeek;
        _vm.DurationSeconds = dur.TotalSeconds;
        _vm.DurationText = Fmt(dur);
        if (!_userSeeking) UpdateInterpolatedPosition();
    }

    private void UpdateInterpolatedPosition()
    {
        if (_userSeeking) return;
        var pos = _basePosition;
        if (_isPlaying) pos += DateTime.UtcNow - _baseTimestamp;
        if (_baseDuration > TimeSpan.Zero && pos > _baseDuration) pos = _baseDuration;
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        _vm.PositionSeconds = pos.TotalSeconds;
        _vm.PositionText = Fmt(pos);
    }

    private static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    // ---------- Transport ----------

    private async void Previous_Click(object sender, RoutedEventArgs e) { RestartHideCountdown(); await _media.PreviousAsync(); }
    private async void PlayPauseBorder_Click(object sender, MouseButtonEventArgs e) { RestartHideCountdown(); await _media.TogglePlayPauseAsync(); }
    private async void Next_Click(object sender, RoutedEventArgs e) { RestartHideCountdown(); await _media.NextAsync(); }

    // ---------- Volume ----------

    private void SetVolumeSlider(float scalar)
    {
        _suppressVolume = true;
        VolumeSlider.Value = Math.Clamp(scalar, 0f, 1f);
        _suppressVolume = false;
    }

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressVolume) return;
        _volume.SetVolume((float)e.NewValue);
        RestartHideCountdown();
    }

    // ---------- Show / hide ----------

    private void ShowWidget()
    {
        PositionAboveTaskbar();
        Show();
        Visibility = Visibility.Visible;

        BeginAnimation(OpacityProperty, null);
        RootTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        RootTransform.Y = 48;
        Opacity = 0;

        var slide = new DoubleAnimation(48, 0, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360));

        RootTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
        BeginAnimation(OpacityProperty, fade);

        RestartHideCountdown();
    }

    private void HideWidget()
    {
        var slide = new DoubleAnimation(0, 48, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(300));
        fade.Completed += (_, _) => { if (Opacity < 0.02) Hide(); };

        RootTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
        BeginAnimation(OpacityProperty, fade);
    }

    private void RestartHideCountdown() { _hideTimer.Stop(); _hideTimer.Start(); }

    private void OnHideTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        if (!IsMouseOver && !_userSeeking) HideWidget();
    }

    private void ToggleWidget()
    {
        if (IsVisible && Opacity > 0.5) HideWidget();
        else ShowWidget();
    }

    private void PositionAboveTaskbar()
    {
        // Bottom-left, just above the taskbar. The card sits ~24px inside the window
        // (transparent margin left for the drop shadow), so nudge left to hug the edge.
        var wa = SystemParameters.WorkArea;
        var h = ActualHeight > 0 ? ActualHeight : 200;
        Left = wa.Left - 8;
        Top = wa.Bottom - h + 16;
    }

    private void AnimateArt()
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        ArtBorder.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Recolour the accent brush + glow to match the album artwork.</summary>
    private void ApplyAccent(Color accent)
    {
        _accent = accent;

        var target = new SolidColorBrush(accent);
        target.Freeze();
        _vm.AccentBrush = target;

        // Dark icon on a light accent, white icon on a dark one.
        double luminance = 0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B;
        _vm.AccentForeground = luminance > 150 ? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)) : Brushes.White;

        // Colour bleeds in from behind the album art (left) and fades across the card.
        var glow = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.12, 0.4),
            Center = new Point(0.12, 0.4),
            RadiusX = 1.4,
            RadiusY = 1.6,
        };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x8C, accent.R, accent.G, accent.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, accent.R, accent.G, accent.B), 0.5));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1));
        glow.Freeze();
        _vm.GlowBrush = glow;

        _spectrum?.SetAccent(accent);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => HideWidget();

    // ---------- Tray ----------

    private void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Visible = true,
            Text = "FluentTune",
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Mostrar", null, (_, _) => ShowWidget());

        var startup = new WinForms.ToolStripMenuItem("Iniciar con Windows")
        {
            Checked = StartupService.IsEnabled(),
            CheckOnClick = true,
        };
        startup.CheckedChanged += (s, _) => StartupService.SetEnabled(((WinForms.ToolStripMenuItem)s!).Checked);
        menu.Items.Add(startup);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitApp());

        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) ToggleWidget(); };
    }

    private static Drawing.Icon CreateTrayIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x4C, 0xC2, 0xFF));
            using var font = new Drawing.Font("Segoe UI Symbol", 22, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            g.DrawString("♪", font, brush, new Drawing.PointF(6, 2));
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private void ExitApp()
    {
        _reallyExit = true;
        _hideTimer.Stop();
        _positionTimer.Stop();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _spectrum?.Close();
        _spectrumService.Dispose();
        _volume.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}
