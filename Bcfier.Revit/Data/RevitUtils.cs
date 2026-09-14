using Autodesk.Revit.DB;
using Bcfier.Bcf.Bcf2;
using System;
using Point = Bcfier.Bcf.Bcf2.Point;

namespace Bcfier.Revit.Data
{
  public enum BcfCoordinateMode
  {
    RevitZUp = 0,
    SourceYUp = 1,
    SourceXUp = 2
  }

  public static class RevitUtils
  {
    /// <summary>
    //MOVES THE CAMERA ACCORDING TO THE PROJECT BASE LOCATION 
    //function that changes the coordinates accordingly to the project base location to an absolute location (for BCF export)
    //if the value negative is set to true, does the opposite (for opening BCF views)
    /// </summary>
    /// <param name="c">center</param>
    /// <param name="view">view direction</param>
    /// <param name="up">up direction</param>
    /// <param name="negative">convert to/from</param>
    /// <returns></returns>
    public static ViewOrientation3D ConvertBasePoint(Document doc, XYZ c, XYZ view, XYZ up, bool negative)
    {
      if (c == null
          || !HasFiniteCoordinates(c.X, c.Y, c.Z)
          || (view != null && !HasFiniteCoordinates(view.X, view.Y, view.Z))
          || (up != null && !HasFiniteCoordinates(up.X, up.Y, up.Z)))
      {
        return null;
      }

      TransformVectorsByBasePoint(doc, c, view, up, negative, out XYZ newC, out XYZ newView, out XYZ newUp);
      ValidateOrientationVectors(newView, newUp, out XYZ fixedForward, out XYZ fixedUp);
      return new ViewOrientation3D(newC, fixedUp, fixedForward);
    }

    /// <summary>
    /// Преобразует только точку с учётом Project Base Point (без ориентации камеры).
    /// </summary>
    public static XYZ ConvertPointBasePoint(Document doc, XYZ point, bool negative)
    {
      TransformVectorsByBasePoint(doc, point, XYZ.BasisX, XYZ.BasisZ, negative, out XYZ newC, out _, out _);
      return newC;
    }

    /// <summary>
    /// Поворачивает/смещает точку и векторы относительно Project Base Point.
    /// </summary>
    private static void TransformVectorsByBasePoint(
      Document doc,
      XYZ c,
      XYZ view,
      XYZ up,
      bool negative,
      out XYZ newC,
      out XYZ newView,
      out XYZ newUp)
    {
      view = view ?? XYZ.BasisZ.Negate();
      up = up ?? XYZ.BasisZ;

      ProjectPosition position = null;
      try
      {
        position = doc?.ActiveProjectLocation?.GetProjectPosition(XYZ.Zero);
      }
      catch
      {
        position = null;
      }

      // Нет Project Location — оставляем координаты как есть.
      if (position == null)
      {
        newC = c;
        newView = view;
        newUp = up;
        return;
      }

      int i = negative ? -1 : 1;
      double x = i * position.EastWest;
      double y = i * position.NorthSouth;
      double z = i * position.Elevation;
      double angle = i * position.Angle;

      if (negative)
        c = new XYZ(c.X + x, c.Y + y, c.Z + z);

      double centX = (c.X * Math.Cos(angle)) - (c.Y * Math.Sin(angle));
      double centY = (c.X * Math.Sin(angle)) + (c.Y * Math.Cos(angle));

      if (negative)
        newC = new XYZ(centX, centY, c.Z);
      else
        newC = new XYZ(centX + x, centY + y, c.Z + z);

      double viewX = (view.X * Math.Cos(angle)) - (view.Y * Math.Sin(angle));
      double viewY = (view.X * Math.Sin(angle)) + (view.Y * Math.Cos(angle));
      newView = new XYZ(viewX, viewY, view.Z);

      double upX = (up.X * Math.Cos(angle)) - (up.Y * Math.Sin(angle));
      double upY = (up.X * Math.Sin(angle)) + (up.Y * Math.Cos(angle));
      newUp = new XYZ(upX, upY, up.Z);
    }

    /// <summary>
    /// BCF Direction — безразмерный unit vector; не переводим через ToFeet.
    /// Нулевой, NaN или отсутствующий вектор возвращает null.
    /// </summary>
    public static XYZ GetBcfDirection(Direction direction)
    {
      if (direction == null)
        return null;

      return TryCreateXyz(direction.X, direction.Y, direction.Z);
    }

    /// <summary>
    /// Возвращает направление из BCF или запасное значение, если вектор не задан.
    /// </summary>
    public static XYZ GetBcfDirectionOrDefault(Direction direction, XYZ fallback)
    {
      XYZ value = GetBcfDirection(direction);
      return value ?? fallback;
    }

