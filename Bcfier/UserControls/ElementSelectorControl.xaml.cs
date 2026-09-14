using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Bcfier.Localization;

namespace Bcfier.UserControls
{
  /// <summary>
  /// UI выбора элементов viewpoint: чекбоксы, dual-list со стрелками или ручной ввод Id.
  /// </summary>
  public partial class ElementSelectorControl : UserControl
  {
    /// <summary>
    /// Показывать заголовок секции «Элементы вида» (отключается, если заголовок уже есть у родителя).
    /// </summary>
    public static readonly DependencyProperty ShowSectionHeaderProperty =
      DependencyProperty.Register(
        nameof(ShowSectionHeader),
        typeof(bool),
        typeof(ElementSelectorControl),
        new PropertyMetadata(true, OnShowSectionHeaderChanged));

    /// <summary>
    /// Две таблицы (доступные / выбранные) со стрелками переноса вместо чекбоксов.
    /// </summary>
    public static readonly DependencyProperty UseDualListModeProperty =
      DependencyProperty.Register(
        nameof(UseDualListMode),
        typeof(bool),
        typeof(ElementSelectorControl),
        new PropertyMetadata(false, OnUseDualListModeChanged));

    public bool ShowSectionHeader
    {
      get => (bool)GetValue(ShowSectionHeaderProperty);
      set => SetValue(ShowSectionHeaderProperty, value);
    }

    public bool UseDualListMode
    {
      get => (bool)GetValue(UseDualListModeProperty);
      set => SetValue(UseDualListModeProperty, value);
    }

    public ElementSelectorViewModel ViewModel { get; private set; }

    public ElementSelectorControl()
    {
      Loc.ApplyCultureFromSettings();
      InitializeComponent();
      ApplySectionHeaderVisibility();
      ApplyLayoutMode();
    }

