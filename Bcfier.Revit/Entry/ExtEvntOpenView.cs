using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Bcfier.Data.Utils;
using Bcfier.Localization;
using Bcfier.Revit.Data;
using Bcfier.Revit.Host;

namespace Bcfier.Revit.Entry
{
  /// <summary>
  /// Obfuscation Ignore for External Interface
  /// </summary>
  public class ExtEvntOpenView : IExternalEventHandler
  {
    public VisualizationInfo v;
    public bool ApplyVisibilityIsolation { get; set; }

    /// <summary>
    /// External Event Implementation
    /// </summary>
    /// <param name="app"></param>
    public void Execute(UIApplication app)
    {
      bool applyVisibilityIsolation = ApplyVisibilityIsolation;
      ApplyVisibilityIsolation = false;
      VisualizationInfo viewpoint = v;

      try
      {
        if (app == null)
          return;

        UIDocument uidoc = app.ActiveUIDocument;
        if (uidoc?.Document == null)
          return;

        Document doc = uidoc.Document;

        // Нет VisInfo (повреждённый/неполный BCF) — не падаем на v.OrthogonalCamera.
        if (viewpoint == null)
        {
          RevitExceptionUi.Show(
            Loc.Get("ViewpointNull"),
            Loc.Error);
          return;
        }

        bool hasSheetCamera = viewpoint.SheetCamera != null;
        bool sheetViewFound = hasSheetCamera
          && TryFindSheetView(doc, viewpoint.SheetCamera, out _);
        ViewpointOpenAction openAction = ViewpointOpenStrategy.Resolve(
          hasSheetCamera,
          sheetViewFound,
          viewpoint.OrthogonalCamera != null,
          viewpoint.PerspectiveCamera != null);

        if (openAction == ViewpointOpenAction.NotifySheetViewMissing)
        {
          string viewName = string.IsNullOrWhiteSpace(viewpoint.SheetCamera?.SheetName)
            ? viewpoint.SheetCamera?.SheetID.ToString() ?? string.Empty
            : viewpoint.SheetCamera.SheetName;
          RevitExceptionUi.Show(
            Loc.Format("SheetViewNotInModelMessage", viewName),
            Loc.Get("SheetViewNotInModelTitle"));
          return;
        }

        // Один общий 3D-вид пользователя; uniqueView оставляем для отладки.
        bool uniqueView = false;

        // Section box: сначала ClippingPlanes из BCF.
        BoundingBoxXYZ resolvedSectionBox = SectionBoxClipping.TryToSectionBox(doc, viewpoint.ClippingPlanes);

        // Элементы нужны для изоляции и/или для «Границы 3D», если плоскостей обрезки в BCF нет.
        bool needElementResolve = applyVisibilityIsolation || resolvedSectionBox == null;
        List<Component> unresolvedComponents = null;
        Dictionary<Component, ElementId> resolvedComponents = needElementResolve
          ? ResolveComponents(doc, viewpoint.Components, out unresolvedComponents)
          : new Dictionary<Component, ElementId>();
        List<ElementId> elementsToSelect = needElementResolve
          ? GetResolvedIds(viewpoint.Components?.Selection, resolvedComponents)
          : new List<ElementId>();
        List<ElementId> visibilityExceptions = needElementResolve
          ? GetResolvedIds(viewpoint.Components?.Visibility?.Exceptions, resolvedComponents)
          : new List<ElementId>();
        List<ElementId> sectionBoxTargets = needElementResolve
          ? (elementsToSelect.Any()
            ? elementsToSelect
            : viewpoint.Components?.Visibility?.DefaultVisibility == false
              ? visibilityExceptions
              : new List<ElementId>())
          : new List<ElementId>();

        // Нет ClippingPlanes → строим «Границу 3D» по bbox найденных элементов
        // (и для обычного «3D», и для «3D с изоляцией»).
        if (resolvedSectionBox == null)
          resolvedSectionBox = BuildElementsBoundingBox(doc, sectionBoxTargets);

        XYZ cameraViewPoint = null;
        XYZ cameraDirection = null;
        XYZ cameraUpVector = null;
        bool cameraViewOpened = false;
        View3D openedView3D = null;

        // Сначала исходный 2D/лист в этой модели (dual-write: Ortho тоже может быть).
        if (openAction == ViewpointOpenAction.OpenSheetView
            && TryOpenSheetView(uidoc, doc, viewpoint.SheetCamera))
        {
          cameraViewOpened = true;
        }
        // IS ORTHOGONAL (приоритетнее perspective)
        else if (viewpoint.OrthogonalCamera != null)
        {
          ViewOrientation3D orthoOrient = null;
          if (TryGetCameraVectors(
            viewpoint.OrthogonalCamera.CameraViewPoint,
            viewpoint.OrthogonalCamera.CameraDirection,
            viewpoint.OrthogonalCamera.CameraUpVector,
            out cameraViewPoint,
            out cameraDirection,
            out cameraUpVector))
          {
            orthoOrient = RevitUtils.ConvertBasePoint(doc, cameraViewPoint, cameraDirection, cameraUpVector, true);
          }

          if (orthoOrient != null)
          {
            cameraViewOpened = true;
            var zoom = viewpoint.OrthogonalCamera.ViewToWorldScale.ToFeet();

            View3D orthoView = null;
            using (var trans = new Transaction(uidoc.Document))
            {
              if (trans.Start("Open orthogonal view") == TransactionStatus.Started)
              {
                orthoView = GetOrCreateWorking3DView(uidoc, doc, uniqueView);
                if (orthoView == null)
                {
                  trans.RollBack();
                }
                else
                {
                  orthoView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                  orthoView.SetOrientation(orthoOrient);
                  ApplySectionBox(orthoView, resolvedSectionBox);
                  trans.Commit();
                }
              }
            }

            if (orthoView != null)
            {
              openedView3D = orthoView;
              if (uidoc.ActiveView == null || uidoc.ActiveView.Id != orthoView.Id)
                uidoc.ActiveView = orthoView;
              try { uidoc.RefreshActiveView(); } catch { /* не во всех версиях Revit */ }

              // Зум по камере BCF: только UIView этого 3D-вида.
              double x = zoom;
              XYZ m_xyzTl = orthoView.Origin.Add(orthoView.UpDirection.Multiply(x)).Subtract(orthoView.RightDirection.Multiply(x));
              XYZ m_xyzBr = orthoView.Origin.Subtract(orthoView.UpDirection.Multiply(x)).Add(orthoView.RightDirection.Multiply(x));
              ZoomUiView(uidoc, orthoView.Id, m_xyzTl, m_xyzBr);

              if (applyVisibilityIsolation
                  && sectionBoxTargets.Count > 0
                  && sectionBoxTargets.Count <= 300)
                uidoc.ShowElements(sectionBoxTargets);
            }
          }
          else if (TryOpenFallback3DView(
            uidoc,
            doc,
            uniqueView,
            viewpoint,
            resolvedSectionBox,
            sectionBoxTargets,
            out openedView3D))
          {
            // OrthogonalCamera задана, но координаты неполные — остаёмся в ортогональном 3D.
            cameraViewOpened = true;
          }
        }
        //perspective (только если OrthogonalCamera отсутствует)
        else if (viewpoint.PerspectiveCamera != null
                 && TryGetCameraVectors(
                   viewpoint.PerspectiveCamera.CameraViewPoint,
                   viewpoint.PerspectiveCamera.CameraDirection,
                   viewpoint.PerspectiveCamera.CameraUpVector,
                   out cameraViewPoint,
                   out cameraDirection,
                   out cameraUpVector))
        {
          var orient3D = RevitUtils.ConvertBasePoint(doc, cameraViewPoint, cameraDirection, cameraUpVector, true);
          if (orient3D == null)
          {
            if (TryOpenFallback3DView(
              uidoc,
              doc,
              uniqueView,
              viewpoint,
              resolvedSectionBox,
              sectionBoxTargets,
              out openedView3D))
            {
              cameraViewOpened = true;
            }
          }
          else
          {
          cameraViewOpened = true;

          // Перспективу BCF открываем в ортогональном 3D, без вида «Камера».
          View3D perspView = null;
          using (var trans = new Transaction(uidoc.Document))
          {
            if (trans.Start("Open orthogonal view") == TransactionStatus.Started)
            {
              perspView = GetOrCreateWorking3DView(uidoc, doc, uniqueView);
              if (perspView == null)
              {
                trans.RollBack();
              }
              else
              {
                perspView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                perspView.SetOrientation(orient3D);
                ApplySectionBox(perspView, resolvedSectionBox);
                trans.Commit();
              }
            }
          }

          if (perspView != null)
          {
            openedView3D = perspView;
            if (uidoc.ActiveView == null || uidoc.ActiveView.Id != perspView.Id)
              uidoc.ActiveView = perspView;
            try { uidoc.RefreshActiveView(); } catch { /* не во всех версиях Revit */ }

            ZoomPerspectiveAsOrthogonal(
              uidoc,
              perspView,
              viewpoint.PerspectiveCamera.FieldOfView,
              resolvedSectionBox,
              orient3D.EyePosition);

            if (applyVisibilityIsolation
                && sectionBoxTargets.Count > 0
                && sectionBoxTargets.Count <= 300)
              uidoc.ShowElements(sectionBoxTargets);
          }
          }
        }
        // Камера не задана — открываем 3D и зумим к рамке / точке из BCF (элементы — только при изоляции)
        else if (TryOpenFallback3DView(
          uidoc,
          doc,
          uniqueView,
          viewpoint,
          resolvedSectionBox,
          sectionBoxTargets,
          out openedView3D))
        {
          cameraViewOpened = true;
        }
        else if (viewpoint.Components == null)
          return;

        if (!cameraViewOpened && viewpoint.Components == null)
          return;

        // Обычный «3D»: камера + обрезка, без временной изоляции Revit.
        if (!applyVisibilityIsolation)
        {
          try { uidoc.RefreshActiveView(); } catch { /* ignore */ }
          return;
        }

        // «3D с изоляцией»: обрезка уже применена выше + Temporary Hide/Isolate Revit.
        View isolationView = openedView3D ?? uidoc.ActiveView;
        ApplyTemporaryIsolation(uidoc, isolationView, viewpoint, elementsToSelect, visibilityExceptions);
        try { uidoc.RefreshActiveView(); } catch { /* ignore */ }

        // Один диалог после ориентации, изоляции и выделения
        if (unresolvedComponents != null && unresolvedComponents.Count > 0)
          ShowUnresolvedComponentsDialog(unresolvedComponents);
      }
      catch (Exception ex)
      {
        RevitExceptionUi.Show(ex);
      }
    }

