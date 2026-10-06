using System.Globalization;
using Microsoft.UI.Xaml.Data;

namespace NeoShell.Themes;

/// <summary>For themes that set their headings in capitals.</summary>
internal sealed partial class UpperCaseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        (value as string)?.ToUpper(CultureInfo.CurrentCulture) ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
