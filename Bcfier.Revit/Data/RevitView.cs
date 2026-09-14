using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data.Utils;
using Point = Bcfier.Bcf.Bcf2.Point;

namespace Bcfier.Revit.Data
{
    /// <summary>
    /// Генерация viewpoint из текущего вида Revit.
    /// </summary>
    public static class RevitView
    {
        public static VisualizationInfo GenerateViewpoint(UIDocument uidoc)
        {
            return GenerateViewpoint(uidoc, uidoc?.Selection?.GetElementIds());
        }

        public static VisualizationInfo GenerateViewpoint(UIDocument uidoc, ICollection<ElementId> selectedElementIds)
        {
            Document doc = null;
            ElementId previousViewId = null;
            bool switchedTo3dForViewpoint = false;
            try
            {
                doc = uidoc.Document;
                var v = new VisualizationInfo();
                XYZ topLeft;
                XYZ bottomRight;
                previousViewId = uidoc.ActiveView?.Id;

                // Для некоторых типов видов (например, спецификаций/Schedule) Revit может не предоставлять zoom-углы.
                // В этом случае временно переключаемся на 3D вид, чтобы viewpoint можно было сгенерировать.
                if (!TryGetZoomCorners(uidoc, out topLeft, out bottomRight))
                {
                    try
                    {
                        View3D temp3d = EnsureUsable3DView(doc);
                        if (temp3d == null)
                            return null;

                        uidoc.ActiveView = temp3d;
                        try { uidoc.RefreshActiveView(); } catch { /* не во всех версиях Revit */ }

                        if (!TryGetZoomCorners(uidoc, out topLeft, out bottomRight))
                            return null;

                        switchedTo3dForViewpoint = true;
                    }
                    catch
                    {
                        return null;
                    }
                }

                if (uidoc.ActiveView.ViewType != ViewType.ThreeD)
                {
                    v.SheetCamera = new SheetCamera
                    {
                        SheetID = uidoc.ActiveView.Id.GetValue(),
                        SheetName = uidoc.ActiveView.Name,
                        TopLeft = new Point { X = topLeft.X, Y = topLeft.Y, Z = topLeft.Z },
                        BottomRight = new Point { X = bottomRight.X, Y = bottomRight.Y, Z = bottomRight.Z }
                    };
                }
                else
                {
                    BuildCamera(uidoc, v, topLeft, bottomRight, doc);
                    // Активный section box → ClippingPlanes (Transform + base point)
                    var view3D = uidoc.ActiveView as View3D;
                    ClippingPlane[] planes = SectionBoxClipping.TryFromView3D(doc, view3D);
                    if (planes != null)
                        v.ClippingPlanes = planes;
                }

                string versionName = doc.Application.VersionName;
                // Тот же фильтр, что в UI «Элементы вида»: только элементы с семейством и типом.
                var visibleElems = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .WhereElementIsViewIndependent()
                    .Where(HasUiComponentNames)
                    .Select(x => x.Id)
                    .ToList();

                var hiddenElems = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WhereElementIsViewIndependent()
                    .Where(x => x.IsHidden(doc.ActiveView)
                        || !doc.ActiveView.IsElementVisibleInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate, x.Id))
                    .Where(HasUiComponentNames)
                    .Select(x => x.Id)
                    .ToList();

                var selectedElems = (selectedElementIds ?? new List<ElementId>())
                    .Where(id => HasUiComponentNames(doc.GetElement(id)))
                    .ToList();

                v.Components = new Components { Visibility = new ComponentVisibility() };

                // Компактное хранение: меньший список идёт в Exceptions
                if (visibleElems.Count > hiddenElems.Count)
                {
                    v.Components.Visibility.DefaultVisibility = true;
                    v.Components.Visibility.DefaultVisibilitySpecified = true;
                    v.Components.Visibility.Exceptions = hiddenElems
                        .Select(x => ToComponent(doc, x, versionName))
                        .ToArray();
                }
                else
                {
                    v.Components.Visibility.DefaultVisibility = false;
                    v.Components.Visibility.DefaultVisibilitySpecified = true;
                    v.Components.Visibility.Exceptions = visibleElems
                        .Select(x => ToComponent(doc, x, versionName))
                        .ToArray();
                }

                v.Components.Selection = selectedElems
                    .Select(x => ToComponent(doc, x, versionName))
                    .ToArray();

                // Если изначально был непригодный вид (например, спецификация), мы построили viewpoint из временного 3D.
                // Возвращаем исходный вид после генерации.
                if (switchedTo3dForViewpoint && previousViewId != null)
                {
                    try
                    {
                        var prev = doc.GetElement(previousViewId) as View;
                        if (prev != null)
                            uidoc.ActiveView = prev;
                    }
                    catch { /* ignore */ }
                }

                return v;
            }
            catch (Exception ex)
            {
                if (switchedTo3dForViewpoint && previousViewId != null)
                {
                    try
                    {
                        var prev = doc.GetElement(previousViewId) as View;
                        if (prev != null)
                            uidoc.ActiveView = prev;
                    }
                    catch { /* ignore */ }
                }

                RevitExceptionUi.Show(ex, "Error generating viewpoint");
            }