    /// <summary>
    /// Временная изоляция/скрытие через инструменты Revit (внутри Transaction).
    /// </summary>
    private static void ApplyTemporaryIsolation(
      UIDocument uidoc,
      View view,
      VisualizationInfo viewpoint,
      IList<ElementId> elementsToSelect,
      IList<ElementId> visibilityExceptions)
    {
      if (uidoc == null || view == null)
        return;

      Document doc = uidoc.Document;
      List<ElementId> selection = (elementsToSelect ?? Array.Empty<ElementId>())
        .Where(id => id != null && id != ElementId.InvalidElementId)
        .Distinct()
        .ToList();
      List<ElementId> exceptions = (visibilityExceptions ?? Array.Empty<ElementId>())
        .Where(id => id != null && id != ElementId.InvalidElementId)
        .Distinct()
        .ToList();

      try
      {
        using (var trans = new Transaction(doc, "BCF temporary isolation"))
        {
          if (trans.Start() != TransactionStatus.Started)
            return;

          ComponentVisibility visibility = viewpoint?.Components?.Visibility;
          if (visibility != null && visibility.DefaultVisibilitySpecified)
          {
            if (visibility.DefaultVisibility)
            {
              // Default visible → временно скрыть Exceptions
              List<ElementId> elementsToHide = exceptions
                .Where(id => doc.GetElement(id)?.CanBeHidden(view) == true)
                .ToList();
              if (elementsToHide.Count > 0)
                view.HideElementsTemporary(elementsToHide);
            }
            else
            {
              // Default hidden → временно изолировать Exceptions ∪ Selection
              List<ElementId> elementsToShow = exceptions
                .Concat(selection)
                .Where(id => doc.GetElement(id)?.CanBeHidden(view) == true)
                .Distinct()
                .ToList();
              if (elementsToShow.Count > 0)
                view.IsolateElementsTemporary(elementsToShow);
            }
          }
          else if (selection.Count > 0)
          {
            // Нет явного Visibility → изолируем Selection
            List<ElementId> elementsToShow = selection
              .Where(id => doc.GetElement(id)?.CanBeHidden(view) == true)
              .ToList();
            if (elementsToShow.Count > 0)
              view.IsolateElementsTemporary(elementsToShow);
          }
          else if (exceptions.Count > 0)
          {
            // Только Exceptions без флага DefaultVisibility → считаем их целевыми
            List<ElementId> elementsToShow = exceptions
              .Where(id => doc.GetElement(id)?.CanBeHidden(view) == true)
              .ToList();
            if (elementsToShow.Count > 0)
              view.IsolateElementsTemporary(elementsToShow);
          }

          trans.Commit();
        }

        if (selection.Count > 0)
          uidoc.Selection.SetElementIds(selection);
      }
      catch (Exception ex)
      {
        RevitExceptionUi.Show(
          "Не удалось применить временную изоляцию Revit.\n" + ExceptionUi.Format(ex));
      }
    }

