using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Bcfier.Bcf.Bcf2;
using Point = Bcfier.Bcf.Bcf2.Point;

namespace Bcfier.Revit.Data
{
  /// <summary>
  /// Roundtrip Revit Section Box ↔ BCF ClippingPlanes.
  /// Direction в BCF — в отсекаемую (невидимую) полупространство; для section box это внешние нормали граней.
  /// </summary>
  public static class SectionBoxClipping
  {
    private const double ParallelDotThreshold = 0.95;
    private const double MinExtent = 1e-6;

    /// <summary>
    /// Экспорт активного section box в 6 ClippingPlanes (с Transform и Project Base Point).
    /// </summary>
    public static ClippingPlane[] TryFromView3D(Document doc, View3D view)
    {
      if (doc == null || view == null || !view.IsSectionBoxActive)
        return null;

      BoundingBoxXYZ box = view.GetSectionBox();
      if (box == null)
        return null;

      Transform t = box.Transform ?? Transform.Identity;
      XYZ min = box.Min;
      XYZ max = box.Max;
      if (min.DistanceTo(max) < MinExtent)
        return null;

      // Оси box в model space (Transform может содержать поворот/смещение)
      XYZ axisX = NormalizeOrFallback(t.OfVector(XYZ.BasisX), XYZ.BasisX);
      XYZ axisY = NormalizeOrFallback(t.OfVector(XYZ.BasisY), XYZ.BasisY);
      XYZ axisZ = NormalizeOrFallback(t.OfVector(XYZ.BasisZ), XYZ.BasisZ);

      double midX = (min.X + max.X) * 0.5;
      double midY = (min.Y + max.Y) * 0.5;
      double midZ = (min.Z + max.Z) * 0.5;

      // Центры граней в local → model; Direction = внешняя нормаль
      var faces = new (XYZ localCenter, XYZ outward)[]
      {
        (new XYZ(min.X, midY, midZ), axisX.Negate()),
        (new XYZ(max.X, midY, midZ), axisX),
        (new XYZ(midX, min.Y, midZ), axisY.Negate()),
        (new XYZ(midX, max.Y, midZ), axisY),
        (new XYZ(midX, midY, min.Z), axisZ.Negate()),
        (new XYZ(midX, midY, max.Z), axisZ)
      };

      var planes = new ClippingPlane[faces.Length];
      for (int i = 0; i < faces.Length; i++)
      {
        XYZ modelPoint = t.OfPoint(faces[i].localCenter);
        XYZ modelNormal = NormalizeOrFallback(faces[i].outward, XYZ.BasisZ);
        planes[i] = ToBcfPlane(doc, modelPoint, modelNormal);
      }

      return planes;
    }

    /// <summary>
    /// Восстановление ориентированного BoundingBoxXYZ из ClippingPlanes (OBB с Transform).
    /// Нужны 3 пары почти параллельных противоположных плоскостей; иначе AABB-fallback.
    /// </summary>
    public static BoundingBoxXYZ TryToSectionBox(Document doc, ClippingPlane[] planes)
    {
      if (doc == null || planes == null || planes.Length == 0)
        return null;

      var modelPlanes = new List<(XYZ point, XYZ outward)>();
      foreach (ClippingPlane plane in planes)
      {
        if (plane?.Location == null || plane.Direction == null)
          continue;

        XYZ point = PointFromBcf(doc, plane.Location);
        XYZ outward = DirectionFromBcf(doc, plane.Direction);
        if (point == null || outward == null || outward.IsZeroLength())
          continue;

        modelPlanes.Add((point, outward.Normalize()));
      }

      if (modelPlanes.Count < 2)
        return null;

      BoundingBoxXYZ oriented = TryBuildOrientedSectionBox(modelPlanes);
      if (oriented != null)
        return oriented;

      // Solibri/другие писатели: нормали или число плоскостей не всегда дают OBB —
      // всё равно включаем обрезку по AABB точек плоскостей.
      return TryBuildAxisAlignedSectionBox(modelPlanes);
    }

