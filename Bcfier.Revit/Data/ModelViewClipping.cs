using System;
using System.Linq;
using Autodesk.Revit.DB;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Point = Bcfier.Bcf.Bcf2.Point;
using Vec3 = Bcfier.Data.ModelViewClipMath.Vec3;

namespace Bcfier.Revit.Data
{
  /// <summary>
  /// Dual-write для модельных видов: OrthogonalCamera + ClippingPlanes из crop / секущего диапазона.
  /// </summary>
  public static class ModelViewClipping
  {
    /// <summary>
    /// План / разрез / фасад / план несущих — виды, для которых пишем стандартную BCF-камеру.
    /// </summary>
    public static bool IsModelViewForInterop(View view)
    {
      if (view == null || view.IsTemplate)
        return false;

      switch (view.ViewType)
      {
        case ViewType.FloorPlan:
        case ViewType.CeilingPlan:
        case ViewType.EngineeringPlan:
        case ViewType.Section:
        case ViewType.Elevation:
          return true;
      }

      if (view is ViewSection)
        return true;

      try
      {
        ElementId typeId = view.GetTypeId();
        if (typeId == null || typeId == ElementId.InvalidElementId)
          return false;

        if (view.Document?.GetElement(typeId) is ViewFamilyType familyType)
        {
          return familyType.ViewFamily == ViewFamily.Section
            || familyType.ViewFamily == ViewFamily.Elevation
            || familyType.ViewFamily == ViewFamily.FloorPlan
            || familyType.ViewFamily == ViewFamily.CeilingPlan
            || familyType.ViewFamily == ViewFamily.StructuralPlan;
        }
      }
      catch
      {
        // Тип вида недоступен — не считаем model interop
      }

      return false;
    }