    /// <summary>
    /// Читает векторы камеры из BCF. Если up не указан — берём мировую ось Z вверх.
    /// </summary>
    private static bool TryGetCameraVectors(
      Bcfier.Bcf.Bcf2.Point cameraViewPoint,
      Direction cameraDirection,
      Direction cameraUpVector,
      out XYZ viewPoint,
      out XYZ direction,
      out XYZ upVector)
    {
      viewPoint = null;
      direction = null;
      upVector = null;

      if (cameraViewPoint == null)
        return false;

      BcfCoordinateMode coordinateMode = GetCoordinateModeFromSettings();

      // Direction обязателен; Up по умолчанию — Z вверх (Revit Z-up).
      direction = RevitUtils.MapBcfVectorToRevit(
        RevitUtils.GetBcfDirection(cameraDirection),
        coordinateMode);
      if (direction == null)
        return false;

      upVector = RevitUtils.MapBcfVectorToRevit(
        RevitUtils.GetBcfDirectionOrDefault(cameraUpVector, XYZ.BasisZ),
        coordinateMode);
      viewPoint = RevitUtils.MapBcfPointToRevit(
        RevitUtils.GetRevitXYZ(cameraViewPoint),
        coordinateMode);
      return viewPoint != null && upVector != null;
    }

    /// <summary>
    /// Читает пользовательский пресет осей для импорта BCF-камеры.
    /// </summary>
    private static BcfCoordinateMode GetCoordinateModeFromSettings()
    {
      string raw = Bcfier.Data.BcfCoordinateSettings.GetMode();
      switch (raw)
      {
        case "SourceYUp":
          return BcfCoordinateMode.SourceYUp;
        case "SourceXUp":
          return BcfCoordinateMode.SourceXUp;
        default:
          return BcfCoordinateMode.RevitZUp;
      }
    }

