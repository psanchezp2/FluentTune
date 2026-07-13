using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace FluentTune.Converters;

/// <summary>true (playing) -> Pause icon, false -> Play icon.</summary>
public sealed class PlayPauseSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? SymbolRegular.Pause24 : SymbolRegular.Play24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
