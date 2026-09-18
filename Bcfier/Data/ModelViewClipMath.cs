using System;
using System.Collections.Generic;
using System.Linq;

namespace Bcfier.Data
{
  /// <summary>
  /// Чистая геометрия: crop/разрез → ориентированный box и камера для BCF
  /// (логика как в SP_Create3DFromSection, без Revit API).
  /// </summary>
  public static class ModelViewClipMath
  {
    private const double MinExtent = 1e-9;

    /// <summary>Ориентированный параллелепипед в мировых координатах.</summary>
    public sealed class OrientedBox
    {
      public Vec3 Origin;
      public Vec3 BasisX;
      public Vec3 BasisY;
      public Vec3 BasisZ;
      public Vec3 Min;
      public Vec3 Max;
    }

    /// <summary>Параметры ортогональной камеры в тех же единицах, что и вход (обычно футы).</summary>
    public sealed class CameraPose
    {
      public Vec3 Eye;
      public Vec3 Forward;
      public Vec3 Up;
      public double ViewHeight;
    }

    /// <summary>
    /// CropBox разреза → система ViewSection.CreateSection.
    /// </summary>
    public static void CropBoxToCreateSectionTransform(
      Vec3 cropOrigin,
      Vec3 cropBasisX,
      Vec3 cropBasisY,
      Vec3 cropBasisZ,
      out Vec3 origin,
      out Vec3 basisX,
      out Vec3 basisY,
      out Vec3 basisZ)
    {
      basisZ = NormalizeOrFallback(Negate(NormalizeOrFallback(cropBasisZ, Vec3.UnitZ)), Vec3.UnitZ);
      basisY = NormalizeOrFallback(cropBasisY, Vec3.UnitY);
      basisX = NormalizeOrFallback(Cross(basisY, basisZ), Vec3.UnitX);
      basisY = NormalizeOrFallback(Cross(basisZ, basisX), Vec3.UnitY);
      origin = cropOrigin;
    }

    /// <summary>
    /// 3D SectionBox: X — ширина, Z — высота, Y — глубина; правосторонний базис.
    /// </summary>
    public static void Build3DSectionBoxTransform(
      Vec3 sectionOrigin,
      Vec3 sectionBasisX,
      Vec3 sectionBasisY,
      Vec3 sectionBasisZ,
      out Vec3 origin,
      out Vec3 basisX,
      out Vec3 basisY,
      out Vec3 basisZ)
    {
      basisX = NormalizeOrFallback(sectionBasisX, Vec3.UnitX);
      basisZ = NormalizeOrFallback(sectionBasisY, Vec3.UnitZ);
      basisY = NormalizeOrFallback(Cross(basisZ, basisX), Vec3.UnitY);
      basisX = NormalizeOrFallback(Cross(basisY, basisZ), Vec3.UnitX);
      origin = sectionOrigin;
    }

    /// <summary>
    /// SectionBox из crop разреза и дальней подрезки (farClip &gt; 0).
    /// </summary>
    public static OrientedBox CreateSectionBoxFromCrop(
      Vec3 cropOrigin,
      Vec3 cropBasisX,
      Vec3 cropBasisY,
      Vec3 cropBasisZ,
      Vec3 cropMin,
      Vec3 cropMax,
      double farClip)
    {
      if (farClip <= MinExtent)
        return null;

      CropBoxToCreateSectionTransform(
        cropOrigin, cropBasisX, cropBasisY, cropBasisZ,
        out Vec3 sectionOrigin, out Vec3 sx, out Vec3 sy, out Vec3 sz);

      double cropXMin = Math.Min(cropMin.X, cropMax.X);
      double cropXMax = Math.Max(cropMin.X, cropMax.X);
      double cropYMin = Math.Min(cropMin.Y, cropMax.Y);
      double cropYMax = Math.Max(cropMin.Y, cropMax.Y);
      double cropZMin = Math.Min(cropMin.Z, cropMax.Z);
      double cropZMax = Math.Max(cropMin.Z, cropMax.Z);

      Vec3[] sectionLocalCropCorners = GetBoxCorners(cropXMin, cropYMin, cropZMin, cropXMax, cropYMax, cropZMax)
        .Select(corner => InverseTransformPoint(sectionOrigin, sx, sy, sz, TransformPoint(cropOrigin, cropBasisX, cropBasisY, cropBasisZ, corner)))
        .ToArray();

      double widthMin = sectionLocalCropCorners.Min(p => p.X);
      double widthMax = sectionLocalCropCorners.Max(p => p.X);
      double heightMin = sectionLocalCropCorners.Min(p => p.Y);
      double heightMax = sectionLocalCropCorners.Max(p => p.Y);
      EnsureMinLessThanMax(ref widthMin, ref widthMax);
      EnsureMinLessThanMax(ref heightMin, ref heightMax);

      Vec3[] worldCorners = GetBoxCorners(widthMin, heightMin, 0, widthMax, heightMax, farClip)
        .Select(corner => TransformPoint(sectionOrigin, sx, sy, sz, corner))
        .ToArray();

      Build3DSectionBoxTransform(
        sectionOrigin, sx, sy, sz,
        out Vec3 boxOrigin, out Vec3 bx, out Vec3 by, out Vec3 bz);

      Vec3[] localCorners = worldCorners
        .Select(corner => InverseTransformPoint(boxOrigin, bx, by, bz, corner))
        .ToArray();

      return new OrientedBox
      {
        Origin = boxOrigin,
        BasisX = bx,
        BasisY = by,
        BasisZ = bz,
        Min = new Vec3(
          localCorners.Min(p => p.X),
          localCorners.Min(p => p.Y),
          localCorners.Min(p => p.Z)),
        Max = new Vec3(
          localCorners.Max(p => p.X),
          localCorners.Max(p => p.Y),
          localCorners.Max(p => p.Z))
      };
    }