    /// <summary>
    /// Открывает лист с зумом, если в BCF заданы координаты SheetCamera и вид есть в документе.
    /// </summary>
    private static bool TryOpenSheetView(UIDocument uidoc, Document doc, SheetCamera sheetCamera)
    {
      if (!TryFindSheetView(doc, sheetCamera, out View sheetView))
        return false;

      uidoc.ActiveView = sheetView;
      try { uidoc.RefreshActiveView(); } catch { /* не во всех версиях Revit */ }

      if (HasValidSheetZoomCorners(sheetCamera))
      {
        XYZ m_xyzTl = new XYZ(sheetCamera.TopLeft.X, sheetCamera.TopLeft.Y, sheetCamera.TopLeft.Z);
        XYZ m_xyzBr = new XYZ(sheetCamera.BottomRight.X, sheetCamera.BottomRight.Y, sheetCamera.BottomRight.Z);
        ZoomUiView(uidoc, sheetView.Id, m_xyzTl, m_xyzBr);
      }

      return true;
    }

    /// <summary>
    /// Ищет вид по SheetID / имени SheetCamera в активном документе.
    /// </summary>
    private static bool TryFindSheetView(Document doc, SheetCamera sheetCamera, out View sheetView)
    {
      sheetView = null;
      if (doc == null || sheetCamera == null)
        return false;

      IEnumerable<View> viewcollectorSheet = getSheets(doc, sheetCamera.SheetID, sheetCamera.SheetName);
      if (!viewcollectorSheet.Any())
        return false;

      sheetView = viewcollectorSheet.First();
      return sheetView != null;
    }

    /// <summary>
    /// Проверяет, что у SheetCamera задан прямоугольник зума.
    /// </summary>
    private static bool HasValidSheetZoomCorners(SheetCamera sheetCamera)
    {
      if (sheetCamera?.TopLeft == null || sheetCamera.BottomRight == null)
        return false;

      var topLeft = new XYZ(sheetCamera.TopLeft.X, sheetCamera.TopLeft.Y, sheetCamera.TopLeft.Z);
      var bottomRight = new XYZ(sheetCamera.BottomRight.X, sheetCamera.BottomRight.Y, sheetCamera.BottomRight.Z);
      return topLeft.DistanceTo(bottomRight) > 1e-6;
    }

