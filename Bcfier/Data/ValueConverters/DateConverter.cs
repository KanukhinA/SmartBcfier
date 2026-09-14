using System;
using System.Globalization;
using System.Windows.Data;
using Bcfier.Data.Utils;


namespace Bcfier.Data.ValueConverters
{
  /// <summary>
  /// Converts a date to relative
  /// </summary>
  [ValueConversion(typeof(DateTime), typeof(String))]
  public class DateConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value == null)
        return "";
      DateTime date;
      if (value is DateTime dateValue)
        date = dateValue;
      else if (!DateTime.TryParse(value.ToString(), culture ?? CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
        return "";

      if (parameter != null && parameter.ToString() == "relative")
        return RelativeDate.ToRelative(date);

      // Стандартный краткий формат текущего языка: без жёстко заданного английского "at".
      return date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {

      throw new NotImplementedException();
    }


  }
}