    /// <summary>
    /// Сохраняет исходную ориентацию BCF и корректирует только невалидные случаи для Revit.
    /// </summary>
    private static void ValidateOrientationVectors(XYZ forwardHint, XYZ upHint, out XYZ forward, out XYZ up)
    {
      forward = NormalizeOrFallback(forwardHint, XYZ.BasisY);

      XYZ rawUp = upHint;
      if (rawUp == null || rawUp.IsZeroLength())
        rawUp = XYZ.BasisZ;

      // Если векторы уже валидны для Revit — не меняем ориентацию.
      XYZ normalizedUp = rawUp.Normalize();
      if (Math.Abs(normalizedUp.DotProduct(forward)) < 1e-6)
      {
        up = normalizedUp;
        return;
      }

      // Минимальная коррекция: только убираем компоненту вдоль forward.
      XYZ projected = forward.Multiply(rawUp.DotProduct(forward));
      XYZ orthogonalUp = rawUp.Subtract(projected);
      if (orthogonalUp.IsZeroLength())
      {
        XYZ refAxis = Math.Abs(forward.DotProduct(XYZ.BasisZ)) < 0.99 ? XYZ.BasisZ : XYZ.BasisX;
        orthogonalUp = refAxis.Subtract(forward.Multiply(refAxis.DotProduct(forward)));
      }

      up = NormalizeOrFallback(orthogonalUp, XYZ.BasisZ);

      // Удерживаем исходный знак "вверх", чтобы не получить переворот экрана.
      if (up.DotProduct(rawUp) < 0)
        up = up.Negate();
    }

    /// <summary>
    /// Возвращает единичный вектор или запасной, если исходный нулевой.
    /// </summary>
    private static XYZ NormalizeOrFallback(XYZ vector, XYZ fallback)
    {
      if (vector == null || vector.IsZeroLength())
        return fallback.Normalize();
      return vector.Normalize();
    }

    public static XYZ GetRevitXYZ(double X, double Y, double Z)
    {
      if (!HasFiniteCoordinates(X, Y, Z))
        return null;

      try
      {
        return new XYZ(X.ToFeet(), Y.ToFeet(), Z.ToFeet());
      }
      catch
      {
        return null;
      }
    }

    public static XYZ GetRevitXYZ(Direction d)
    {
      return GetBcfDirection(d);
    }

    public static XYZ GetRevitXYZ(Point d)
    {
      if (d == null)
        return null;

      return GetRevitXYZ(d.X, d.Y, d.Z);
    }

    /// <summary>
    /// Revit XYZ нельзя создавать с NaN/Infinity: конструктор даёт невосстановимую ошибку процесса.
    /// </summary>
    public static bool HasFiniteCoordinates(double x, double y, double z)
    {
      return !double.IsNaN(x) && !double.IsNaN(y) && !double.IsNaN(z)
          && !double.IsInfinity(x) && !double.IsInfinity(y) && !double.IsInfinity(z);
    }

    /// <summary>
    /// Создаёт XYZ только для конечных координат, без перевода единиц.
    /// Нулевой вектор отбрасываем (для направлений камеры).
    /// </summary>
    public static XYZ TryCreateXyz(double x, double y, double z)
    {
      if (!HasFiniteCoordinates(x, y, z))
        return null;

      try
      {
        var vector = new XYZ(x, y, z);
        return vector.IsZeroLength() ? null : vector;
      }
      catch
      {
        return null;
      }
    }

    /// <summary>
    /// Преобразует точку BCF в систему координат Revit по выбранному пресету осей.
    /// </summary>
    public static XYZ MapBcfPointToRevit(XYZ point, BcfCoordinateMode mode)
    {
      if (point == null)
        return null;

      switch (mode)
      {
        case BcfCoordinateMode.SourceYUp:
          return new XYZ(point.X, point.Z, point.Y);
        case BcfCoordinateMode.SourceXUp:
          return new XYZ(point.Z, point.Y, point.X);
        default:
          return point;
      }
    }

    /// <summary>
    /// Преобразует вектор BCF в систему координат Revit по выбранному пресету осей.
    /// </summary>
    public static XYZ MapBcfVectorToRevit(XYZ vector, BcfCoordinateMode mode)
    {
      if (vector == null)
        return null;

      XYZ mapped = MapBcfPointToRevit(vector, mode);
      if (mapped == null || mapped.IsZeroLength())
        return mapped;

      return mapped.Normalize();
    }

    /// <summary>
    /// Converts feet units to meters
    /// </summary>
    /// <param name="feet">Value in feet to be converted to meters</param>
    /// <returns></returns>
    public static double ToMeters(this double feet)
    {
      return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);
    }
    /// <summary>
    /// Converts meters units to feet
    /// </summary>
    /// <param name="meters">Value in feet to be converted to feet</param>
    /// <returns></returns>
    public static double ToFeet(this double meters)
    {
      return UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);
    }
  }

}