    /// <summary>
    /// Заполняет OrthogonalCamera и при возможности ClippingPlanes по активному модельному виду.
    /// </summary>
    public static bool TryApplyInteropCamera(
      Document doc,
      View view,
      XYZ zoomTopLeft,
      XYZ zoomBottomRight,
      VisualizationInfo target)
    {
      if (doc == null || view == null || target == null || !IsModelViewForInterop(view))
        return false;

      try
      {
        ModelViewClipMath.OrientedBox box = TryBuildOrientedBox(view, zoomTopLeft, zoomBottomRight);
        double viewHeight = ResolveViewHeight(view, zoomTopLeft, zoomBottomRight, box);

        ModelViewClipMath.CameraPose pose = ModelViewClipMath.AlignCameraLikeSection(
          ToVec3(view.ViewDirection),
          ToVec3(view.UpDirection),
          ToVec3(view.Origin),
          box ?? BuildFallbackBoxAroundView(view, viewHeight),
          viewHeight);

        if (pose == null)
          return false;

        if (!TryWriteOrthogonalCamera(doc, pose, target))
          return false;

        if (box != null)
        {
          BoundingBoxXYZ revitBox = ToBoundingBox(box);
          ClippingPlane[] planes = SectionBoxClipping.TryFromBoundingBox(doc, revitBox);
          if (planes != null)
            target.ClippingPlanes = planes;
        }

        return true;
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Строит OBB: для разреза — crop + far clip; для плана — секущий диапазон (ViewRange),
    /// даже если рамка обрезки выключена (иначе в другой модели есть только камера без границ 3D).
    /// </summary>
    private static ModelViewClipMath.OrientedBox TryBuildOrientedBox(
      View view,
      XYZ zoomTopLeft,
      XYZ zoomBottomRight)
    {
      if (view == null)
        return null;

      if (IsSectionLike(view))
      {
        if (!view.CropBoxActive)
          return null;

        BoundingBoxXYZ sectionCrop = view.CropBox;
        if (sectionCrop == null)
          return null;

        Transform cropTrf = sectionCrop.Transform ?? Transform.Identity;
        double farClip = Math.Abs(
          view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR)?.AsDouble() ?? 0.0);
        if (farClip <= 1e-9)
          return null;

        return ModelViewClipMath.CreateSectionBoxFromCrop(
          ToVec3(cropTrf.Origin),
          ToVec3(cropTrf.BasisX),
          ToVec3(cropTrf.BasisY),
          ToVec3(cropTrf.BasisZ),
          ToVec3(sectionCrop.Min),
          ToVec3(sectionCrop.Max),
          farClip);
      }

      return TryBuildPlanOrientedBox(view, zoomTopLeft, zoomBottomRight);
    }

    /// <summary>
    /// План/потолок: глубина из ViewRange, XY из crop или zoom.
    /// </summary>
    private static ModelViewClipMath.OrientedBox TryBuildPlanOrientedBox(
      View view,
      XYZ zoomTopLeft,
      XYZ zoomBottomRight)
    {
      BoundingBoxXYZ crop = null;
      try
      {
        crop = view.CropBox;
      }
      catch
      {
        crop = null;
      }

      if (crop != null)
      {
        Transform cropTrf = crop.Transform ?? Transform.Identity;
        if (!TryGetPlanDepthRange(view, cropTrf, crop, out double depthMin, out double depthMax))
          return null;

        Vec3 min = ToVec3(crop.Min);
        Vec3 max = ToVec3(crop.Max);

        // Рамка обрезки выключена — берём видимую область из zoom UIView.
        if (!view.CropBoxActive
            && TryProjectZoomToCropLocal(
              cropTrf,
              zoomTopLeft,
              zoomBottomRight,
              out double xMin,
              out double xMax,
              out double yMin,
              out double yMax))
        {
          min = new Vec3(xMin, yMin, min.Z);
          max = new Vec3(xMax, yMax, max.Z);
        }

        return ModelViewClipMath.CreatePlanBoxFromCrop(
          ToVec3(cropTrf.Origin),
          ToVec3(cropTrf.BasisX),
          ToVec3(cropTrf.BasisY),
          ToVec3(cropTrf.BasisZ),
          min,
          max,
          depthMin,
          depthMax);
      }

      return TryBuildPlanBoxFromViewAxes(view, zoomTopLeft, zoomBottomRight);
    }

    /// <summary>
    /// Нет CropBox: OBB в осях вида (Right/Up/ViewDirection) + ViewRange + zoom.
    /// </summary>
    private static ModelViewClipMath.OrientedBox TryBuildPlanBoxFromViewAxes(
      View view,
      XYZ zoomTopLeft,
      XYZ zoomBottomRight)
    {
      Transform viewTrf = Transform.Identity;
      viewTrf.Origin = view.Origin ?? XYZ.Zero;
      viewTrf.BasisX = NormalizeXyz(view.RightDirection, XYZ.BasisX);
      viewTrf.BasisY = NormalizeXyz(view.UpDirection, XYZ.BasisY);
      viewTrf.BasisZ = NormalizeXyz(view.ViewDirection, XYZ.BasisZ);

      if (!TryGetPlanDepthRange(view, viewTrf, crop: null, out double depthMin, out double depthMax))
        return null;

      double half = Math.Max(ResolveViewHeight(view, zoomTopLeft, zoomBottomRight, box: null) * 0.5, 5.0);
      double xMin = -half;
      double xMax = half;
      double yMin = -half;
      double yMax = half;

      if (TryProjectZoomToCropLocal(
            viewTrf,
            zoomTopLeft,
            zoomBottomRight,
            out double zx0,
            out double zx1,
            out double zy0,
            out double zy1))
      {
        xMin = zx0;
        xMax = zx1;
        yMin = zy0;
        yMax = zy1;
      }

      return ModelViewClipMath.CreatePlanBoxFromCrop(
        ToVec3(viewTrf.Origin),
        ToVec3(viewTrf.BasisX),
        ToVec3(viewTrf.BasisY),
        ToVec3(viewTrf.BasisZ),
        new Vec3(xMin, yMin, 0),
        new Vec3(xMax, yMax, 0),
        depthMin,
        depthMax);
    }

    /// <summary>
    /// Проекция углов zoom на локальные XY crop/вида.
    /// </summary>
    private static bool TryProjectZoomToCropLocal(
      Transform localTrf,
      XYZ zoomTopLeft,
      XYZ zoomBottomRight,
      out double xMin,
      out double xMax,
      out double yMin,
      out double yMax)
    {
      xMin = xMax = yMin = yMax = 0;
      if (localTrf == null || zoomTopLeft == null || zoomBottomRight == null)
        return false;

      try
      {
        Transform inv = localTrf.Inverse;
        XYZ a = inv.OfPoint(zoomTopLeft);
        XYZ b = inv.OfPoint(zoomBottomRight);
        xMin = Math.Min(a.X, b.X);
        xMax = Math.Max(a.X, b.X);
        yMin = Math.Min(a.Y, b.Y);
        yMax = Math.Max(a.Y, b.Y);
        ModelViewClipMath.EnsureMinLessThanMax(ref xMin, ref xMax);
        ModelViewClipMath.EnsureMinLessThanMax(ref yMin, ref yMax);
        return (xMax - xMin) > 1e-6 && (yMax - yMin) > 1e-6;
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Глубина плана: ViewRange (Top…View Depth) в локальных Z crop/вида.
    /// </summary>
    private static bool TryGetPlanDepthRange(
      View view,
      Transform localTrf,
      BoundingBoxXYZ crop,
      out double depthMin,
      out double depthMax)
    {
      depthMin = 0;
      depthMax = 0;

      if (view is ViewPlan viewPlan)
      {
        try
        {
          PlanViewRange range = viewPlan.GetViewRange();
          if (TryProjectViewRangeToLocalZ(view, localTrf, range, out depthMin, out depthMax))
            return depthMax - depthMin > 1e-6;
        }
        catch
        {
          // ViewRange недоступен — fallback
        }
      }

      if (crop != null)
      {
        depthMin = Math.Min(crop.Min.Z, crop.Max.Z);
        depthMax = Math.Max(crop.Min.Z, crop.Max.Z);
        if (depthMax - depthMin > 1e-6)
          return true;
      }

      // Нет осмысленной глубины — небольшой объём вокруг плоскости вида
      depthMin = -5.0;
      depthMax = 5.0;
      return true;
    }

    /// <summary>
    /// Проецирует Top/ViewDepth ViewRange на локальную ось Z (футы).
    /// </summary>
    private static bool TryProjectViewRangeToLocalZ(
      View view,
      Transform localTrf,
      PlanViewRange range,
      out double depthMin,
      out double depthMax)
    {
      depthMin = 0;
      depthMax = 0;
      if (view?.Document == null || localTrf == null || range == null)
        return false;

      if (!TryGetPlaneWorldElevation(view, range, PlanViewPlane.TopClipPlane, out double topWorld)
          || !TryGetPlaneWorldElevation(view, range, PlanViewPlane.ViewDepthPlane, out double depthWorld))
        return false;

      try
      {
        Transform inv = localTrf.Inverse;
        XYZ origin = localTrf.Origin ?? XYZ.Zero;
        XYZ topLocal = inv.OfPoint(new XYZ(origin.X, origin.Y, topWorld));
        XYZ depthLocal = inv.OfPoint(new XYZ(origin.X, origin.Y, depthWorld));

        depthMin = Math.Min(topLocal.Z, depthLocal.Z);
        depthMax = Math.Max(topLocal.Z, depthLocal.Z);
        ModelViewClipMath.EnsureMinLessThanMax(ref depthMin, ref depthMax);
        return depthMax - depthMin > 1e-6;
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Мировая отметка плоскости ViewRange с учётом Level Above/Below и Unlimited.
    /// </summary>
    private static bool TryGetPlaneWorldElevation(
      View view,
      PlanViewRange range,
      PlanViewPlane plane,
      out double elevation)
    {
      elevation = 0;
      Document doc = view?.Document;
      if (doc == null || range == null)
        return false;

      ElementId levelId = range.GetLevelId(plane);
      if (levelId == null || levelId == ElementId.InvalidElementId)
        return false;

      // Unlimited не даёт конечной отметки — вызывающий код возьмёт fallback.
      if (levelId == PlanViewRange.Unlimited)
        return false;

      double offset = range.GetOffset(plane);
      Level level = null;

      if (levelId == PlanViewRange.LevelAbove || levelId == PlanViewRange.LevelBelow)
      {
        Level baseLevel = (view as ViewPlan)?.GenLevel;
        if (baseLevel == null)
          return false;

        bool wantAbove = levelId == PlanViewRange.LevelAbove;
        level = FindAdjacentLevel(doc, baseLevel, wantAbove);
      }
      else
      {
        level = doc.GetElement(levelId) as Level;
      }

      if (level == null)
        return false;

      elevation = level.Elevation + offset;
      return true;
    }

    /// <summary>Соседний уровень выше или ниже относительно базового.</summary>
    private static Level FindAdjacentLevel(Document doc, Level baseLevel, bool above)
    {
      if (doc == null || baseLevel == null)
        return null;

      try
      {
        var levels = new FilteredElementCollector(doc)
          .OfClass(typeof(Level))
          .Cast<Level>()
          .OrderBy(l => l.Elevation)
          .ToList();

        if (levels.Count == 0)
          return null;

        if (above)
        {
          return levels
            .Where(l => l.Id != baseLevel.Id && l.Elevation > baseLevel.Elevation + 1e-6)
            .OrderBy(l => l.Elevation)
            .FirstOrDefault();
        }

        return levels
          .Where(l => l.Id != baseLevel.Id && l.Elevation < baseLevel.Elevation - 1e-6)
          .OrderByDescending(l => l.Elevation)
          .FirstOrDefault();
      }
      catch
      {
        return null;
      }
    }

    private static XYZ NormalizeXyz(XYZ value, XYZ fallback)
    {
      if (value == null || value.IsZeroLength())
        return fallback;
      return value.Normalize();
    }

    private static bool IsSectionLike(View view)
    {
      if (view == null)
        return false;

      if (view.ViewType == ViewType.Section || view.ViewType == ViewType.Elevation)
        return true;

      if (view is ViewSection)
        return true;

      try
      {
        if (view.Document?.GetElement(view.GetTypeId()) is ViewFamilyType familyType)
        {
          return familyType.ViewFamily == ViewFamily.Section
            || familyType.ViewFamily == ViewFamily.Elevation;
        }
      }
      catch
      {
        // ignore
      }

      return false;
    }

    private static double ResolveViewHeight(
      View view,
      XYZ zoomTopLeft,
      XYZ zoomBottomRight,
      ModelViewClipMath.OrientedBox box)
    {
      if (zoomTopLeft != null && zoomBottomRight != null)
      {
        XYZ diag = zoomTopLeft.Subtract(zoomBottomRight);
        double alongUp = Math.Abs(diag.DotProduct(view.UpDirection));
        if (alongUp > 1e-6)
          return alongUp;
      }

      if (box != null)
      {
        double h = Math.Abs(box.Max.Y - box.Min.Y);
        if (h > 1e-6)
          return h;
        double w = Math.Abs(box.Max.X - box.Min.X);
        if (w > 1e-6)
          return w;
      }

      return 20.0;
    }

    private static ModelViewClipMath.OrientedBox BuildFallbackBoxAroundView(View view, double viewHeight)
    {
      double half = Math.Max(viewHeight * 0.5, 5.0);
      Vec3 origin = ToVec3(view.Origin);
      Vec3 right = ToVec3(view.RightDirection);
      Vec3 up = ToVec3(view.UpDirection);
      Vec3 dir = ToVec3(view.ViewDirection);
      return new ModelViewClipMath.OrientedBox
      {
        Origin = origin,
        BasisX = ModelViewClipMath.NormalizeOrFallback(right, Vec3.UnitX),
        BasisY = ModelViewClipMath.NormalizeOrFallback(up, Vec3.UnitZ),
        BasisZ = ModelViewClipMath.NormalizeOrFallback(dir, Vec3.UnitY),
        Min = new Vec3(-half, -half, -half),
        Max = new Vec3(half, half, half)
      };
    }

    private static bool TryWriteOrthogonalCamera(
      Document doc,
      ModelViewClipMath.CameraPose pose,
      VisualizationInfo target)
    {
      var eye = new XYZ(pose.Eye.X, pose.Eye.Y, pose.Eye.Z);
      // BCF CameraDirection = направление взгляда; Forward из Align = −ViewDirection
      var look = new XYZ(pose.Forward.X, pose.Forward.Y, pose.Forward.Z);
      var up = new XYZ(pose.Up.X, pose.Up.Y, pose.Up.Z);

      // Как BuildCamera: в ConvertBasePoint передаём ViewDirection (−Forward), затем инвертируем Forward
      ViewOrientation3D oriented = RevitUtils.ConvertBasePoint(doc, eye, look.Negate(), up, false);
      if (oriented == null)
        return false;

      XYZ c = oriented.EyePosition;
      XYZ vi = oriented.ForwardDirection;
      XYZ upVec = oriented.UpDirection;

      target.OrthogonalCamera = new OrthogonalCamera
      {
        CameraViewPoint = { X = c.X.ToMeters(), Y = c.Y.ToMeters(), Z = c.Z.ToMeters() },
        CameraUpVector = { X = upVec.X, Y = upVec.Y, Z = upVec.Z },
        CameraDirection = { X = vi.X * -1, Y = vi.Y * -1, Z = vi.Z * -1 },
        ViewToWorldScale = Math.Max(pose.ViewHeight.ToMeters() * 0.5, 0.1)
      };
      target.PerspectiveCamera = null;
      return true;
    }

    private static BoundingBoxXYZ ToBoundingBox(ModelViewClipMath.OrientedBox box)
    {
      var transform = Transform.Identity;
      transform.Origin = new XYZ(box.Origin.X, box.Origin.Y, box.Origin.Z);
      transform.BasisX = new XYZ(box.BasisX.X, box.BasisX.Y, box.BasisX.Z);
      transform.BasisY = new XYZ(box.BasisY.X, box.BasisY.Y, box.BasisY.Z);
      transform.BasisZ = new XYZ(box.BasisZ.X, box.BasisZ.Y, box.BasisZ.Z);

      return new BoundingBoxXYZ
      {
        Transform = transform,
        Min = new XYZ(box.Min.X, box.Min.Y, box.Min.Z),
        Max = new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
      };
    }

    private static Vec3 ToVec3(XYZ p)
    {
      if (p == null)
        return Vec3.Zero;
      return new Vec3(p.X, p.Y, p.Z);
    }
  }
}
