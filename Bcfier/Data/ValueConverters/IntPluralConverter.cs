using System;
using System.Globalization;
using System.Windows.Data;
using Bcfier.Localization;

namespace Bcfier.Data.ValueConverters
{
  /// <summary>
  /// Счётчик с локализованным склонением.
  /// ConverterParameter — префикс ключей Loc (Viewpoints → CountViewpoints_one/few/many).
  /// </summary>
  [ValueConversion(typeof(Int16), typeof(String))]
  public class IntPluralConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      int count = value is int i ? i : System.Convert.ToInt32(value);
      string prefix = parameter?.ToString() ?? "CountIssues";
      if (!prefix.StartsWith("Count", StringComparison.Ordinal))
        prefix = "Count" + prefix;

      return Loc.Plural(prefix, count);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }
  }
}