            return null;
        }

        /// <summary>
        /// Пытается извлечь top-left и bottom-right углы прямоугольника зума из активного интерфейса.
        /// Для schedule/specifications Revit иногда не возвращает UIView/zoom-corners, поэтому возвращается false.
        /// </summary>
        private static bool TryGetZoomCorners(UIDocument uidoc, out XYZ topLeft, out XYZ bottomRight)
        {
            topLeft = null;
            bottomRight = null;

            if (uidoc == null)
                return false;

            var openViews = uidoc.GetOpenUIViews();
            if (openViews == null || !openViews.Any())
                return false;

            var first = openViews.First();
            if (first == null)
                return false;

            var corners = first.GetZoomCorners();
            if (corners == null || corners.Count < 2)
                return false;

            topLeft = corners[0];
            bottomRight = corners[1];
            return topLeft != null && bottomRight != null;
        }

        /// <summary>
        /// Находит ортогональный 3D-вид или создаёт изометрический.
        /// Виды «Камера» (perspective) не используем: в них нельзя задать ортопроекцию.
        /// </summary>
        private static View3D EnsureUsable3DView(Document doc)
        {
            if (doc == null)
                return null;

            try
            {
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .Where(x => !x.IsTemplate && !x.IsPerspective)
                    .OrderBy(x => string.Equals(x.Name, "{3D}", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .FirstOrDefault();
                if (existing != null)
                    return existing;

                ViewFamilyType familyType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

                if (familyType == null)
                    return null;

                View3D created = null;
                using (var trans = new Transaction(doc, "Create temporary 3D view"))
                {
                    if (trans.Start() == TransactionStatus.Started)
                    {
                        created = View3D.CreateIsometric(doc, familyType.Id);
                        if (created != null)
                            created.Name = GetUniqueTemporaryViewName(doc, "3D - BCFier temp");

                        trans.Commit();
                    }
                }

                return created;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Генерирует имя временного вида, чтобы избежать конфликта имен в проекте.
        /// </summary>
        private static string GetUniqueTemporaryViewName(Document doc, string baseName)
        {
            if (doc == null || string.IsNullOrWhiteSpace(baseName))
                return "3D - BCFier temp";

            var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var view in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
                occupied.Add(view?.Name);

            if (!occupied.Contains(baseName))
                return baseName;

            int suffix = 2;
            while (occupied.Contains(baseName + " (" + suffix + ")"))
                suffix++;

            return baseName + " (" + suffix + ")";
        }

        /// <summary>Максимум элементов в раскрывающемся списке «Элементы вида».</summary>
        public const int MaxCandidateComponents = 100;

        /// <summary>
        /// Собирает кандидатов вида, попадающих в видимую область скриншота (GetZoomCorners).
        /// При превышении лимита возвращает пустой массив и exceedsLimit = true.
        /// </summary>
        public static Component[] BuildCandidateComponents(
            UIDocument uidoc,
            out Dictionary<string, string> familyNames,
            out Dictionary<string, string> typeNames,
            out bool exceedsLimit)
        {
            Document doc = uidoc.Document;
            View view = uidoc.ActiveView;
            string versionName = doc.Application.VersionName;
            familyNames = new Dictionary<string, string>();
            typeNames = new Dictionary<string, string>();
            exceedsLimit = false;
            var components = new List<Component>();

            IList<XYZ> zoomCorners = uidoc.GetOpenUIViews()[0].GetZoomCorners();
            TryGetZoomUvBounds(view, zoomCorners[0], zoomCorners[1],
                out double zoomUMin, out double zoomUMax, out double zoomVMin, out double zoomVMax);

            XYZ right = view.RightDirection;
            XYZ up = view.UpDirection;
            XYZ origin = zoomCorners[0];

            foreach (Element element in new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .WhereElementIsViewIndependent())
            {
                if (!IntersectsVisibleScreenshotRegion(
                        element, view, origin, right, up, zoomUMin, zoomUMax, zoomVMin, zoomVMax))
                    continue;

                Component component = ToComponent(doc, element.Id, versionName);

                // Без семейства и типа элемент не показываем в списке «Элементы вида».
                if (!HasUiComponentNames(element))
                    continue;

                GetElementDisplayNames(element, out string familyName, out string typeName);

                components.Add(component);
                familyNames[component.AuthoringToolId] = familyName;
                typeNames[component.AuthoringToolId] = typeName;

                // Ранний выход: полный список не нужен, достаточно знать о переполнении.
                if (components.Count > MaxCandidateComponents)
                {
                    exceedsLimit = true;
                    familyNames.Clear();
                    typeNames.Clear();
                    return Array.Empty<Component>();
                }
            }

            return components.ToArray();
        }

        /// <summary>
        /// Считает UV-границы прямоугольника зума в плоскости вида.
        /// </summary>
        private static void TryGetZoomUvBounds(
            View view,
            XYZ corner0,
            XYZ corner1,
            out double uMin,
            out double uMax,
            out double vMin,
            out double vMax)
        {
            XYZ right = view.RightDirection;
            XYZ up = view.UpDirection;
            double u0 = 0;
            double v0 = 0;
            XYZ delta = corner1 - corner0;
            double u1 = delta.DotProduct(right);
            double v1 = delta.DotProduct(up);
            uMin = Math.Min(u0, u1);
            uMax = Math.Max(u0, u1);
            vMin = Math.Min(v0, v1);
            vMax = Math.Max(v0, v1);
        }

        /// <summary>
        /// Проверяет, пересекается ли проекция bbox элемента с видимой областью скриншота.
        /// </summary>
        private static bool IntersectsVisibleScreenshotRegion(
            Element element,
            View view,
            XYZ origin,
            XYZ right,
            XYZ up,
            double zoomUMin,
            double zoomUMax,
            double zoomVMin,
            double zoomVMax)
        {
            BoundingBoxXYZ bb = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
            if (bb == null)
                return false;

            XYZ min = bb.Min;
            XYZ max = bb.Max;
            // Transform bbox, если у элемента задан локальный Transform.
            Transform transform = bb.Transform;
            bool hasTransform = transform != null && !transform.IsIdentity;

            double elemUMin = double.MaxValue;
            double elemUMax = double.MinValue;
            double elemVMin = double.MaxValue;
            double elemVMax = double.MinValue;

            for (int i = 0; i < 8; i++)
            {
                XYZ corner = new XYZ(
                    (i & 1) == 0 ? min.X : max.X,
                    (i & 2) == 0 ? min.Y : max.Y,
                    (i & 4) == 0 ? min.Z : max.Z);
                if (hasTransform)
                    corner = transform.OfPoint(corner);

                XYZ relative = corner - origin;
                double u = relative.DotProduct(right);
                double v = relative.DotProduct(up);
                if (u < elemUMin) elemUMin = u;
                if (u > elemUMax) elemUMax = u;
                if (v < elemVMin) elemVMin = v;
                if (v > elemVMax) elemVMax = v;
            }

            return elemUMin <= zoomUMax && elemUMax >= zoomUMin
                && elemVMin <= zoomVMax && elemVMax >= zoomVMin;
        }

        public static ICollection<ElementId> ParseSelectedIds(Document doc, Component[] selected)
        {
            if (selected == null || selected.Length == 0)
                return new List<ElementId>();

            var ids = new List<ElementId>();
            foreach (var component in selected)
            {
                if (string.IsNullOrWhiteSpace(component?.AuthoringToolId))
                    continue;

                if (!int.TryParse(component.AuthoringToolId, out int intId))
                    continue;

                ElementId elementId = RevitIdHelper.FromInt(intId);
                if (doc.GetElement(elementId) != null)
                    ids.Add(elementId);
            }

            return ids;
        }

        /// <summary>
        /// Формирует BCF-компонент из элемента модели со ссылкой на Revit Id.
        /// </summary>
        public static Component ToComponent(Document doc, ElementId id, string versionName)
        {
            int intId = id.GetValue();
            string ifcGuid = null;
            try
            {
                ifcGuid = IfcGuid.ToIfcGuid(ExportUtils.GetExportId(doc, id));
            }
            catch
            {
                // IfcGuid не обязателен для связи по Id
            }

            return new Component
            {
                OriginatingSystem = versionName,
                IfcGuid = ifcGuid,
                AuthoringToolId = intId.ToString(),
                HasModelLink = true,
                LinkedElementId = intId
            };
        }

        /// <summary>
        /// Критерий UI «Элементы вида»: у элемента заданы семейство и тип.
        /// Связи (RevitLinkInstance) и прочие объекты без семейства/типа отсекаются.
        /// </summary>
        public static bool HasUiComponentNames(Element element)
        {
            if (element == null)
                return false;

            GetElementDisplayNames(element, out string familyName, out string typeName);
            return !string.IsNullOrWhiteSpace(familyName) && !string.IsNullOrWhiteSpace(typeName);
        }

        /// <summary>
        /// Возвращает имена семейства и типа элемента для отображения в списке.
        /// </summary>
        public static void GetElementDisplayNames(Element element, out string familyName, out string typeName)
        {
            familyName = string.Empty;
            typeName = string.Empty;
            if (element == null)
                return;

            try
            {
                Element typeElement = element.Document?.GetElement(element.GetTypeId());
                familyName = (typeElement as ElementType)?.FamilyName ?? string.Empty;
                typeName = typeElement?.Name ?? string.Empty;
                if (element is FamilyInstance familyInstance)
                {
                    familyName = familyInstance.Symbol?.Family?.Name ?? familyName;
                    typeName = familyInstance.Symbol?.Name ?? typeName;
                }
            }
            catch
            {
                // Имена нужны только для UI, отсутствие не блокирует связь
            }
        }

        /// <summary>
        /// Записывает ортогональную камеру BCF. Перспективный вид Revit («Камера»)
        /// приводится к ортопроекции с тем же направлением взгляда.
        /// </summary>
        private static void BuildCamera(UIDocument uidoc, VisualizationInfo v, XYZ topLeft, XYZ bottomRight, Document doc)
        {
            try
            {
                var view3D = uidoc?.ActiveView as View3D;
                if (view3D == null || v == null)
                    return;

                if (!TryGetViewCenterAndScale(view3D, topLeft, bottomRight, out XYZ viewCenter, out double zoomValue))
                    return;

                ViewOrientation3D t = RevitUtils.ConvertBasePoint(
                    doc,
                    viewCenter,
                    view3D.ViewDirection,
                    view3D.UpDirection,
                    false);
                if (t == null)
                    return;

                XYZ c = t.EyePosition;
                XYZ vi = t.ForwardDirection;
                XYZ up = t.UpDirection;

                v.OrthogonalCamera = new OrthogonalCamera
                {
                    CameraViewPoint = { X = c.X.ToMeters(), Y = c.Y.ToMeters(), Z = c.Z.ToMeters() },
                    CameraUpVector = { X = up.X, Y = up.Y, Z = up.Z },
                    CameraDirection = { X = vi.X * -1, Y = vi.Y * -1, Z = vi.Z * -1 },
                    ViewToWorldScale = zoomValue
                };
                v.PerspectiveCamera = null;
            }
            catch
            {
                // Камера viewpoint не должна ронять создание замечания
            }
        }

        /// <summary>
        /// Центр и масштаб ортогональной камеры: зум экрана, иначе CropBox, иначе Origin.
        /// </summary>
        private static bool TryGetViewCenterAndScale(
            View3D view3D,
            XYZ topLeft,
            XYZ bottomRight,
            out XYZ viewCenter,
            out double zoomMeters)
        {
            viewCenter = null;
            zoomMeters = 1;
            if (view3D == null)
                return false;

            try
            {
                if (TryGetScaleFromZoomCorners(view3D, topLeft, bottomRight, out viewCenter, out zoomMeters))
                    return true;

                if (TryGetScaleFromCropBox(view3D, out viewCenter, out zoomMeters))
                    return true;

                viewCenter = view3D.Origin;
                zoomMeters = 10;
                return viewCenter != null;
            }
            catch
            {
                viewCenter = null;
                zoomMeters = 1;
                return false;
            }
        }

        /// <summary>
        /// Масштаб по углам текущего зума UIView.
        /// </summary>
        private static bool TryGetScaleFromZoomCorners(
            View3D view3D,
            XYZ topLeft,
            XYZ bottomRight,
            out XYZ viewCenter,
            out double zoomMeters)
        {
            viewCenter = null;
            zoomMeters = 1;
            if (view3D == null || topLeft == null || bottomRight == null)
                return false;

            double distance = topLeft.DistanceTo(bottomRight);
            if (distance < 1e-6)
                return false;

            viewCenter = new XYZ(
                (topLeft.X + bottomRight.X) / 2,
                (topLeft.Y + bottomRight.Y) / 2,
                (topLeft.Z + bottomRight.Z) / 2);

            XYZ diagVector = topLeft.Subtract(bottomRight);
            zoomMeters = (distance / 2) * Math.Sin(diagVector.AngleTo(view3D.RightDirection)).ToMeters();
            return zoomMeters > 1e-6 && !double.IsNaN(zoomMeters);
        }

        /// <summary>
        /// Масштаб по CropBox: для перспективной «Камеры» это высота кадра в плоскости вида.
        /// </summary>
        private static bool TryGetScaleFromCropBox(View3D view3D, out XYZ viewCenter, out double zoomMeters)
        {
            viewCenter = null;
            zoomMeters = 1;
            if (view3D == null)
                return false;

            BoundingBoxXYZ crop = view3D.CropBox;
            if (crop == null)
                return false;

            Transform transform = crop.Transform ?? Transform.Identity;
            XYZ min = crop.Min;
            XYZ max = crop.Max;
            viewCenter = transform.OfPoint((min + max) * 0.5);

            double height = Math.Abs(max.Y - min.Y);
            if (height < 1e-6)
                height = Math.Abs(max.X - min.X);

            zoomMeters = height.ToMeters();
            return viewCenter != null && zoomMeters > 1e-6 && !double.IsNaN(zoomMeters);
        }
    }
}