    /// <summary>
    /// SectionBox из crop плана: XY crop, глубина вдоль ViewDirection от nearDepth до farDepth (локально по crop Z или явные пределы).
    /// </summary>
    public static OrientedBox CreatePlanBoxFromCrop(
      Vec3 cropOrigin,
      Vec3 cropBasisX,
      Vec3 cropBasisY,
      Vec3 cropBasisZ,
      Vec3 cropMin,
      Vec3 cropMax,
      double depthMin,
      double depthMax)
    {
      EnsureMinLessThanMax(ref depthMin, ref depthMax);
      if (depthMax - depthMin <= MinExtent)
        return null;

      double cropXMin = Math.Min(cropMin.X, cropMax.X);
      double cropXMax = Math.Max(cropMin.X, cropMax.X);
      double cropYMin = Math.Min(cropMin.Y, cropMax.Y);
      double cropYMax = Math.Max(cropMin.Y, cropMax.Y);
      EnsureMinLessThanMax(ref cropXMin, ref cropXMax);
      EnsureMinLessThanMax(ref cropYMin, ref cropYMax);

      Vec3 basisX = NormalizeOrFallback(cropBasisX, Vec3.UnitX);
      Vec3 basisY = NormalizeOrFallback(cropBasisY, Vec3.UnitY);
      Vec3 basisZ = NormalizeOrFallback(cropBasisZ, Vec3.UnitZ);
      // Правосторонний базис
      basisZ = NormalizeOrFallback(Cross(basisX, basisY), basisZ);
      basisY = NormalizeOrFallback(Cross(basisZ, basisX), basisY);

      return new OrientedBox
      {
        Origin = cropOrigin,
        BasisX = basisX,
        BasisY = basisY,
        BasisZ = basisZ,
        Min = new Vec3(cropXMin, cropYMin, depthMin),
        Max = new Vec3(cropXMax, cropYMax, depthMax)
      };
    }

    /// <summary>
    /// Камера как AlignView3DLikeSection: eye за разрезом, смотрит на центр box.
    /// </summary>
    public static CameraPose AlignCameraLikeSection(
      Vec3 viewDirection,
      Vec3 upDirection,
      Vec3 viewOrigin,
      OrientedBox box,
      double viewHeight)
    {
      if (box == null)
        return null;

      Vec3 viewDir = NormalizeOrFallback(viewDirection, Vec3.UnitY);
      Vec3 up = NormalizeOrFallback(upDirection, Vec3.UnitZ);
      Vec3 forward = Negate(viewDir);

      Vec3[] vertices = GetOrientedBoxVertices(box);
      Vec3 center = Average(vertices);

      double halfDepth = vertices
        .Select(v => Dot(Subtract(v, viewOrigin), Negate(viewDir)))
        .DefaultIfEmpty(0.0)
        .Max();
      halfDepth = Math.Max(halfDepth, 1.0);
      double eyeDistance = Math.Max(halfDepth * 2.5, 15.0);

      double height = viewHeight;
      if (height <= MinExtent)
        height = Math.Max(box.Max.Y - box.Min.Y, box.Max.X - box.Min.X);
      if (height <= MinExtent)
        height = 20.0;

      return new CameraPose
      {
        Eye = Add(center, Multiply(viewDir, eyeDistance)),
        Forward = forward,
        Up = up,
        ViewHeight = height
      };
    }