    /// <summary>
    /// Открывает 3D без камеры BCF и зумит к элементам, section box или точке из viewpoint.
    /// </summary>
    private static bool TryOpenFallback3DView(
      UIDocument uidoc,
      Document doc,
      bool uniqueView,
      VisualizationInfo viewpoint,
      BoundingBoxXYZ sectionBox,
      IList<ElementId> elementTargets,
      out View3D openedView3D)
    {
      openedView3D = null;
      if (!HasFallbackZoomTarget(viewpoint, sectionBox, elementTargets, doc, out XYZ focusPoint))
        return false;

      View3D view3D = null;
      using (var trans = new Transaction(doc))
      {
        if (trans.Start("Open BCF fallback 3D view") != TransactionStatus.Started)
          return false;

        view3D = GetOrCreateWorking3DView(uidoc, doc, uniqueView);
        if (view3D == null)
        {
          trans.RollBack();
          return false;
        }

        view3D.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
        ApplySectionBox(view3D, sectionBox);
        trans.Commit();
      }

      openedView3D = view3D;
      if (uidoc.ActiveView == null || uidoc.ActiveView.Id != view3D.Id)
        uidoc.ActiveView = view3D;
      try { uidoc.RefreshActiveView(); } catch { /* не во всех версиях Revit */ }

      ZoomToFallbackTargets(uidoc, view3D, elementTargets, sectionBox, focusPoint);
      return true;
    }

    /// <summary>
    /// Есть ли к чему зумить без камеры: элементы, рамка или точка из BCF.
    /// </summary>
    private static bool HasFallbackZoomTarget(
      VisualizationInfo viewpoint,
      BoundingBoxXYZ sectionBox,
      IList<ElementId> elementTargets,
      Document doc,
      out XYZ focusPoint)
    {
      focusPoint = null;
      if (elementTargets != null && elementTargets.Any())
        return true;
      if (sectionBox != null)
        return true;
      return TryGetFocusPointFromViewpoint(doc, viewpoint, out focusPoint);
    }

    /// <summary>
    /// Зум без камеры: сначала элементы, затем рамка, затем точка.
    /// </summary>
    private static void ZoomToFallbackTargets(
      UIDocument uidoc,
      View3D view3D,
      IList<ElementId> elementTargets,
      BoundingBoxXYZ sectionBox,
      XYZ focusPoint)
    {
      if (elementTargets != null && elementTargets.Count > 0 && elementTargets.Count <= 300)
      {
        uidoc.ShowElements(elementTargets);
        return;
      }

      if (sectionBox != null)
      {
        ZoomToBoundingBox(uidoc, view3D, sectionBox);
        return;
      }

      if (focusPoint != null)
        ZoomAroundPoint(uidoc, view3D, focusPoint);
    }

    /// <summary>
    /// Извлекает точку фокуса из ClippingPlanes, Lines или CameraViewPoint без направления.
    /// </summary>
    private static bool TryGetFocusPointFromViewpoint(Document doc, VisualizationInfo viewpoint, out XYZ focusPoint)
    {
      focusPoint = null;
      if (doc == null || viewpoint == null)
        return false;

      var points = new List<XYZ>();

      if (viewpoint.ClippingPlanes != null)
      {
        foreach (ClippingPlane plane in viewpoint.ClippingPlanes)
        {
          if (plane?.Location == null)
            continue;
          try
          {
            XYZ feet = RevitUtils.GetRevitXYZ(plane.Location);
            points.Add(RevitUtils.ConvertPointBasePoint(doc, feet, true));
          }
          catch
          {
            // ignore invalid plane point
          }
        }
      }

      if (viewpoint.Lines != null)
      {
        foreach (Bcfier.Bcf.Bcf2.Line line in viewpoint.Lines)
        {
          TryAddBcfPoint(doc, line?.StartPoint, points);
          TryAddBcfPoint(doc, line?.EndPoint, points);
        }
      }

      TryAddBcfPoint(doc, viewpoint.OrthogonalCamera?.CameraViewPoint, points);
      TryAddBcfPoint(doc, viewpoint.PerspectiveCamera?.CameraViewPoint, points);

      if (!points.Any())
        return false;

      focusPoint = new XYZ(
        points.Average(p => p.X),
        points.Average(p => p.Y),
        points.Average(p => p.Z));
      return true;
    }

    /// <summary>
    /// Добавляет точку BCF в список, если координаты заданы.
    /// </summary>
    private static void TryAddBcfPoint(Document doc, Bcfier.Bcf.Bcf2.Point point, ICollection<XYZ> target)
    {
      if (doc == null || point == null || target == null)
        return;
      if (Math.Abs(point.X) < 1e-9 && Math.Abs(point.Y) < 1e-9 && Math.Abs(point.Z) < 1e-9)
        return;

      try
      {
        XYZ feet = RevitUtils.GetRevitXYZ(point);
        target.Add(RevitUtils.ConvertPointBasePoint(doc, feet, true));
      }
      catch
      {
        // ignore invalid point
      }
    }

