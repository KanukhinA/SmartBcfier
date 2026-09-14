using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Bcfier.Bcf.Bcf2;
using BcfComponent = Bcfier.Bcf.Bcf2.Component;
using Bcfier.Localization;

namespace Bcfier.UserControls
{
    /// <summary>
    /// Элемент списка выбора компонентов viewpoint.
    /// </summary>
    public class ComponentSelectionItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string AuthoringToolId { get; set; }
        public string IfcGuid { get; set; }
        public string OriginatingSystem { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public int LinkedElementId { get; set; }
        public bool HasModelLink { get; set; }
        public bool IsInitiallySelected { get; set; }
        public string SelectionGroup => IsInitiallySelected ? Loc.SelectedElements : Loc.OtherElements;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public static ComponentSelectionItem FromComponent(
            BcfComponent component,
            bool selected,
            string familyName = null,
            string typeName = null)
        {
            return new ComponentSelectionItem
            {
                AuthoringToolId = component?.AuthoringToolId,
                IfcGuid = component?.IfcGuid,
                OriginatingSystem = component?.OriginatingSystem,
                FamilyName = familyName,
                TypeName = typeName,
                LinkedElementId = component?.LinkedElementId ?? 0,
                HasModelLink = component != null && component.HasModelLink,
                IsInitiallySelected = selected,
                IsSelected = selected
            };
        }

        public BcfComponent ToComponent()
        {
            int linkedId = LinkedElementId;
            if (linkedId <= 0)
                int.TryParse(AuthoringToolId, out linkedId);

            return new BcfComponent
            {
                AuthoringToolId = AuthoringToolId,
                IfcGuid = IfcGuid,
                OriginatingSystem = OriginatingSystem,
                HasModelLink = linkedId > 0 || HasModelLink,
                LinkedElementId = linkedId
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// ViewModel выбора элементов viewpoint.
    /// </summary>
    public class ElementSelectorViewModel
    {
        /// <summary>
        /// Проверяет, что для элемента указаны и семейство, и тип.
        /// </summary>
        public static bool HasFamilyAndTypeNames(string familyName, string typeName)
        {
            return !string.IsNullOrWhiteSpace(familyName) && !string.IsNullOrWhiteSpace(typeName);
        }

        /// <summary>
        /// Проверяет, что у элемента списка указаны и семейство, и тип.
        /// </summary>
        public static bool HasFamilyAndTypeNames(ComponentSelectionItem item)
        {
            return item != null && HasFamilyAndTypeNames(item.FamilyName, item.TypeName);
        }

        public ObservableCollection<ComponentSelectionItem> Items { get; } = new ObservableCollection<ComponentSelectionItem>();
        public string FilterText { get; set; }

        /// <summary>Режим ручного ввода Id вместо списка кандидатов.</summary>
        public bool IsManualIdMode { get; private set; }

        /// <summary>Текст поля ручного ввода Id.</summary>
        public string ManualIdsText { get; set; }

        public IEnumerable<ComponentSelectionItem> FilteredItems
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FilterText))
                    return Items;

                // Фильтр по Revit Id, IfcGuid, семейству или типу.
                string f = FilterText.Trim();
                return Items.Where(i =>
                    (i.AuthoringToolId != null && i.AuthoringToolId.Contains(f))
                    || (i.IfcGuid != null && i.IfcGuid.Contains(f))
                    || (i.FamilyName != null && i.FamilyName.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    || (i.TypeName != null && i.TypeName.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0));
            }
        }

        /// <summary>
        /// Элементы пула «доступные на виде» (ещё не выбранные) с учётом фильтра.
        /// </summary>
        public IEnumerable<ComponentSelectionItem> AvailableItems
        {
            get
            {
                return FilteredItems
                    .Where(i => !i.IsSelected)
                    .OrderBy(i => i.FamilyName)
                    .ThenBy(i => i.TypeName)
                    .ThenBy(i => i.AuthoringToolId);
            }
        }

        /// <summary>
        /// Элементы пула «выбранные» с учётом фильтра.
        /// </summary>
        public IEnumerable<ComponentSelectionItem> SelectedPoolItems
        {
            get
            {
                return FilteredItems
                    .Where(i => i.IsSelected)
                    .OrderBy(i => i.FamilyName)
                    .ThenBy(i => i.TypeName)
                    .ThenBy(i => i.AuthoringToolId);
            }
        }

        /// <summary>
        /// Переносит элементы из доступных в выбранные.
        /// </summary>
        public void MoveToSelected(IEnumerable<ComponentSelectionItem> items)
        {
            if (items == null)
                return;

            foreach (ComponentSelectionItem item in items.Where(i => i != null))
                item.IsSelected = true;
        }

        /// <summary>
        /// Переносит элементы из выбранных обратно в доступные.
        /// </summary>
        public void MoveToAvailable(IEnumerable<ComponentSelectionItem> items)
        {
            if (items == null)
                return;

            foreach (ComponentSelectionItem item in items.Where(i => i != null))
                item.IsSelected = false;
        }

        public void LoadFromSelection(
            BcfComponent[] selection,
            BcfComponent[] allCandidates,
            IDictionary<string, string> familyNames = null,
            IDictionary<string, string> typeNames = null)
        {
            IsManualIdMode = false;
            ManualIdsText = null;
            Items.Clear();
            // Preselect по AuthoringToolId из текущего Selection
            var selectedIds = new HashSet<string>(
                (selection ?? new BcfComponent[0]).Select(c => c?.AuthoringToolId).Where(id => !string.IsNullOrWhiteSpace(id)));

            bool filterByRevitNames = familyNames != null && typeNames != null;
            foreach (var component in allCandidates ?? new BcfComponent[0])
            {
                bool selected = selectedIds.Contains(component.AuthoringToolId);
                string familyName = null;
                string typeName = null;
                familyNames?.TryGetValue(component.AuthoringToolId, out familyName);
                typeNames?.TryGetValue(component.AuthoringToolId, out typeName);

                // В списке элементов вида Revit не показываем объекты без семейства и типа.
                if (filterByRevitNames && !HasFamilyAndTypeNames(familyName, typeName))
                    continue;

                Items.Add(ComponentSelectionItem.FromComponent(component, selected, familyName, typeName));
            }
        }

        /// <summary>
        /// Включает режим ручного ввода Id при слишком большом числе кандидатов вида.
        /// </summary>
        public void EnableManualIdMode(IEnumerable<string> prefillIds = null)
        {
            IsManualIdMode = true;
            Items.Clear();
            ManualIdsText = prefillIds == null
                ? string.Empty
                : string.Join(", ", prefillIds.Where(id => !string.IsNullOrWhiteSpace(id)));
        }

        /// <summary>
        /// Разбирает строку ручного ввода Id (пробел, запятая, точка с запятой).
        /// </summary>
        public static string[] ParseManualIds(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            return text
                .Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct()
                .ToArray();
        }

        public BcfComponent[] GetSelectedComponents()
        {
            if (IsManualIdMode)
            {
                return ParseManualIds(ManualIdsText)
                    .Select(id =>
                    {
                        int linkedId;
                        bool hasLink = int.TryParse(id, out linkedId);
                        return new BcfComponent
                        {
                            AuthoringToolId = id,
                            HasModelLink = hasLink,
                            LinkedElementId = hasLink ? linkedId : 0
                        };
                    })
                    .ToArray();
            }

            return Items.Where(i => i.IsSelected).Select(i => i.ToComponent()).ToArray();
        }

        public void SelectAll(bool selected)
        {
            foreach (var item in Items)
                item.IsSelected = selected;
        }

        /// <summary>
        /// Добавляет выбранные в модели элементы в список и отмечает их как связанные.
        /// </summary>
        public void AddOrSelect(IEnumerable<ComponentSelectionItem> picked)
        {
            if (picked == null)
                return;

            foreach (ComponentSelectionItem item in picked)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.AuthoringToolId))
                    continue;

