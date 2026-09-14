using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Bcfier.Localization;
using Bcfier.UserControls;
using Bcfier.Data.Utils;

namespace Bcfier.Windows
{
    /// <summary>
    /// Окно списка элементов viewpoint: просмотр, правка selection и выделение в модели.
    /// </summary>
    public partial class ComponentsList : Window
    {
        private readonly bool _editMode;
        private readonly Action<Component[]> _onSave;
        private readonly Action<List<int>> _onSelectInModel;
        private readonly ElementSelectorViewModel _viewModel = new ElementSelectorViewModel();

        public ComponentsList(
            Components components,
            bool editMode,
            Action<Component[]> onSave = null,
            Action<List<int>> onSelectInModel = null)
        {
            Loc.ApplyCultureFromSettings();
            InitializeComponent();
            _editMode = editMode;
            _onSave = onSave;
            _onSelectInModel = onSelectInModel;

            Title = Loc.ElementsList;
            var selection = components?.Selection ?? Array.Empty<Component>();
            // Кандидаты = Selection ∪ Visibility.Exceptions (уникальные по AuthoringToolId)
            var candidates = selection
                .Concat(components?.Visibility?.Exceptions ?? Array.Empty<Component>())
                .GroupBy(c => c.AuthoringToolId)
                .Select(g => g.First())
                .ToArray();

            _viewModel.LoadFromSelection(selection, candidates);
            Selector.Bind(_viewModel);

            SaveBtn.Visibility = _editMode ? Visibility.Visible : Visibility.Collapsed;
            bool canSelectInModel = BcfHostCapabilities.SupportsSelectInModel
                && (_onSelectInModel != null || ComponentListHost.SelectInModel != null);
            SelectInModelBtn.Visibility = canSelectInModel ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.HasTooManySelected())
            {
                MessageBox.Show(_viewModel.TooManySelectedMessage, Loc.ElementsList, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _onSave?.Invoke(_viewModel.GetSelectedComponents());
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Собирает Revit ElementId выбранных компонентов (LinkedElementId или числовой AuthoringToolId).
        /// </summary>
        private static List<int> CollectLinkedElementIds(IEnumerable<Component> components)
        {
            var ids = new List<int>();
            if (components == null)
                return ids;

            foreach (Component component in components)
            {
                if (!BcfViewpointComponents.TryGetSelectableElementId(component, out int elementId))
                    continue;

                if (!ids.Contains(elementId))
                    ids.Add(elementId);
            }

            return ids;
        }

        private void SelectInModel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // AuthoringToolId → int ElementId → ExternalEvent в Revit
                var ids = CollectLinkedElementIds(_viewModel.GetSelectedComponents());
                if (ids.Count == 0)
                    return;

                if (_onSelectInModel != null)
                    _onSelectInModel(ids);
                else
                    ComponentListHost.SelectInModel?.Invoke(ids, false);
            }
            catch (Exception ex)
            {
                ExceptionUi.Show(ex);
            }
        }
    }
}
