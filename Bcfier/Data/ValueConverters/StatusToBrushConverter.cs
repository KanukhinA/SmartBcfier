using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Bcfier.Data.ValueConverters
{
  /// <summary>
  /// Преобразует имя статуса в кисть цвета из настроек.
  /// ConverterParameter=fg — контрастный цвет текста.
  /// </summary>
  [ValueConversion(typeof(string), typeof(Brush))]
  public class StatusToBrushConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      string status = value as string;
      string hex = Globals.GetStatusColor(status);
      string mode = parameter as string;
      if (string.Equals(mode, "fg", StringComparison.OrdinalIgnoreCase))
        return TopicStatusListCodec.ContrastingForeground(hex);
      return TopicStatusListCodec.ToBrush(hex);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }
  }
}