    /// <summary>
    /// Зум UIView по ограничивающей рамке section box в model space.
    /// </summary>
    private static void ZoomToBoundingBox(UIDocument uidoc, View3D view3D, BoundingBoxXYZ box)
    {
      if (uidoc == null || view3D == null || box == null)
        return;

      Transform transform = box.Transform ?? Transform.Identity;
      XYZ min = box.Min;
      XYZ max = box.Max;
      XYZ worldMin = null;
      XYZ worldMax = null;

      for (int i = 0; i < 8; i++)
      {
        XYZ corner = new XYZ(
          (i & 1) == 0 ? min.X : max.X,
          (i & 2) == 0 ? min.Y : max.Y,
          (i & 4) == 0 ? min.Z : max.Z);
        corner = transform.OfPoint(corner);

        if (worldMin == null)
        {
          worldMin = corner;
          worldMax = corner;
          continue;
        }

        worldMin = new XYZ(
          Math.Min(worldMin.X, corner.X),
          Math.Min(worldMin.Y, corner.Y),
          Math.Min(worldMin.Z, corner.Z));
        worldMax = new XYZ(
          Math.Max(worldMax.X, corner.X),
          Math.Max(worldMax.Y, corner.Y),
          Math.Max(worldMax.Z, corner.Z));
      }

      if (worldMin == null || worldMax == null)
        return;

      ZoomUiView(uidoc, view3D.Id, worldMax, worldMin);
    }

    /// <summary>
    /// Зум к точке небольшим прямоугольником вокруг неё в плоскости текущего 3D-вида.
    /// </summary>
    private static void ZoomAroundPoint(UIDocument uidoc, View3D view3D, XYZ center, double halfSizeFeet = 10.0)
    {
      if (uidoc == null || view3D == null || center == null)
        return;

      if (halfSizeFeet <= 0)
        halfSizeFeet = 10.0;

      XYZ topLeft = center
        .Add(view3D.UpDirection.Multiply(halfSizeFeet))
        .Subtract(view3D.RightDirection.Multiply(halfSizeFeet));
      XYZ bottomRight = center
        .Subtract(view3D.UpDirection.Multiply(halfSizeFeet))
        .Add(view3D.RightDirection.Multiply(halfSizeFeet));
      ZoomUiView(uidoc, view3D.Id, topLeft, bottomRight);
    }

    /// <summary>
    /// Зум ортогонального 3D после перспективной камеры BCF: рамка, иначе высота кадра из FOV.
    /// </summary>
    private static void ZoomPerspectiveAsOrthogonal(
      UIDocument uidoc,
      View3D view3D,
      double fieldOfViewDegrees,
      BoundingBoxXYZ sectionBox,
      XYZ eye)
    {
      try
      {
        if (uidoc == null || view3D == null)
          return;

        if (sectionBox != null)
        {
          ZoomToBoundingBox(uidoc, view3D, sectionBox);
          return;
        }

        double fov = fieldOfViewDegrees;
        if (fov <= 1 || fov >= 179 || double.IsNaN(fov))
          fov = 60;

        double distanceMeters = 10;
        if (eye != null)
        {
          XYZ focus = view3D.Origin ?? eye;
          double distanceFeet = eye.DistanceTo(focus);
          if (distanceFeet > 1e-3)
            distanceMeters = Math.Max(distanceFeet.ToMeters(), 0.5);
        }

        double viewHeightMeters = 2.0 * distanceMeters * Math.Tan(fov * Math.PI / 360.0);
        if (viewHeightMeters <= 1e-3 || double.IsNaN(viewHeightMeters))
          viewHeightMeters = 10;

        ZoomAroundPoint(uidoc, view3D, view3D.Origin, viewHeightMeters.ToFeet() / 2.0);
      }
      catch
      {
        // Зум не должен ронять открытие вида
      }
    }

