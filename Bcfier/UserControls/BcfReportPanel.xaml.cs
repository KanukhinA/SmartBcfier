using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Bcfier.Localization;
using Bcfier.Windows;
using GongSolutions.Wpf.DragDrop;


namespace Bcfier.UserControls
{
  /// <summary>
  /// Панель замечаний одного BCF-файла (список issues + детали topic).
  /// </summary>
  public partial class BcfReportPanel : UserControl
  {
    private Topic _currentTopic;
    private bool _suppressLabelChange;
    private bool _suppressDueDateChange;
    private bool _suppressCoordinateModeChange;

    public BcfReportPanel()
    {
      Loc.ApplyCultureFromSettings();
      InitializeComponent();

      // Dummy: загрузка GongSolutions.Wpf.DragDrop рядом с Bcfier (Costura/Revit)
      try
      {
        _ = GongSolutions.Wpf.DragDrop.DragDrop.DataFormat;
      }
      catch (Exception ex)
      {
        throw new InvalidOperationException(
          "Не удалось загрузить GongSolutions.Wpf.DragDrop.dll рядом с Bcfier.", ex);
      }
      //binding set from code-behind
      //so that in the designer it still binds to the "Issues" collection
      //allowing for Design time preview
      //the binding to View is needed for filtering the collection
      IssueList.SetBinding(ItemsControl.ItemsSourceProperty, "View");
      ((INotifyCollectionChanged)IssueList.Items).CollectionChanged += IssueList_CollectionChanged;
      IssueList.SelectionChanged += IssueList_OnSelectionChanged;
    }

    private void IssueList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
      if (e.Action == NotifyCollectionChangedAction.Add)
      {
        // scroll the new item into view   
        IssueList.ScrollIntoView(e.NewItems[0]);
        TextBox_Title.Focus();
      }
    }

    private void IssueList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      var issue = IssueList.SelectedItem as Markup;
      if (issue?.Topic == null)
      {
        _suppressLabelChange = true;
        _suppressDueDateChange = true;
        try
        {
          LabelsCombo.SelectedItem = null;
          DueDatePicker.SelectedDate = null;
        }
        finally
        {
          _suppressLabelChange = false;
          _suppressDueDateChange = false;
        }

        _currentTopic = null;
        SyncCustomFieldsUi();
        return;
      }

      // Labels[] → SelectedLabels; в ComboBox — первая метка (как Priority)
      BcfIssueHelper.SyncLabelsFromTopic(issue.Topic);
      _currentTopic = issue.Topic;

      string selected = issue.Topic.SelectedLabels?.FirstOrDefault()
                       ?? issue.Topic.Labels?.FirstOrDefault();

      _suppressLabelChange = true;
      try
      {
        LabelsCombo.SelectedItem = selected;
        // В XSD дата не nullable, поэтому отсутствие значения определяется отдельным флагом.
        _suppressDueDateChange = true;
        DueDatePicker.SelectedDate = issue.Topic.DueDateSpecified
          ? issue.Topic.DueDate
          : (DateTime?)null;
      }
      finally
      {
        _suppressLabelChange = false;
        _suppressDueDateChange = false;
      }

      SyncCustomFieldsUi();
    }

    private void AddCustomFields_Click(object sender, RoutedEventArgs e)
    {
      if (DataContext is not BcfFile currentFile)
        return;

      var window = new AddCustomFieldsWindow(currentFile) { Owner = Window.GetWindow(this) };
      if (window.ShowDialog() != true)
        return;

      currentFile.HasBeenSaved = false;
      currentFile.HasCustomFields = currentFile.ReportLevelCustomFields?.Count > 0;
      currentFile.CustomFieldsVisible = true;
      CustomFieldsXmlStoreQuickSave(currentFile);

      SyncCustomFieldsUi();
    }

    private void CustomFieldsBlock_FieldsChanged(object sender, EventArgs e)
    {
      if (DataContext is BcfFile bcf)
      {
        bcf.HasBeenSaved = false;
        CustomFieldsXmlStoreQuickSave(bcf);
      }
    }

    private void SyncCustomFieldsUi()
    {
      if (DataContext is BcfFile bcf)
      {
        CustomFieldsBlock.BcfFile = bcf;
        CustomFieldsBlock.Refresh();
      }
    }

    private static void CustomFieldsXmlStoreQuickSave(BcfFile bcf)
    {
      try
      {
        Bcfier.CustomFields.CustomFieldsXmlStore.SaveCanonical(bcf.TempPath, bcf.ReportLevelCustomFields);
      }
      catch
      {
      }
    }

    private void DueDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
      if (_suppressDueDateChange || _currentTopic == null)
        return;

      BcfIssueHelper.ApplyDueDate(_currentTopic, DueDatePicker.SelectedDate);

      if (DataContext is BcfFile bcf)
        bcf.HasBeenSaved = false;
    }

    private void LabelsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (_suppressLabelChange || _currentTopic == null)
        return;

      _currentTopic.SelectedLabels.Clear();
      if (LabelsCombo.SelectedItem is string label && !string.IsNullOrWhiteSpace(label))
        _currentTopic.SelectedLabels.Add(label);

      BcfIssueHelper.SyncLabelsToTopic(_currentTopic);

      if (DataContext is BcfFile bcf)
        bcf.HasBeenSaved = false;
    }

    /// <summary>
    /// Сохраняет выбранную ориентацию осей BCF и обновляет отображение в отчёте.
    /// </summary>
    private void BcfCoordinateModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (_suppressCoordinateModeChange || BcfCoordinateModeCombo == null)
        return;

      try
      {
        string mode = BcfCoordinateSettings.DefaultMode;
        if (BcfCoordinateModeCombo.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && !string.IsNullOrWhiteSpace(tag))
        {
          mode = tag;
        }

        BcfCoordinateSettings.SetMode(mode);
        if (DataContext is BcfFile bcf)
          bcf.RefreshReportMetadata();
      }
      catch
      {
        // Ошибка сохранения пресета осей не должна ломать UI
      }
    }

    private void TextBox_OnTextChanged(object sender, DataTransferEventArgs e)
    {
      if (IssueList.SelectedIndex == -1)
        return;
      var bcf = this.DataContext as BcfFile;
      if (bcf == null)
        return;
      bcf.HasBeenSaved = false;
    }

    private void BcfReportPanel_OnLoaded(object sender, RoutedEventArgs e)
    {
      SelectCoordinateMode(BcfCoordinateSettings.GetMode());
    }

    /// <summary>
    /// Выбирает пресет осей BCF в комбобоксе панели отчёта.
    /// </summary>
    private void SelectCoordinateMode(string mode)
    {
      if (BcfCoordinateModeCombo == null)
        return;

      string normalized = BcfCoordinateSettings.Normalize(mode);
      _suppressCoordinateModeChange = true;
      try
      {
        foreach (var item in BcfCoordinateModeCombo.Items)
        {
          if (item is ComboBoxItem comboItem
              && string.Equals(comboItem.Tag as string, normalized, StringComparison.Ordinal))
          {
            BcfCoordinateModeCombo.SelectedItem = comboItem;
            return;
          }
        }

        BcfCoordinateModeCombo.SelectedIndex = 0;
      }
      finally
      {
        _suppressCoordinateModeChange = false;
      }
    }

    private void SearchBox_OnKeyDown(object sender, KeyEventArgs e)
    {
     if(e.Key== Key.Enter)
       Keyboard.ClearFocus();
    }
  }
}
