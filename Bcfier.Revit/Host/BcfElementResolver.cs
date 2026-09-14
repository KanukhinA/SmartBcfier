using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data.Utils;
using Bcfier.Revit.Data;

namespace Bcfier.Revit.Host
{
    /// <summary>
    /// Сопоставление компонентов BCF с элементами Revit: сначала IfcGuid, затем Id.
    /// Id из описания используется только если GUID в модели не нашёлся.
    /// </summary>
    internal static class BcfElementResolver
    {
        /// <summary>Порог: при большем числе Id строим полный индекс имён за один проход.</summary>
        private const int FullNameTailIndexThreshold = 20;

        private static string _cacheKey;
        private static Dictionary<string, int> _guidToIdIndex;
        private static HashSet<string> _guidMissCache;
        private static Dictionary<long, int> _nameTailIdIndex;
        private static HashSet<long> _nameTailMissCache;
        private static bool _nameTailIndexFullyBuilt;
        private static Dictionary<string, int?> _resolveCache;

        /// <summary>
        /// Сбрасывает кэш при смене документа Revit.
        /// </summary>
        public static void ResetForDocument(Document doc)
        {
            string key = BuildDocumentKey(doc);
            if (string.Equals(_cacheKey, key, StringComparison.Ordinal))
                return;

            _cacheKey = key;
            _guidToIdIndex = null;
            _guidMissCache = null;
            _nameTailIdIndex = null;
            _nameTailMissCache = null;
            _nameTailIndexFullyBuilt = false;
            _resolveCache = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Пакетно сопоставляет компоненты BCF с элементами модели и сразу
        /// выставляет HasModelLink на переданных экземплярах.
        /// </summary>
        public static Dictionary<Component, ElementId> ResolveBatch(Document doc, IEnumerable<Component> components)
        {
            var result = new Dictionary<Component, ElementId>();
            if (doc == null || components == null)
                return result;

            ResetForDocument(doc);

            List<Component> uniqueComponents = components
                .Where(c => c != null)
                .Distinct()
                .ToList();
            if (uniqueComponents.Count == 0)
                return result;

            InvalidateNegativeCacheEntries(uniqueComponents);

            var pendingGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var componentsByGuid = new Dictionary<string, List<Component>>(StringComparer.OrdinalIgnoreCase);

            foreach (Component component in uniqueComponents)
            {
                string cacheKey = BuildComponentKey(component);
                if (_resolveCache.TryGetValue(cacheKey, out int? cached) && cached.HasValue
                    && TryGetElementByNumericId(doc, cached.Value, out ElementId cachedEid))
                {
                    result[component] = cachedEid;
                    continue;
                }

                string guid = NormalizeGuidKey(component.IfcGuid);
                if (string.IsNullOrWhiteSpace(guid))
                    continue;

                if (_guidToIdIndex != null && _guidToIdIndex.TryGetValue(guid, out int knownId)
                    && TryGetElementByNumericId(doc, knownId, out ElementId knownEid))
                {
                    result[component] = knownEid;
                    CacheResolve(component, knownId);
                    continue;
                }

                pendingGuids.Add(guid);
                if (!componentsByGuid.TryGetValue(guid, out var list))
                {
                    list = new List<Component>();
                    componentsByGuid[guid] = list;
                }
                list.Add(component);
            }

            if (pendingGuids.Count > 0)
            {
                try
                {
                    ResolvePendingGuids(doc, pendingGuids);
                }
                catch
                {
                    // GUID-скан не должен ронять пакетный resolve
                }
            }

            foreach (var pair in componentsByGuid)
            {
                if (_guidToIdIndex == null || !_guidToIdIndex.TryGetValue(pair.Key, out int idValue))
                    continue;

                if (!TryGetElementByNumericId(doc, idValue, out ElementId eid))
                    continue;

                foreach (Component component in pair.Value)
                {
                    if (result.ContainsKey(component))
                        continue;
                    result[component] = eid;
                    CacheResolve(component, idValue);
                }
            }

            // GUID не нашёлся: GetElement по Id из BCF или описания.
            try
            {
                ResolveRemainingByAuthoringToolId(doc, uniqueComponents, result);
            }
            catch
            {
                // Поиск по Id не должен ронять пакетный resolve
            }

            // Id из другой модели: ищем тот же номер в хвосте имени элемента.
            try
            {
                ResolveByNameTailBatch(doc, uniqueComponents, result);
            }
            catch
            {
                // Поиск по имени не должен ронять пакетный resolve
            }

            foreach (Component component in uniqueComponents)
            {
                if (!result.ContainsKey(component))
                    CacheResolve(component, null);
            }

            return result;
        }

        /// <summary>
        /// Возвращает ElementId для одного компонента, используя общий кэш документа.
        /// </summary>
        public static int? ResolveElementId(Document doc, Component component)
        {
            if (doc == null || component == null)
                return null;

            Dictionary<Component, ElementId> batch = ResolveBatch(doc, new[] { component });
            if (batch.TryGetValue(component, out ElementId elementId))
                return elementId.GetValue();

            return null;
        }

        /// <summary>
        /// Ищет pending GUID в активном документе по параметру IfcGUID / IFC_GUID.
        /// Без лимита 20k: для BCF с одними IfcGuid иначе отверстия в большой модели не находятся.
        /// </summary>
        private static void ResolvePendingGuids(Document doc, HashSet<string> pendingGuids)
        {
            if (doc == null || pendingGuids == null || pendingGuids.Count == 0)
                return;

            if (_guidToIdIndex == null)
                _guidToIdIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (_guidMissCache == null)
                _guidMissCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var unresolved = new HashSet<string>(
                pendingGuids.Where(g => !_guidToIdIndex.ContainsKey(g) && !_guidMissCache.Contains(g)),
                StringComparer.OrdinalIgnoreCase);

            if (unresolved.Count == 0)
                return;

            // 1) Быстрый путь через BuiltInParameter.IFC_GUID для небольшого набора.
            if (unresolved.Count <= 8)
                TryResolveGuidsByIfcParameter(doc, unresolved);

            if (unresolved.Count == 0)
                return;

            // 2) Полный проход: параметр IfcGUID / IFC_GUID и UniqueId. GetExportId не вызываем: он роняет Revit.
            ScanDocumentForGuids(doc, unresolved);

            foreach (string guid in unresolved)
                _guidMissCache.Add(guid);
        }

        /// <summary>
        /// Обходит элементы модели и сопоставляет pending GUID по параметру IfcGUID и UniqueId.
        /// ExportUtils.GetExportId здесь не вызываем: на части элементов это роняет процесс Revit.
        /// </summary>
        private static void ScanDocumentForGuids(Document doc, HashSet<string> unresolved)
        {
            if (doc == null || unresolved == null || unresolved.Count == 0)
                return;

            foreach (Element element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (unresolved.Count == 0)
                    break;

                if (!RevitComponentListHost.IsSearchableModelElement(element))
                    continue;

                try
                {
                    TryIndexElementGuidKeys(element, unresolved);
                }
                catch
                {
                    // Один элемент не должен ронять весь GUID-скан
                }
            }
        }

        /// <summary>
        /// Индексирует GUID элемента и снимает совпавшие ключи из pending.
        /// </summary>
        private static void TryIndexElementGuidKeys(Element element, HashSet<string> unresolved)
        {
            if (element == null || unresolved == null || unresolved.Count == 0)
                return;

            foreach (string raw in EnumerateElementGuidKeys(element))
            {
                string key = NormalizeGuidKey(raw);
                if (string.IsNullOrWhiteSpace(key) || !unresolved.Contains(key))
                    continue;

                _guidToIdIndex[key] = element.Id.GetValue();
                unresolved.Remove(key);
                if (unresolved.Count == 0)
                    return;
            }
        }

        /// <summary>
        /// Возможные GUID элемента: параметр IFC_GUID и UniqueId. Без GetExportId.
        /// </summary>
        private static IEnumerable<string> EnumerateElementGuidKeys(Element element)
        {
            if (element == null)
                yield break;

            string paramGuid = ReadIfcGuidParam(element);
            if (!string.IsNullOrWhiteSpace(paramGuid))
                yield return paramGuid;

            if (!TryGetUniqueIdGuid(element, out Guid uniqueGuid))
                yield break;

            yield return uniqueGuid.ToString("D");
            string uniqueIfc = null;
            try
            {
                uniqueIfc = IfcGuid.ToIfcGuid(uniqueGuid);
            }
            catch
            {
                uniqueIfc = null;
            }

            if (!string.IsNullOrWhiteSpace(uniqueIfc))
                yield return uniqueIfc;
        }

        /// <summary>
        /// Берёт GUID из UniqueId Revit (первые 36 символов, без хвоста ElementId).
        /// </summary>
        private static bool TryGetUniqueIdGuid(Element element, out Guid guid)
        {
            guid = Guid.Empty;
            try
            {
                string uniqueId = element?.UniqueId;
                if (string.IsNullOrWhiteSpace(uniqueId) || uniqueId.Length < 36)
                    return false;

                return Guid.TryParse(uniqueId.Substring(0, 36), out guid);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Читает IFC_GUID / IfcGUID у элемента.
        /// </summary>
        private static string ReadIfcGuidParam(Element element)
        {
            if (element == null)
                return null;

            try
            {
                Parameter p = element.get_Parameter(BuiltInParameter.IFC_GUID);
                if (p != null && p.HasValue)
                {
                    string value = p.AsString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }

                p = element.LookupParameter("IfcGUID");
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                {
                    string value = p.AsString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        /// <summary>
        /// Быстрый поиск через ElementParameterFilter по BuiltInParameter.IFC_GUID.
        /// </summary>
        private static void TryResolveGuidsByIfcParameter(Document doc, HashSet<string> unresolved)
        {
            if (doc == null || unresolved == null || unresolved.Count == 0)
                return;

            foreach (string guid in unresolved.ToList())
            {
                try
                {
                    // Revit 2022: CreateEqualsRule(id, value, caseSensitive).
                    // С 2023 аргумент caseSensitive убран, в 2026 старый overload уже не компилируется.
#if R2022
                    FilterRule rule = ParameterFilterRuleFactory.CreateEqualsRule(
                        new ElementId(BuiltInParameter.IFC_GUID),
                        guid,
                        false);
#else
                    FilterRule rule = ParameterFilterRuleFactory.CreateEqualsRule(
                        new ElementId(BuiltInParameter.IFC_GUID),
                        guid);
#endif

                    Element element = new FilteredElementCollector(doc)
                        .WherePasses(new ElementParameterFilter(rule))
                        .WhereElementIsNotElementType()
                        .FirstElement();

                    if (element == null)
                        continue;

                    _guidToIdIndex[guid] = element.Id.GetValue();
                    unresolved.Remove(guid);
                }
                catch
                {
                    // Фильтр может быть недоступен в части моделей
                }
            }
        }

        /// <summary>
        /// Для компонентов без GUID-совпадения ищет элемент через GetElement по AuthoringToolId.
        /// </summary>
        private static void ResolveRemainingByAuthoringToolId(
            Document doc,
            IEnumerable<Component> components,
            Dictionary<Component, ElementId> result)
        {
            if (doc == null || result == null)
                return;

            foreach (Component component in components ?? Enumerable.Empty<Component>())
            {
                if (component == null || result.ContainsKey(component))
                    continue;

                try
                {
                    if (!TryResolveByAuthoringToolId(doc, component.AuthoringToolId, out ElementId byId))
                        continue;

                    result[component] = byId;
                    CacheResolve(component, byId.GetValue());
                }
                catch
                {
                    // Один неверный Id не должен ронять поиск остальных
                }
            }
        }

        /// <summary>
        /// Для компонентов без GUID-совпадения ищет Id в хвосте имени элемента модели.
        /// Нужен, когда ElementId из BCF принадлежит другой модели, но в имени ещё старый номер.
        /// </summary>
        private static void ResolveByNameTailBatch(
            Document doc,
            IEnumerable<Component> components,
            Dictionary<Component, ElementId> result)
        {
            var neededIds = new HashSet<long>();
            var componentsByTailId = new Dictionary<long, List<Component>>();

            foreach (Component component in components ?? Enumerable.Empty<Component>())
            {
                if (component == null || result.ContainsKey(component))
                    continue;

                if (!TryGetSearchIdFromAuthoringTool(component.AuthoringToolId, out long parsedId))
                    continue;

                neededIds.Add(parsedId);
                if (!componentsByTailId.TryGetValue(parsedId, out List<Component> list))
                {
                    list = new List<Component>();
                    componentsByTailId[parsedId] = list;
                }

                list.Add(component);
            }

            if (neededIds.Count == 0)
                return;

            EnsureNameTailIndex(doc, neededIds);

            if (_nameTailIdIndex == null)
                return;

            foreach (KeyValuePair<long, List<Component>> pair in componentsByTailId)
            {
                if (!_nameTailIdIndex.TryGetValue(pair.Key, out int currentId)
                    || !TryGetElementByNumericId(doc, currentId, out ElementId elementId))
                {
                    continue;
                }

                foreach (Component component in pair.Value)
                {
                    if (result.ContainsKey(component))
                        continue;

                    result[component] = elementId;
                    CacheResolve(component, currentId);
                }
            }
        }

        /// <summary>
        /// Строит индекс «Id из имени → ElementId текущей модели» лениво, только для нужных Id.
        /// </summary>
        private static void EnsureNameTailIndex(Document doc, HashSet<long> neededIds)
        {
            if (doc == null || neededIds == null || neededIds.Count == 0)
                return;

            if (_nameTailIdIndex == null)
                _nameTailIdIndex = new Dictionary<long, int>();
            if (_nameTailMissCache == null)
                _nameTailMissCache = new HashSet<long>();

            var pending = new HashSet<long>(
                neededIds.Where(id => !_nameTailIdIndex.ContainsKey(id) && !_nameTailMissCache.Contains(id)));
            if (pending.Count == 0)
                return;

            bool buildFull = _nameTailIndexFullyBuilt || pending.Count >= FullNameTailIndexThreshold;

            foreach (Element element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (!buildFull && pending.Count == 0)
                    break;

                if (!RevitComponentListHost.IsSearchableModelElement(element))
                    continue;

                try
                {
                    if (!TryParseIdFromElementName(element.Name, out long nameId))
                        continue;

                    if (!_nameTailIdIndex.ContainsKey(nameId))
                        _nameTailIdIndex[nameId] = element.Id.GetValue();

                    if (!buildFull)
                        pending.Remove(nameId);
                }
                catch
                {
                    // Имя недоступно у части служебных элементов
                }
            }

            if (buildFull)
            {
                _nameTailIndexFullyBuilt = true;
                return;
            }

            foreach (long missedId in pending)
                _nameTailMissCache.Add(missedId);
        }

        /// <summary>
        /// Берёт числовой Id из AuthoringToolId: целое число, хвост после ':' или hex UniqueId.
        /// </summary>
        private static bool TryGetSearchIdFromAuthoringTool(string authoringToolId, out long id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(authoringToolId))
                return false;

            string trimmed = authoringToolId.Trim();
            if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                return true;

            return TryParseIdAfterSeparator(trimmed, out id);
        }

        /// <summary>
        /// Берёт числовой Id из имени элемента: вся строка или хвост после последнего двоеточия.
        /// </summary>
        private static bool TryParseIdFromElementName(string name, out long id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            string trimmed = name.Trim();
            if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                return true;

            int colon = trimmed.LastIndexOf(':');
            if (colon < 0 || colon >= trimmed.Length - 1)
                return false;

            return long.TryParse(
                trimmed.Substring(colon + 1).Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out id);
        }

        /// <summary>
        /// Ищет элемент по AuthoringToolId через GetElement: целое число или хвост после ':'/'-'.
        /// Безопасно для любой категории, без обхода модели.
        /// </summary>
        private static bool TryResolveByAuthoringToolId(Document doc, string authoringToolId, out ElementId elementId)
        {
            elementId = ElementId.InvalidElementId;
            if (doc == null || string.IsNullOrWhiteSpace(authoringToolId))
                return false;

            try
            {
                string trimmed = authoringToolId.Trim();
                if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long directId)
                    && TryGetElementByNumericId(doc, directId, out elementId))
                {
                    return true;
                }

                if (TryParseIdAfterSeparator(trimmed, out long tailId)
                    && TryGetElementByNumericId(doc, tailId, out elementId))
                {
                    return true;
                }
            }
            catch
            {
                elementId = ElementId.InvalidElementId;
                return false;
            }

            elementId = ElementId.InvalidElementId;
            return false;
        }

        /// <summary>
        /// Берёт элемент документа по числовому Id. GetElement не роняет Revit, если элемента нет.
        /// </summary>
        private static bool TryGetElementByNumericId(Document doc, long id, out ElementId elementId)
        {
            elementId = ElementId.InvalidElementId;
            if (doc == null || id <= 0)
                return false;

            try
            {
                if (!RevitIdHelper.TryFromLong(id, out elementId))
                    return false;

                return doc.GetElement(elementId) != null;
            }
            catch
            {
                elementId = ElementId.InvalidElementId;
                return false;
            }
        }

        /// <summary>
        /// Берёт числовой Id из хвоста после ':' (decimal) или hex-хвоста UniqueId.
        /// </summary>
        private static bool TryParseIdAfterSeparator(string value, out long id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            int colon = value.LastIndexOf(':');
            if (colon >= 0 && colon < value.Length - 1)
            {
                string colonTail = value.Substring(colon + 1).Trim();
                if (long.TryParse(colonTail, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                    return true;
            }

            int dash = value.LastIndexOf('-');
            if (dash >= 0 && dash < value.Length - 1)
            {
                string dashTail = value.Substring(dash + 1).Trim();
                if (dashTail.Length > 0
                    && dashTail.Length <= 8
                    && long.TryParse(dashTail, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Нормализует ключ GUID для словарей: стандартный GUID → D/upper, IFC compressed → trim.
        /// </summary>
        private static string NormalizeGuidKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string trimmed = value.Trim().Trim('"').Trim('{', '}');
            if (string.IsNullOrWhiteSpace(trimmed))
                return null;

            if (Guid.TryParse(trimmed, out Guid guid))
                return guid.ToString("D").ToUpperInvariant();

            return trimmed;
        }

        /// <summary>
        /// Удаляет отрицательные записи кэша для повторного resolve.
        /// </summary>
        private static void InvalidateNegativeCacheEntries(IEnumerable<Component> components)
        {
            if (_resolveCache == null || components == null)
                return;

            foreach (Component component in components)
            {
                if (component == null)
                    continue;

                string key = BuildComponentKey(component);
                if (_resolveCache.TryGetValue(key, out int? cached) && !cached.HasValue)
                    _resolveCache.Remove(key);

                string guid = NormalizeGuidKey(component.IfcGuid);
                if (!string.IsNullOrWhiteSpace(guid))
                    _guidMissCache?.Remove(guid);
            }
        }

        /// <summary>
        /// Помечает компоненты как не найденные без сканирования модели.
        /// </summary>
        public static void MarkUnresolved(IEnumerable<Component> components)
        {
            if (components == null)
                return;

            if (_resolveCache == null)
                _resolveCache = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);

            foreach (Component component in components)
            {
                if (component == null)
                    continue;

                CacheResolve(component, null);
            }
        }

        /// <summary>
        /// Возвращает закэшированный ElementId без обращения к Revit API.
        /// </summary>
        public static bool TryGetCachedResolve(Component component, out int? elementId)
        {
            elementId = null;
            if (component == null || _resolveCache == null)
                return false;

            return _resolveCache.TryGetValue(BuildComponentKey(component), out elementId);
        }

        /// <summary>
        /// Формирует ключ кэша по AuthoringToolId и IfcGuid.
        /// </summary>
        public static string BuildComponentResolveKey(Component component)
        {
            return BuildComponentKey(component);
        }

        /// <summary>
        /// Сохраняет результат сопоставления в кэше.
        /// </summary>
        private static void CacheResolve(Component component, int? elementId)
        {
            if (component == null)
                return;

            if (_resolveCache == null)
                _resolveCache = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);

            _resolveCache[BuildComponentKey(component)] = elementId;
        }

        /// <summary>
        /// Формирует ключ кэша по AuthoringToolId и IfcGuid.
        /// </summary>
        private static string BuildComponentKey(Component component)
        {
            string authoringId = component?.AuthoringToolId?.Trim() ?? string.Empty;
            string ifcGuid = component?.IfcGuid?.Trim() ?? string.Empty;
            return authoringId + "|" + ifcGuid;
        }

        /// <summary>
        /// Формирует ключ активного документа Revit для инвалидации кэша.
        /// </summary>
        private static string BuildDocumentKey(Document doc)
        {
            return (doc?.PathName ?? string.Empty) + "|" + doc?.GetHashCode();
        }
    }
}