    /// <summary>
    /// Сопоставляет компоненты BCF с элементами модели.
    /// Ненайденные компоненты возвращает в unresolved (без показа диалога).
    /// </summary>
    private static Dictionary<Component, ElementId> ResolveComponents(
      Document doc,
      Components components,
      out List<Component> unresolved)
    {
      unresolved = new List<Component>();
      if (doc == null || components == null)
        return new Dictionary<Component, ElementId>();

      List<Component> allComponents = (components.Selection ?? Array.Empty<Component>())
        .Concat(components.Visibility?.Exceptions ?? Array.Empty<Component>())
        .Where(component => component != null)
        .Distinct()
        .ToList();

      Dictionary<Component, ElementId> result = BcfElementResolver.ResolveBatch(doc, allComponents);

      List<Component> stillMissing = allComponents
        .Where(component => !result.ContainsKey(component)
          && (!string.IsNullOrWhiteSpace(component.IfcGuid) || !string.IsNullOrWhiteSpace(component.AuthoringToolId)))
        .ToList();

      List<Component> selectionMissing = (components.Selection ?? Array.Empty<Component>())
        .Where(component => component != null && stillMissing.Contains(component))
        .Distinct()
        .ToList();

      unresolved = selectionMissing.Any() ? selectionMissing : stillMissing;
      return result;
    }

    /// <summary>
    /// Сообщает, что компоненты BCF не удалось сопоставить ни по GUID, ни по Id.
    /// </summary>
    private static void ShowUnresolvedComponentsDialog(IList<Component> missing)
    {
      if (missing == null || missing.Count == 0)
        return;

      try
      {
        const int maxLines = 15;
        var lines = missing
          .Take(maxLines)
          .Select(component =>
          {
            string guid = string.IsNullOrWhiteSpace(component.IfcGuid) ? "—" : component.IfcGuid;
            string id = string.IsNullOrWhiteSpace(component.AuthoringToolId) ? "—" : component.AuthoringToolId;
            return "IfcGuid: " + guid + ", Id: " + id;
          });

        string list = string.Join("\n", lines);
        if (missing.Count > maxLines)
          list += "\n…";

        string message = Loc.Format("ComponentsNotFoundMessage", missing.Count, list);
        RevitExceptionUi.Show(message, Loc.Get("ComponentsNotFoundTitle"));
      }
      catch
      {
        // Диалог не должен срывать открытие вида
      }
    }

    private static List<ElementId> GetResolvedIds(
      IEnumerable<Component> components,
      IReadOnlyDictionary<Component, ElementId> resolvedComponents)
    {
      if (components == null)
        return new List<ElementId>();

      return components
        .Where(component => component != null && resolvedComponents.ContainsKey(component))
        .Select(component => resolvedComponents[component])
        .Distinct()
        .ToList();
    }

    /// <summary>
    /// Рабочий 3D-вид: текущий ортогональный 3D, иначе существующий/новый 3D_BCF_ИмяПользователя.
    /// Виды «Камера» (perspective) не берём: ориентацию задаём только на изометрии.
    /// Вызывать внутри открытой Transaction при создании вида.
    /// </summary>
    private static View3D GetOrCreateWorking3DView(UIDocument uidoc, Document doc, bool uniqueView)
    {
      if (!uniqueView && TryGetCurrentOrtho3DView(uidoc, out View3D current3D))
        return current3D;

      string userViewName = BuildUser3DViewName(doc);
      View3D existing = uniqueView ? null : Find3DView(doc, userViewName, false);
      if (existing != null)
        return existing;

      ViewFamilyType familyType = getFamilyViews(doc).FirstOrDefault();
      if (familyType == null)
        return null;

      View3D created = View3D.CreateIsometric(doc, familyType.Id);
      created.Name = GetUniqueViewName(
        doc,
        uniqueView
          ? userViewName + " - BCF " + DateTime.Now.ToString("yyyyMMdd-HHmmss")
          : userViewName);
      return created;
    }

    /// <summary>
    /// Возвращает текущий ортогональный 3D-вид, если он активен.
    /// </summary>
    private static bool TryGetCurrentOrtho3DView(UIDocument uidoc, out View3D view3D)
    {
      view3D = null;
      if (uidoc?.ActiveView is View3D active3D
          && !active3D.IsTemplate
          && !active3D.IsPerspective)
      {
        view3D = active3D;
        return true;
      }

      return false;
    }

    /// <summary>
    /// Проверяет, что целевой вид уже является активным ортогональным 3D.
    /// </summary>
    private static bool IsCurrentOrtho3DView(UIDocument uidoc, View3D view3D)
    {
      return view3D != null
        && uidoc?.ActiveView != null
        && uidoc.ActiveView.Id == view3D.Id;
    }