    /// <summary>
    /// Обновляет видимость внутреннего заголовка секции.
    /// </summary>
    private static void OnShowSectionHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is ElementSelectorControl control)
        control.ApplySectionHeaderVisibility();
    }

    /// <summary>
    /// Переключает dual-list / checkbox layout.
    /// </summary>
    private static void OnUseDualListModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is ElementSelectorControl control)
        control.ApplyLayoutMode();
    }

    /// <summary>
    /// Скрывает или показывает заголовок «Элементы вида» внутри контрола.
    /// </summary>
    private void ApplySectionHeaderVisibility()
    {
      if (SectionHeaderPanel == null)
        return;

      SectionHeaderPanel.Visibility = ShowSectionHeader
        ? Visibility.Visible
        : Visibility.Collapsed;
    }

    /// <summary>
    /// Показывает dual-list или обычный список чекбоксов (если не manual mode).
    /// </summary>
    private void ApplyLayoutMode()
    {
      if (ListModePanel == null || DualListPanel == null)
        return;

      if (ViewModel?.IsManualIdMode == true)
        return;

      bool dual = UseDualListMode;
      ListModePanel.Visibility = dual ? Visibility.Collapsed : Visibility.Visible;
      DualListPanel.Visibility = dual ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Bind(ElementSelectorViewModel viewModel)
    {
      ViewModel = viewModel;
      ApplyModeUi();

      if (ViewModel?.IsManualIdMode == true)
        return;

      ApplyColumnWidths();
      RefreshList();
    }

    /// <summary>
    /// Переключает видимость списка и панели ручного ввода Id.
    /// </summary>
    private void ApplyModeUi()
    {
      bool manual = ViewModel?.IsManualIdMode == true;
      ManualModePanel.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
      if (manual)
      {
        ListModePanel.Visibility = Visibility.Collapsed;
        DualListPanel.Visibility = Visibility.Collapsed;
        ManualIdsBox.Text = ViewModel.ManualIdsText ?? string.Empty;
        return;
      }

      ApplyLayoutMode();
    }

    /// <summary>
    /// В dual-list всегда Id/Семейство/Тип; в списке чекбоксов переключает IfcGuid и имена Revit.
    /// </summary>
    private void ApplyColumnWidths()
    {
      if (UseDualListMode)
      {
        AvailableIdColumn.Width = 70;
        AvailableFamilyColumn.Width = 110;
        AvailableTypeColumn.Width = 120;

        SelectedIdColumn.Width = 70;
        SelectedFamilyColumn.Width = 110;
        SelectedTypeColumn.Width = 120;
        return;
      }

      bool hasRevitNames = ViewModel?.Items.Any(i =>
        !string.IsNullOrWhiteSpace(i.FamilyName) || !string.IsNullOrWhiteSpace(i.TypeName)) == true;

      IdColumn.Width = hasRevitNames ? 80 : 120;
      IfcGuidColumn.Width = hasRevitNames ? 0 : 220;
      FamilyColumn.Width = hasRevitNames ? 150 : 0;
      TypeColumn.Width = hasRevitNames ? 180 : 0;
    }

    /// <summary>
    /// Обновляет источник(и) списков из ViewModel.
    /// </summary>
    private void RefreshList()
    {
      if (ViewModel == null || ViewModel.IsManualIdMode)
        return;

      if (UseDualListMode)
      {
        AvailableList.ItemsSource = ViewModel.AvailableItems.ToList();
        SelectedList.ItemsSource = ViewModel.SelectedPoolItems.ToList();
        return;
      }

      var items = ViewModel.FilteredItems
        .OrderByDescending(i => i.IsInitiallySelected)
        .ThenBy(i => i.FamilyName)
        .ThenBy(i => i.TypeName)
        .ThenBy(i => i.AuthoringToolId)
        .ToList();

      if (ViewModel.Items.Any(i => i.IsInitiallySelected))
      {
        var groupedView = new ListCollectionView(items);
        groupedView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ComponentSelectionItem.SelectionGroup)));
        ItemsList.ItemsSource = groupedView;
      }
      else
      {
        ItemsList.ItemsSource = items;
      }
    }

    private void FilterBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
      if (ViewModel == null)
        return;

      ViewModel.FilterText = FilterBox.Text;
      RefreshList();
    }

    private void DualFilterBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
      if (ViewModel == null)
        return;

      ViewModel.FilterText = DualFilterBox.Text;
      RefreshList();
    }

    private void ManualIdsBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
      if (ViewModel == null)
        return;

      ViewModel.ManualIdsText = ManualIdsBox.Text;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
      ViewModel?.SelectAll(true);
      RefreshList();
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
      ViewModel?.SelectAll(false);
      RefreshList();
    }

    /// <summary>
    /// Переносит выделенные в левой таблице элементы в выбранные.
    /// </summary>
    private void MoveToSelected_Click(object sender, RoutedEventArgs e)
    {
      MoveHighlighted(AvailableList, toSelected: true);
    }

    /// <summary>
    /// Переносит все доступные элементы в выбранные.
    /// </summary>
    private void MoveAllToSelected_Click(object sender, RoutedEventArgs e)
    {
      if (ViewModel == null)
        return;

      ViewModel.MoveToSelected(ViewModel.AvailableItems.ToList());
      RefreshList();
    }

    /// <summary>
    /// Переносит выделенные в правой таблице элементы обратно в доступные.
    /// </summary>
    private void MoveToAvailable_Click(object sender, RoutedEventArgs e)
    {
      MoveHighlighted(SelectedList, toSelected: false);
    }

    /// <summary>
    /// Убирает все элементы из выбранных.
    /// </summary>
    private void MoveAllToAvailable_Click(object sender, RoutedEventArgs e)
    {
      if (ViewModel == null)
        return;

      ViewModel.MoveToAvailable(ViewModel.SelectedPoolItems.ToList());
      RefreshList();
    }

    /// <summary>
    /// Двойной клик по доступному элементу добавляет его в выбранные.
    /// </summary>
    private void AvailableList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
      if (AvailableList.SelectedItem is ComponentSelectionItem item)
      {
        ViewModel?.MoveToSelected(new[] { item });
        RefreshList();
      }
    }

    /// <summary>
    /// Двойной клик по выбранному элементу возвращает его в доступные.
    /// </summary>
    private void SelectedList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
      if (SelectedList.SelectedItem is ComponentSelectionItem item)
      {
        ViewModel?.MoveToAvailable(new[] { item });
        RefreshList();
      }
    }

    /// <summary>
    /// Переносит текущее выделение ListView между пулами.
    /// </summary>
    private void MoveHighlighted(ListView list, bool toSelected)
    {
      if (ViewModel == null || list?.SelectedItems == null || list.SelectedItems.Count == 0)
        return;

      var items = list.SelectedItems.Cast<ComponentSelectionItem>().ToList();
      if (toSelected)
        ViewModel.MoveToSelected(items);
      else
        ViewModel.MoveToAvailable(items);

      RefreshList();
    }

    public Bcfier.Bcf.Bcf2.Component[] GetSelectedComponents()
    {
      if (ViewModel?.IsManualIdMode == true)
        ViewModel.ManualIdsText = ManualIdsBox.Text;

      return ViewModel?.GetSelectedComponents() ?? new Bcfier.Bcf.Bcf2.Component[0];
    }

    /// <summary>
    /// Обновляет список после выбора элементов в модели.
    /// </summary>
    public void RefreshAfterModelPick()
    {
      if (ViewModel == null)
        return;

      ApplyModeUi();
      if (ViewModel.IsManualIdMode)
        return;

      ApplyColumnWidths();
      RefreshList();
    }
  }
}