    /// <summary>
    /// Строит ориентированный section box по трём парам противоположных плоскостей.
    /// </summary>
    private static BoundingBoxXYZ TryBuildOrientedSectionBox(List<(XYZ point, XYZ outward)> modelPlanes)
    {
      if (modelPlanes == null || modelPlanes.Count < 6)
        return null;

      List<AxisCluster> axes = ClusterAxes(modelPlanes);
      List<AxisCluster> complete = axes.Where(a => a.HasMin && a.HasMax).Take(3).ToList();
      if (complete.Count < 3)
        return null;

      XYZ basisX = complete[0].Direction;
      XYZ basisY = complete[1].Direction;
      XYZ basisZ = complete[2].Direction;

      double dMinX = complete[0].MinProjection;
      double dMaxX = complete[0].MaxProjection;
      double dMinY = complete[1].MinProjection;
      double dMaxY = complete[1].MaxProjection;
      double dMinZ = complete[2].MinProjection;
      double dMaxZ = complete[2].MaxProjection;

      // Если в BCF Direction смотрит «внутрь», min/max могут оказаться перепутаны.
      NormalizeExtent(ref dMinX, ref dMaxX);
      NormalizeExtent(ref dMinY, ref dMaxY);
      NormalizeExtent(ref dMinZ, ref dMaxZ);

      if (basisX.CrossProduct(basisY).DotProduct(basisZ) < 0)
      {
        basisZ = basisZ.Negate();
        double tmp = dMinZ;
        dMinZ = -dMaxZ;
        dMaxZ = -tmp;
        NormalizeExtent(ref dMinZ, ref dMaxZ);
      }

      double extentX = dMaxX - dMinX;
      double extentY = dMaxY - dMinY;
      double extentZ = dMaxZ - dMinZ;
      if (extentX < MinExtent || extentY < MinExtent || extentZ < MinExtent)
        return null;

      XYZ origin =
        basisX.Multiply((dMinX + dMaxX) * 0.5) +
        basisY.Multiply((dMinY + dMaxY) * 0.5) +
        basisZ.Multiply((dMinZ + dMaxZ) * 0.5);

      var transform = Transform.Identity;
      transform.Origin = origin;
      transform.BasisX = basisX;
      transform.BasisY = basisY;
      transform.BasisZ = basisZ;

      return new BoundingBoxXYZ
      {
        Transform = transform,
        Min = new XYZ(-extentX * 0.5, -extentY * 0.5, -extentZ * 0.5),
        Max = new XYZ(extentX * 0.5, extentY * 0.5, extentZ * 0.5)
      };
    }

    /// <summary>
    /// AABB-fallback: ограничивающий параллелепипед по точкам плоскостей с запасом вдоль нормалей.
    /// </summary>
    private static BoundingBoxXYZ TryBuildAxisAlignedSectionBox(List<(XYZ point, XYZ outward)> modelPlanes)
    {
      if (modelPlanes == null || modelPlanes.Count == 0)
        return null;

      double minX = double.PositiveInfinity;
      double minY = double.PositiveInfinity;
      double minZ = double.PositiveInfinity;
      double maxX = double.NegativeInfinity;
      double maxY = double.NegativeInfinity;
      double maxZ = double.NegativeInfinity;

      foreach ((XYZ point, XYZ outward) in modelPlanes)
      {
        // Точка на плоскости + небольшой сдвиг внутрь видимой области (−outward).
        XYZ inward = point.Subtract(outward.Multiply(0.01));
        minX = Math.Min(minX, Math.Min(point.X, inward.X));
        minY = Math.Min(minY, Math.Min(point.Y, inward.Y));
        minZ = Math.Min(minZ, Math.Min(point.Z, inward.Z));
        maxX = Math.Max(maxX, Math.Max(point.X, inward.X));
        maxY = Math.Max(maxY, Math.Max(point.Y, inward.Y));
        maxZ = Math.Max(maxZ, Math.Max(point.Z, inward.Z));
      }

      // Для каждой оси уточняем границы по почти осевым плоскостям.
      foreach ((XYZ point, XYZ outward) in modelPlanes)
      {
        double ax = Math.Abs(outward.X);
        double ay = Math.Abs(outward.Y);
        double az = Math.Abs(outward.Z);
        if (ax >= ParallelDotThreshold && ax >= ay && ax >= az)
        {
          if (outward.X > 0)
            maxX = Math.Min(maxX, point.X);
          else
            minX = Math.Max(minX, point.X);
        }
        else if (ay >= ParallelDotThreshold && ay >= ax && ay >= az)
        {
          if (outward.Y > 0)
            maxY = Math.Min(maxY, point.Y);
          else
            minY = Math.Max(minY, point.Y);
        }
        else if (az >= ParallelDotThreshold && az >= ax && az >= ay)
        {
          if (outward.Z > 0)
            maxZ = Math.Min(maxZ, point.Z);
          else
            minZ = Math.Max(minZ, point.Z);
        }
      }

      if (maxX - minX < MinExtent || maxY - minY < MinExtent || maxZ - minZ < MinExtent)
        return null;

      return new BoundingBoxXYZ
      {
        Transform = Transform.Identity,
        Min = new XYZ(minX, minY, minZ),
        Max = new XYZ(maxX, maxY, maxZ)
      };
    }