    /// <summary>
    /// Имя рабочего вида: 3D_BCF_ИмяПользователя Revit.
    /// </summary>
    private static string BuildUser3DViewName(Document doc)
    {
      string username = doc?.Application?.Username;
      if (string.IsNullOrWhiteSpace(username))
        username = "Пользователь";

      int separator = username.LastIndexOf('\\');
      if (separator >= 0 && separator < username.Length - 1)
        username = username.Substring(separator + 1);

      char[] prohibited = { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };
      foreach (char prohibitedChar in prohibited)
        username = username.Replace(prohibitedChar.ToString(), "_");

      username = username.Trim();
      return "3D_BCF_" + (string.IsNullOrWhiteSpace(username) ? "Пользователь" : username);
    }

    private static View3D Find3DView(Document doc, string name, bool perspective)
    {
      return new FilteredElementCollector(doc)
        .OfClass(typeof(View3D))
        .Cast<View3D>()
        .FirstOrDefault(view =>
          !view.IsTemplate
          && view.IsPerspective == perspective
          && string.Equals(view.Name?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string GetUniqueViewName(Document doc, string requestedName)
    {
      var occupiedNames = new HashSet<string>(
        new FilteredElementCollector(doc)
          .OfClass(typeof(View))
          .Cast<View>()
          .Select(view => view.Name),
        StringComparer.OrdinalIgnoreCase);

      if (!occupiedNames.Contains(requestedName))
        return requestedName;

      int suffix = 2;
      while (occupiedNames.Contains(requestedName + " (" + suffix + ")"))
        suffix++;
      return requestedName + " (" + suffix + ")";
    }

    /// <summary>
    /// Строит 3D-рамку по выбранным компонентам так же, как ClashViewer.
    /// </summary>
    private static BoundingBoxXYZ BuildElementsBoundingBox(Document doc, IEnumerable<ElementId> ids)
    {
      BoundingBoxXYZ result = null;
      foreach (ElementId id in ids ?? Enumerable.Empty<ElementId>())
      {
        BoundingBoxXYZ elementBox = doc.GetElement(id)?.get_BoundingBox(null);
        if (elementBox == null)
          continue;

        if (result == null)
        {
          result = new BoundingBoxXYZ { Min = elementBox.Min, Max = elementBox.Max };
          continue;
        }

        result.Min = new XYZ(
          Math.Min(result.Min.X, elementBox.Min.X),
          Math.Min(result.Min.Y, elementBox.Min.Y),
          Math.Min(result.Min.Z, elementBox.Min.Z));
        result.Max = new XYZ(
          Math.Max(result.Max.X, elementBox.Max.X),
          Math.Max(result.Max.Y, elementBox.Max.Y),
          Math.Max(result.Max.Z, elementBox.Max.Z));
      }

      return result;
    }

    /// <summary>
    /// Включает section box из BCF ClippingPlanes; если плоскостей нет — выключает рамку.
    /// </summary>
    private static void ApplySectionBox(View3D view, BoundingBoxXYZ sectionBox)
    {
      if (view == null)
        return;

      if (sectionBox == null)
      {
        view.IsSectionBoxActive = false;
        return;
      }

      // Сначала задаём геометрию, затем активируем (порядок важен в части версий Revit).
      view.SetSectionBox(sectionBox);
      view.IsSectionBoxActive = true;
    }

    /// <summary>
    /// Зум именно той вкладки, которую открыли (не «первой открытой» в Revit).
    /// Двойной ZoomAndCenterRectangle — как в ClashViewer: в ряде версий первый вызов после смены вида игнорируется.
    /// </summary>
    private static void ZoomUiView(UIDocument uidoc, ElementId viewId, XYZ topLeft, XYZ bottomRight)
    {
      if (uidoc == null || viewId == null || topLeft == null || bottomRight == null)
        return;

      UIView uiView = uidoc.GetOpenUIViews()?.FirstOrDefault(open => open.ViewId == viewId);
      if (uiView == null)
        return;

      uiView.ZoomAndCenterRectangle(topLeft, bottomRight);
      uiView.ZoomAndCenterRectangle(topLeft, bottomRight);
    }

    private static IEnumerable<ViewFamilyType> getFamilyViews(Document doc)
    {

      return from elem in new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
             let type = elem as ViewFamilyType
             where type.ViewFamily == ViewFamily.ThreeDimensional
             select type;
    }
    private static IEnumerable<View> getSheets(Document doc, int id, string stname)
    {
      ElementId eid = RevitIdHelper.FromInt(id);
      return from elem in new FilteredElementCollector(doc).OfClass(typeof(View))
             let view = elem as View
             //Get the view with the given Id or given name
             where view.Id == eid | view.Name == stname
             select view;
      
    }




    public string GetName()
    {
      return "Open 3D View";
    }
    // returns XYZ and ZOOM/FOV value
  }

}