    /// <summary>
    /// Шесть граней box: центр грани в мире + внешняя нормаль (для ClippingPlanes).
    /// </summary>
    public static IReadOnlyList<(Vec3 center, Vec3 outward)> GetOutwardFaces(OrientedBox box)
    {
      if (box == null)
        return Array.Empty<(Vec3, Vec3)>();

      Vec3 min = box.Min;
      Vec3 max = box.Max;
      if (Distance(min, max) < MinExtent)
        return Array.Empty<(Vec3, Vec3)>();

      double midX = (min.X + max.X) * 0.5;
      double midY = (min.Y + max.Y) * 0.5;
      double midZ = (min.Z + max.Z) * 0.5;

      var faces = new (Vec3 localCenter, Vec3 outward)[]
      {
        (new Vec3(min.X, midY, midZ), Negate(box.BasisX)),
        (new Vec3(max.X, midY, midZ), box.BasisX),
        (new Vec3(midX, min.Y, midZ), Negate(box.BasisY)),
        (new Vec3(midX, max.Y, midZ), box.BasisY),
        (new Vec3(midX, midY, min.Z), Negate(box.BasisZ)),
        (new Vec3(midX, midY, max.Z), box.BasisZ)
      };

      var result = new List<(Vec3, Vec3)>(faces.Length);
      foreach ((Vec3 localCenter, Vec3 outward) in faces)
      {
        result.Add((
          TransformPoint(box.Origin, box.BasisX, box.BasisY, box.BasisZ, localCenter),
          NormalizeOrFallback(outward, Vec3.UnitZ)));
      }

      return result;
    }

    public static Vec3[] GetOrientedBoxVertices(OrientedBox box)
    {
      if (box == null)
        return Array.Empty<Vec3>();

      return GetBoxCorners(box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z)
        .Select(corner => TransformPoint(box.Origin, box.BasisX, box.BasisY, box.BasisZ, corner))
        .ToArray();
    }

    public static IEnumerable<Vec3> GetBoxCorners(
      double xMin, double yMin, double zMin,
      double xMax, double yMax, double zMax)
    {
      for (int i = 0; i <= 1; i++)
      {
        for (int j = 0; j <= 1; j++)
        {
          for (int k = 0; k <= 1; k++)
          {
            yield return new Vec3(
              i == 0 ? xMin : xMax,
              j == 0 ? yMin : yMax,
              k == 0 ? zMin : zMax);
          }
        }
      }
    }

    public static void EnsureMinLessThanMax(ref double min, ref double max)
    {
      if (min < max)
        return;

      double mid = (min + max) * 0.5;
      min = mid - 0.5;
      max = mid + 0.5;
    }

    public static Vec3 TransformPoint(Vec3 origin, Vec3 basisX, Vec3 basisY, Vec3 basisZ, Vec3 local)
    {
      return new Vec3(
        origin.X + basisX.X * local.X + basisY.X * local.Y + basisZ.X * local.Z,
        origin.Y + basisX.Y * local.X + basisY.Y * local.Y + basisZ.Y * local.Z,
        origin.Z + basisX.Z * local.X + basisY.Z * local.Y + basisZ.Z * local.Z);
    }

    public static Vec3 InverseTransformPoint(Vec3 origin, Vec3 basisX, Vec3 basisY, Vec3 basisZ, Vec3 world)
    {
      Vec3 d = Subtract(world, origin);
      return new Vec3(Dot(d, basisX), Dot(d, basisY), Dot(d, basisZ));
    }

    public static Vec3 Average(IEnumerable<Vec3> points)
    {
      var list = points?.ToList() ?? new List<Vec3>();
      if (list.Count == 0)
        return Vec3.Zero;
      return new Vec3(
        list.Average(p => p.X),
        list.Average(p => p.Y),
        list.Average(p => p.Z));
    }

    public static Vec3 NormalizeOrFallback(Vec3 value, Vec3 fallback)
    {
      double len = Length(value);
      if (len < MinExtent)
        return fallback;
      return new Vec3(value.X / len, value.Y / len, value.Z / len);
    }

    public static double Length(Vec3 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    public static double Distance(Vec3 a, Vec3 b) => Length(Subtract(a, b));

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) =>
      new Vec3(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public static Vec3 Add(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3 Subtract(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3 Multiply(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

    public static Vec3 Negate(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);

    /// <summary>Простой вектор 3D для геометрии без Revit.</summary>
    public struct Vec3
    {
      public double X;
      public double Y;
      public double Z;

      public Vec3(double x, double y, double z)
      {
        X = x;
        Y = y;
        Z = z;
      }

      public static Vec3 Zero => new Vec3(0, 0, 0);
      public static Vec3 UnitX => new Vec3(1, 0, 0);
      public static Vec3 UnitY => new Vec3(0, 1, 0);
      public static Vec3 UnitZ => new Vec3(0, 0, 1);
    }
  }
}
