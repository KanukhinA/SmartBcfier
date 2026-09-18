using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Bcfier.Data.ValueConverters
{
  /// <summary>
  /// Имя элемента списка (статус/тип/приоритет/метка) → кисть цвета из настроек.
  /// ConverterParameter:
  ///   null / "bg" — фон статуса;
  ///   "fg" — текст на статусе;
  ///   "type" / "type:fg", "priority" / "priority:fg", "label" / "label:fg".
  /// </summary>
  [ValueConversion(typeof(string), typeof(Brush))]
  public class StatusToBrushConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      string name = value as string;
      ParseParameter(parameter as string, out string listKind, out bool foreground);
      string hex = Globals.GetListColor(listKind, name);
      if (foreground)
        return TopicStatusListCodec.ContrastingForeground(hex);
      return TopicStatusListCodec.ToBrush(hex);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }

    private static void ParseParameter(string parameter, out string listKind, out bool foreground)
    {
      listKind = "status";
      foreground = false;
      if (string.IsNullOrWhiteSpace(parameter))
        return;

      string[] parts = parameter.Split(new[] { ':', ';' }, StringSplitOptions.RemoveEmptyEntries);
      foreach (string part in parts)
      {
        string token = part.Trim();
        if (string.Equals(token, "fg", StringComparison.OrdinalIgnoreCase))
          foreground = true;
        else if (string.Equals(token, "bg", StringComparison.OrdinalIgnoreCase))
          foreground = false;
        else if (string.Equals(token, "type", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(token, "priority", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(token, "label", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(token, "status", StringComparison.OrdinalIgnoreCase))
          listKind = token.ToLowerInvariant();
      }
    }
  }
}
