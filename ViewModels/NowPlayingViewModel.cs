using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FluentTune.ViewModels;

public partial class NowPlayingViewModel : ObservableObject
{
    [ObservableProperty]
    private SolidColorBrush _accentBrush = new(Color.FromRgb(0x4C, 0xC2, 0xFF));

    [ObservableProperty]
    private Brush _accentForeground = Brushes.White;

    [ObservableProperty]
    private Brush _glowBrush = Brushes.Transparent;

    [ObservableProperty]
    private string _title = "Nada sonando";

    [ObservableProperty]
    private string _artist = "Reproduce algo para empezar";

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _hasMedia;

    [ObservableProperty]
    private BitmapImage? _thumbnail;

    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _durationSeconds;

    [ObservableProperty]
    private string _positionText = "0:00";

    [ObservableProperty]
    private string _durationText = "0:00";

    [ObservableProperty]
    private bool _canSeek;
}
