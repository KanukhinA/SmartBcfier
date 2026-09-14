using System;
using System.Collections;
using System.Globalization;
using System.Windows.Data;
using Bcfier.Bcf.Bcf2;

namespace Bcfier.Data.ValueConverters
{
  /// <summary>true, если хотя бы один viewpoint замечания содержит координаты камеры (Perspective/Orthogonal).</summary>
  [ValueConversion(typeof(IEnumerable), typeof(bool))]
  public class HasCoordinatesConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value is IEnumerable viewpoints)
      {
        foreach (object item in viewpoints)
        {
          if (item is ViewPoint vp && (vp.VisInfo?.PerspectiveCamera != null || vp.VisInfo?.OrthogonalCamera != null))
            return true;
        }
      }

      return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }
  }
}