    /// <summary>
    /// Гарантирует min &lt;= max для проекций граней section box.
    /// </summary>
    private static void NormalizeExtent(ref double min, ref double max)
    {
      if (min <= max)
        return;

      double tmp = min;
      min = max;
      max = tmp;
    }

    private static ClippingPlane ToBcfPlane(Document doc, XYZ modelPoint, XYZ modelOutward)
    {
      XYZ bcfPoint = PointToBcf(doc, modelPoint);
      XYZ bcfDir = DirectionToBcf(doc, modelOutward);
      return new ClippingPlane
      {
        Location = new Point { X = bcfPoint.X, Y = bcfPoint.Y, Z = bcfPoint.Z },
        Direction = new Direction { X = bcfDir.X, Y = bcfDir.Y, Z = bcfDir.Z }
      };
    }

    /// <summary>Model feet → BCF meters с учётом Project Base Point (как камера, export).</summary>
    private static XYZ PointToBcf(Document doc, XYZ modelPoint)
    {
      XYZ p = RevitUtils.ConvertPointBasePoint(doc, modelPoint, false);
      return new XYZ(p.X.ToMeters(), p.Y.ToMeters(), p.Z.ToMeters());
    }

    /// <summary>BCF meters → model feet с учётом Project Base Point (import).</summary>
    private static XYZ PointFromBcf(Document doc, Point location)
    {
      XYZ feet = RevitUtils.GetRevitXYZ(location);
      return RevitUtils.ConvertPointBasePoint(doc, feet, true);
    }

    /// <summary>Единичный вектор: только поворот базы, без ToMeters на компонентах.</summary>
    private static XYZ DirectionToBcf(Document doc, XYZ modelDir)
    {
      XYZ rotated = RotateByProjectAngle(doc, modelDir, forExport: true);
      return NormalizeOrFallback(rotated, XYZ.BasisZ);
    }

    private static XYZ DirectionFromBcf(Document doc, Direction direction)
    {
      // BCF Direction — безразмерный unit vector в той же СК, что и точки (после поворота базы)
      var raw = new XYZ(direction.X, direction.Y, direction.Z);
      if (raw.IsZeroLength())
        return null;
      XYZ rotated = RotateByProjectAngle(doc, raw.Normalize(), forExport: false);
      return NormalizeOrFallback(rotated, null);
    }

    private static XYZ RotateByProjectAngle(Document doc, XYZ vector, bool forExport)
    {
      if (vector == null)
        return null;

      ProjectPosition position = null;
      try
      {
        position = doc?.ActiveProjectLocation?.GetProjectPosition(XYZ.Zero);
      }
      catch
      {
        position = null;
      }

      if (position == null)
        return vector;

      double angle = forExport ? position.Angle : -position.Angle;
      double x = (vector.X * Math.Cos(angle)) - (vector.Y * Math.Sin(angle));
      double y = (vector.X * Math.Sin(angle)) + (vector.Y * Math.Cos(angle));
      return new XYZ(x, y, vector.Z);
    }

    private static List<AxisCluster> ClusterAxes(List<(XYZ point, XYZ outward)> modelPlanes)
    {
      var axes = new List<AxisCluster>();
      foreach ((XYZ point, XYZ outward) in modelPlanes)
      {
        AxisCluster match = axes.FirstOrDefault(
          a => Math.Abs(a.Direction.DotProduct(outward)) >= ParallelDotThreshold);

        if (match == null)
        {
          match = new AxisCluster { Direction = outward };
          axes.Add(match);
        }

        double alignment = match.Direction.DotProduct(outward);
        double projection = point.DotProduct(match.Direction);
        // Нормаль ≈ +Direction → грань max; ≈ −Direction → грань min
        if (alignment >= ParallelDotThreshold)
        {
          if (!match.HasMax || projection > match.MaxProjection)
          {
            match.HasMax = true;
            match.MaxProjection = projection;
          }
        }
        else if (alignment <= -ParallelDotThreshold)
        {
          if (!match.HasMin || projection < match.MinProjection)
          {
            match.HasMin = true;
            match.MinProjection = projection;
          }
        }
      }

      return axes;
    }

    private static XYZ NormalizeOrFallback(XYZ value, XYZ fallback)
    {
      if (value == null || value.IsZeroLength())
        return fallback;
      return value.Normalize();
    }

    private sealed class AxisCluster
    {
      public XYZ Direction;
      public bool HasMin;
      public bool HasMax;
      public double MinProjection;
      public double MaxProjection;
    }
  }
}
