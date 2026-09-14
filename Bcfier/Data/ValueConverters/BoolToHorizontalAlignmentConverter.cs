using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Bcfier.Data.ValueConverters
{
  /// <summary>
  /// Maps true → Right (own chat bubble), false → Left.
  /// </summary>
  [ValueConversion(typeof(bool), typeof(HorizontalAlignment))]
  public class BoolToHorizontalAlignmentConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      bool isMine = value is bool b && b;
      return isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }
  }
}