                if (!HasFamilyAndTypeNames(item))
                    continue;

                ComponentSelectionItem existing = Items.FirstOrDefault(
                    x => string.Equals(x.AuthoringToolId, item.AuthoringToolId, StringComparison.Ordinal));
                if (existing != null)
                {
                    existing.IsSelected = true;
                    if (!string.IsNullOrWhiteSpace(item.FamilyName))
                        existing.FamilyName = item.FamilyName;
                    if (!string.IsNullOrWhiteSpace(item.TypeName))
                        existing.TypeName = item.TypeName;
                    if (!string.IsNullOrWhiteSpace(item.IfcGuid))
                        existing.IfcGuid = item.IfcGuid;
                    continue;
                }

                item.IsInitiallySelected = true;
                item.IsSelected = true;
                Items.Insert(0, item);
            }
        }

        /// <summary>
        /// Добавляет Id, выбранные в модели, в режим ручного ввода.
        /// </summary>
        public void AppendManualIds(IEnumerable<string> ids)
        {
            var current = new HashSet<string>(ParseManualIds(ManualIdsText), StringComparer.Ordinal);
            foreach (string id in ids ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(id))
                    current.Add(id.Trim());
            }

            ManualIdsText = string.Join(", ", current);
        }

        /// <summary>Лимит защищает Revit от слишком большой выборки.</summary>
        public bool HasTooManySelected(int limit = 1000)
        {
            return GetSelectedComponents().Length > limit;
        }

        public string TooManySelectedMessage => Loc.TooManyComponents;
    }
}
