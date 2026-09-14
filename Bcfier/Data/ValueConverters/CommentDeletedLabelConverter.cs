using System;
using System.Globalization;
using System.Windows.Data;
using Bcfier.Localization;

namespace Bcfier.Data.ValueConverters
{
  /// <summary>
  /// Formats the soft-deleted comment tombstone label from Author.
  /// </summary>
  [ValueConversion(typeof(string), typeof(string))]
  public class CommentDeletedLabelConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      string name = value as string ?? string.Empty;
      return Loc.Format("CommentDeletedFormat", name);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }
  }
}
