using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Bcfier.Revit.Data;
using Bcfier.Revit.Entry;
using Bcfier.Windows;

namespace Bcfier.Revit.Host
{
    /// <summary>
    /// Revit-реализация окна выбора элементов viewpoint.
    /// </summary>
    public static class RevitComponentListHost
    {
        private static ExternalEvent _selectEvent;
        private static ExtEvntSelectElements _selectHandler;
        private static ExternalEvent _refreshLinksEvent;
        private static ExtEvntRefreshComponentLinks _refreshLinksHandler;
        private static ExternalEvent _pickEvent;
        private static ExtEvntPickModelElements _pickHandler;
        private static UIApplication _uiApp;

        public static void Register(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _selectHandler = new ExtEvntSelectElements();
            _selectEvent = ExternalEvent.Create(_selectHandler);
            _refreshLinksHandler = new ExtEvntRefreshComponentLinks();
            _refreshLinksEvent = ExternalEvent.Create(_refreshLinksHandler);
            // ExternalEvent нельзя создавать из modeless WPF — только здесь, в контексте команды Revit
            _pickHandler = new ExtEvntPickModelElements();
            _pickEvent = ExternalEvent.Create(_pickHandler);
            ComponentListHost.SelectInModel = SelectInModel;
            ComponentListHost.ResolveElementId = ResolveElementId;
            ComponentListHost.RunWithRevitContext = ScheduleRefreshComponentLinks;
            ComponentListHost.TryGetCachedElementId = TryGetCachedElementId;

            ComponentListHost.CreateWindow = (components, editMode) =>
            {
                return new ComponentsList(
                    components,
                    editMode,
                    editMode ? selected => ApplySelection(components, selected) : null,
                    ids => SelectInModel(ids, false));
            };
        }

        /// <summary>
        /// Возвращает заранее созданный ExternalEvent для PickObjects (диалог «Добавить вид»).
        /// </summary>
        public static bool TryGetPickModelElementsEvent(
            out ExtEvntPickModelElements handler,
            out ExternalEvent extEvent)
        {
            handler = _pickHandler;
            extEvent = _pickEvent;
            return handler != null && extEvent != null;
        }

        /// <summary>
        /// Находит ElementId компонента по AuthoringToolId или IfcGuid в активной модели.
        /// </summary>
        private static int? ResolveElementId(Component component)
        {
            Document doc = _uiApp?.ActiveUIDocument?.Document;
            return BcfElementResolver.ResolveElementId(doc, component);
        }

        /// <summary>
        /// Возвращает закэшированный ElementId без обращения к Revit API.
        /// </summary>
        private static (bool found, int? elementId) TryGetCachedElementId(Component component)
        {
            if (BcfElementResolver.TryGetCachedResolve(component, out int? elementId))
                return (true, elementId);

            return (false, null);
        }

        /// <summary>
        /// Планирует обновление ссылок компонентов через ExternalEvent Revit.
        /// </summary>
        public static void ScheduleRefreshComponentLinks(Action uiRefreshAction, IList<Component> components)
        {
            if (uiRefreshAction == null || _refreshLinksEvent == null)
                return;

            Document doc = _uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                try
                {
                    BcfElementResolver.MarkUnresolved(components);
                }
                catch
                {
                    // Ошибка кэша не должна блокировать открытие BCF
                }

                uiRefreshAction();
                return;
            }

            // Всегда запускаем resolve: даже в «почти пустом» файле Id находятся через GetElement,
            // а полный GUID-скан внутри ResolveBatch ограничен и безопасен.
            _refreshLinksHandler.Components = components?.ToList();
            _refreshLinksHandler.UiRefreshAction = uiRefreshAction;
            _refreshLinksEvent.Raise();
        }


        /// <summary>
        /// Прогревает кэш id/guid только для компонентов BCF, переданных из UI.
        /// </summary>
        public static void WarmCaches(UIApplication app, IList<Component> components)
        {
            Document doc = app?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                BcfElementResolver.MarkUnresolved(components);
                return;
            }

            if (components != null && components.Count > 0)
                BcfElementResolver.ResolveBatch(doc, components);
            else
                BcfElementResolver.ResetForDocument(doc);
        }

        /// <summary>
        /// Элемент модели, пригодный для полного GUID/name-скана.
        /// Id по AuthoringToolId ищутся отдельно через GetElement и не зависят от этой проверки.
        /// </summary>
        internal static bool IsSearchableModelElement(Element element)
        {
            try
            {
                if (element == null || element.ViewSpecific)
                    return false;

                if (element is RevitLinkInstance || element is ImportInstance || element is Group)
                    return false;

                Category category = element.Category;
                if (category == null || category.CategoryType != CategoryType.Model)
                    return false;

                int categoryId = category.Id.GetValue();
                if (categoryId == (int)BuiltInCategory.OST_Levels
                    || categoryId == (int)BuiltInCategory.OST_Grids
                    || categoryId == (int)BuiltInCategory.OST_CLines
                    || categoryId == (int)BuiltInCategory.OST_ReferenceLines
                    || categoryId == (int)BuiltInCategory.OST_Cameras
                    || categoryId == (int)BuiltInCategory.OST_Views
                    || categoryId == (int)BuiltInCategory.OST_RvtLinks)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Выделяет элементы в активном документе Revit.
        /// </summary>
        private static void SelectInModel(List<int> ids, bool append)
        {
            if (_selectHandler == null || _selectEvent == null)
                return;

            _selectHandler.ElementIds = ids ?? new List<int>();
            _selectHandler.Append = append;
            _selectEvent.Raise();
        }

        private static void ApplySelection(Components components, Component[] selected)
        {
            if (components == null)
                return;

            components.Selection = selected ?? Array.Empty<Component>();
        }
    }
}
