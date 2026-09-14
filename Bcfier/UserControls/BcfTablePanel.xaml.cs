using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Bcfier.CustomFields;
using Bcfier.Data;
using Bcfier.Data.Utils;
using Bcfier.Localization;
using Bcfier.ReportTable;
using Bcfier.Windows;
using Microsoft.Win32;

namespace Bcfier.UserControls
{
  /// <summary>Табличный режим: отчёт, настройка колонок, DataGrid, экспорт.</summary>
  public partial class BcfTablePanel : UserControl
  {
    private static readonly HashSet<ReportTableColumnKind> ReadOnlyKinds =
      new HashSet<ReportTableColumnKind>
      {
        ReportTableColumnKind.Guid,
        ReportTableColumnKind.CreationAuthor,
        ReportTableColumnKind.CreationDate,
        ReportTableColumnKind.ModifiedAuthor,
        ReportTableColumnKind.ModifiedDate,
        ReportTableColumnKind.Index,
        ReportTableColumnKind.Snapshot
      };

    private readonly ObservableCollection<ReportTableRow> _rows = new ObservableCollection<ReportTableRow>();
    private List<ReportTableColumnConfig> _columns;
    private List<ReportTableUserGroup> _groups = ReportTableUserGroups.Load();
    private BcfContainer _container;
    private BcfFile _subscribedFile;

    public BcfTablePanel()
    {
      InitializeComponent();
      IssuesGrid.ItemsSource = _rows;
      _columns = ReportTableSettings.LoadColumns();
      AddHandler(TableCommentCell.CommentsChangedEvent, new RoutedEventHandler(OnTableCommentsChanged));
      RebuildColumns();
    }

    /// <summary>Привязывает контейнер открытых BCF и подписывается на смену отчёта.</summary>
    public void AttachContainer(BcfContainer container)
    {
      if (_container != null)
      {
        _container.PropertyChanged -= Container_PropertyChanged;
        UnsubscribeSelectedFile();
      }

      _container = container;
      DataContext = container;

      if (_container != null)
        _container.PropertyChanged += Container_PropertyChanged;

      SubscribeSelectedFile();
      RefreshRows();
    }

    private void SubscribeSelectedFile()
    {
      UnsubscribeSelectedFile();
      _subscribedFile = GetSelectedFile();
      if (_subscribedFile?.Issues == null)
        return;

      _subscribedFile.Issues.CollectionChanged += SelectedIssues_CollectionChanged;
      _subscribedFile.PropertyChanged += SelectedFile_PropertyChanged;
    }

    private void UnsubscribeSelectedFile()
    {
      if (_subscribedFile == null)
        return;
      if (_subscribedFile.Issues != null)
        _subscribedFile.Issues.CollectionChanged -= SelectedIssues_CollectionChanged;
      _subscribedFile.PropertyChanged -= SelectedFile_PropertyChanged;
      _subscribedFile = null;
    }

    private void SelectedIssues_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
      RefreshRows();
    }

    private void SelectedFile_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
      if (e.PropertyName == nameof(BcfFile.View)
          || e.PropertyName == nameof(BcfFile.Issues)
          || e.PropertyName == nameof(BcfFile.TextSearch)
          || e.PropertyName == nameof(BcfFile.Filename))
        RefreshRows();
    }

    public void RefreshRows()
    {
      SubscribeSelectedFile();
      _rows.Clear();
      BcfFile file = GetSelectedFile();
      if (file == null)
      {
        EmptyHint.Visibility = Visibility.Visible;
        IssuesGrid.Visibility = Visibility.Collapsed;
        SyncCustomFieldsPanel();
        return;
      }

      EmptyHint.Visibility = Visibility.Collapsed;
      IssuesGrid.Visibility = Visibility.Visible;

      foreach (ReportTableRow row in ReportTableBuilder.BuildRows(file))
        _rows.Add(row);

      SyncCustomFieldsPanel();
    }

    private BcfFile GetSelectedFile()
    {
      if (_container?.BcfFiles == null || _container.BcfFiles.Count == 0)
        return null;
      int index = _container.SelectedReportIndex;
      if (index < 0 || index >= _container.BcfFiles.Count)
        return null;
      return _container.BcfFiles[index];
    }

    private void Container_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
      if (e.PropertyName == nameof(BcfContainer.SelectedReportIndex)
          || e.PropertyName == nameof(BcfContainer.BcfFiles))
        RefreshRows();
    }

    private void ReportCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      RefreshRows();
      SyncCustomFieldsPanel();
    }

    /// <summary>Переименование отчёта прямо в ReportCombo — меняет только отображаемое имя в памяти.</summary>
    private void CommitReportRename()
    {
      if (GetSelectedFile() is not BcfFile file)
        return;

      string text = ReportCombo.Text?.Trim();
      if (string.IsNullOrWhiteSpace(text) || string.Equals(text, file.Filename, StringComparison.Ordinal))
        return;

      file.Filename = text;
    }

    private void ReportCombo_LostFocus(object sender, RoutedEventArgs e) => CommitReportRename();

    private void ReportCombo_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
      if (e.Key == System.Windows.Input.Key.Enter)
      {
        CommitReportRename();
        e.Handled = true;
      }
    }

    private void IssuesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      SyncCustomFieldsPanel();
    }

    private void AddCustomFieldsBtn_Click(object sender, RoutedEventArgs e)
    {
      BcfFile selectedFile = GetSelectedFile();
      if (selectedFile == null)
        return;

      var window = new AddCustomFieldsWindow(selectedFile) { Owner = Window.GetWindow(this) };
      if (window.ShowDialog() != true)
        return;

      BcfFile file = GetSelectedFile();
      if (file != null)
      {
        file.HasBeenSaved = false;
        file.HasCustomFields = file.ReportLevelCustomFields?.Count > 0;
        file.CustomFieldsVisible = true;
        try
        {
          CustomFieldsXmlStore.SaveCanonical(file.TempPath, file.ReportLevelCustomFields);
        }
        catch
        {
        }
      }

      SyncCustomFieldsPanel();
    }

    private void CustomFieldsBlock_FieldsChanged(object sender, EventArgs e)
    {
      BcfFile file = GetSelectedFile();
      if (file == null)
        return;
      file.HasBeenSaved = false;
      try
      {
        CustomFieldsXmlStore.SaveCanonical(file.TempPath, file.ReportLevelCustomFields);
      }
      catch
      {
      }
    }

    private void SyncCustomFieldsPanel()
    {
      BcfFile file = GetSelectedFile();

      if (CustomFieldsBlock != null)
      {
        CustomFieldsBlock.BcfFile = file;
        CustomFieldsBlock.Refresh();
      }
    }

    private void ColumnsBtn_Click(object sender, RoutedEventArgs e)
    {
      var window = new ReportTableColumnsWindow(_columns, CollectAuthorCandidates())
      {
        Owner = Window.GetWindow(this)
      };
      if (window.ShowDialog() != true)
        return;

      _columns = window.ResultColumns ?? ReportTableSettings.LoadColumns();
      _groups = window.ResultGroups ?? ReportTableUserGroups.Load();
      ReportTableSettings.SaveColumns(_columns);
      RebuildColumns();
    }

    private IEnumerable<string> CollectAuthorCandidates()
    {
      var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      BcfFile file = GetSelectedFile();
      if (file?.Issues == null)
        return set;

      foreach (Markup issue in file.Issues)
      {
        if (!string.IsNullOrWhiteSpace(issue?.Topic?.CreationAuthor))
          set.Add(issue.Topic.CreationAuthor.Trim());
        if (issue?.Comment == null)
          continue;
        foreach (Comment comment in issue.Comment)
        {
          if (!string.IsNullOrWhiteSpace(comment?.Author))
            set.Add(comment.Author.Trim());
        }
      }

      return set;
    }

    private void OnTableCommentsChanged(object sender, RoutedEventArgs e)
    {
      BcfFile file = GetSelectedFile();
      if (file != null)
        file.HasBeenSaved = false;

      if (e.OriginalSource is TableCommentCell cell && cell.DataContext is ReportTableRow row)
        ReportTableBuilder.SyncRowTexts(row);
    }

    private void ImportExcelBtn_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        if (_container == null)
          return;

        if (_container.BcfFiles == null || _container.BcfFiles.Count == 0)
          _container.NewFile();

        BcfFile file = GetSelectedFile();
        if (file == null)
        {
          MessageBox.Show(Loc.ExcelImportNoReport, Loc.Warning, MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
        }

        var dialog = new OpenFileDialog
        {
          Filter = Loc.ExcelImportFilter,
          CheckFileExists = true,
          Multiselect = false
        };
        if (dialog.ShowDialog() != true)
          return;

        var window = new ExcelImportWindow(dialog.FileName, file, _columns)
        {
          Owner = Window.GetWindow(this)
        };
        if (window.ShowDialog() == true)
        {
          _container.UpdateDropdowns();
          RefreshRows();
        }
      }
      catch (Exception ex)
      {
        ExceptionUi.Show(ex);
      }
    }

    private enum ExportFormat
    {
      Excel,
      Html,
      Pdf
    }

    private void ExportExcelBtn_Click(object sender, RoutedEventArgs e)
    {
      Export(ExportFormat.Excel);
    }

    private void ExportHtmlBtn_Click(object sender, RoutedEventArgs e)
    {
      Export(ExportFormat.Html);
    }

    private void ExportPdfBtn_Click(object sender, RoutedEventArgs e)
    {
      Export(ExportFormat.Pdf);
    }

    private void Export(ExportFormat format)
    {
      try
      {
        BcfFile file = GetSelectedFile();
        if (file == null || _rows.Count == 0)
        {
          MessageBox.Show(
            Loc.TableExportEmpty,
            Loc.Warning,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
          return;
        }

        SyncAllRowTexts();

        string filter;
        string ext;
        switch (format)
        {
          case ExportFormat.Excel:
            filter = Loc.TableExportExcelFilter;
            ext = ".xlsx";
            break;
          case ExportFormat.Pdf:
            filter = Loc.TableExportPdfFilter;
            ext = ".pdf";
            break;
          default:
            filter = Loc.TableExportHtmlFilter;
            ext = ".html";
            break;
        }

        var dialog = new SaveFileDialog
        {
          Filter = filter,
          FileName = SanitizeFileName(file.Filename) + ext,
          AddExtension = true,
          DefaultExt = ext
        };

        if (dialog.ShowDialog() != true)
          return;

        List<ReportTableColumnConfig> visible = GetVisibleColumns();
        switch (format)
        {
          case ExportFormat.Excel:
            ReportTableExporter.ExportExcel(dialog.FileName, _rows.ToList(), visible, _groups);
            break;
          case ExportFormat.Pdf:
            ReportTableExporter.ExportPdf(dialog.FileName, _rows.ToList(), visible, file.Filename, _groups);
            break;
          default:
            ReportTableExporter.ExportHtml(dialog.FileName, _rows.ToList(), visible, file.Filename, _groups);
            break;
        }
      }
      catch (Exception ex)
      {
        ExceptionUi.Show(ex);
      }
    }

    private List<ReportTableColumnConfig> GetVisibleColumns()
    {
      return (_columns ?? ReportTableSettings.LoadColumns())
        .Where(c => c.Visible)
        .OrderBy(c => c.Order)
        .ToList();
    }

    private void RebuildColumns()
    {
      IssuesGrid.Columns.Clear();

      var rowNumberCol = new DataGridTextColumn
      {
        Header = "#",
        Binding = new Binding(nameof(ReportTableRow.RowNumber)),
        Width = new DataGridLength(52),
        IsReadOnly = true,
        CanUserSort = false,
        CanUserReorder = false,
        ElementStyle = CreateCellTextStyle(TextAlignment.Center, HorizontalAlignment.Center),
        HeaderStyle = CreateHeaderStyle(TextAlignment.Center, HorizontalAlignment.Center)
      };
      IssuesGrid.Columns.Add(rowNumberCol);

      foreach (ReportTableColumnConfig config in GetVisibleColumns())
      {
        switch (config.Kind)
        {
          case ReportTableColumnKind.Snapshot:
            IssuesGrid.Columns.Add(CreateSnapshotColumn(config));
            break;
          case ReportTableColumnKind.DescriptionAndSnapshot:
            IssuesGrid.Columns.Add(CreateDescriptionSnapshotColumn(config));
            break;
          case ReportTableColumnKind.TopicStatus:
            IssuesGrid.Columns.Add(CreateTemplateColumn(config, "TopicStatusCellTemplate", 120));
            break;
          case ReportTableColumnKind.TopicType:
            IssuesGrid.Columns.Add(CreateTemplateColumn(config, "TopicTypeCellTemplate", 110));
            break;
          case ReportTableColumnKind.Priority:
            IssuesGrid.Columns.Add(CreateTemplateColumn(config, "PriorityCellTemplate", 100));
            break;
          case ReportTableColumnKind.AssignedTo:
            IssuesGrid.Columns.Add(CreateTemplateColumn(config, "AssignedToCellTemplate", 120));
            break;
          case ReportTableColumnKind.Labels:
            IssuesGrid.Columns.Add(CreateTemplateColumn(config, "LabelsCellTemplate", 110));
            break;
          case ReportTableColumnKind.DueDate:
            IssuesGrid.Columns.Add(CreateTemplateColumn(config, "DueDateCellTemplate", 150));
            break;
          case ReportTableColumnKind.Title:
            IssuesGrid.Columns.Add(CreateEditableTextColumn(config, "Issue.Topic.Title", star: 1.4));
            break;
          case ReportTableColumnKind.Description:
            IssuesGrid.Columns.Add(CreateEditableTextColumn(config, "Issue.Topic.Description", star: 1.6));
            break;
          case ReportTableColumnKind.TitleAndSnapshot:
            IssuesGrid.Columns.Add(CreateTitleSnapshotColumn(config));
            break;
          case ReportTableColumnKind.Stage:
            IssuesGrid.Columns.Add(CreateEditableTextColumn(config, "Issue.Topic.Stage", star: 1));
            break;
          case ReportTableColumnKind.Comments:
            IssuesGrid.Columns.Add(CreateCommentsColumn(config));
            break;
          default:
            IssuesGrid.Columns.Add(CreateReadOnlyValuesColumn(config));
            break;
        }
      }
    }

    private DataGridTextColumn CreateEditableTextColumn(
      ReportTableColumnConfig config,
      string bindingPath,
      double star)
    {
      return new DataGridTextColumn
      {
        Header = config.EffectiveHeader,
        Binding = new Binding(bindingPath)
        {
          Mode = BindingMode.TwoWay,
          UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
          FallbackValue = string.Empty,
          TargetNullValue = string.Empty
        },
        Width = new DataGridLength(star, DataGridLengthUnitType.Star),
        MinWidth = 80,
        IsReadOnly = false,
        ElementStyle = CreateCellTextStyle(config.ToTextAlignment(), config.ToHorizontalAlignment()),
        HeaderStyle = CreateHeaderStyle(config.ToTextAlignment(), config.ToHorizontalAlignment())
      };
    }

    private DataGridTextColumn CreateReadOnlyValuesColumn(ReportTableColumnConfig config)
    {
      bool readOnly = ReadOnlyKinds.Contains(config.Kind);
      return new DataGridTextColumn
      {
        Header = config.EffectiveHeader,
        Binding = new Binding($"Values[{config.Kind}]")
        {
          Mode = BindingMode.OneWay,
          FallbackValue = string.Empty
        },
        Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        MinWidth = 80,
        IsReadOnly = readOnly,
        ElementStyle = CreateCellTextStyle(config.ToTextAlignment(), config.ToHorizontalAlignment()),
        HeaderStyle = CreateHeaderStyle(config.ToTextAlignment(), config.ToHorizontalAlignment())
      };
    }

    private DataGridTemplateColumn CreateTemplateColumn(
      ReportTableColumnConfig config,
      string templateKey,
      double minWidth)
    {
      return new DataGridTemplateColumn
      {
        Header = config.EffectiveHeader,
        Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        MinWidth = minWidth,
        IsReadOnly = false,
        HeaderStyle = CreateHeaderStyle(config.ToTextAlignment(), config.ToHorizontalAlignment()),
        CellTemplate = TryFindResource(templateKey) as DataTemplate
      };
    }

    private Style CreateCellTextStyle(TextAlignment textAlign, HorizontalAlignment horizontal)
    {
      var style = new Style(typeof(TextBlock), TryFindResource("SpReportTableCellText") as Style);
      style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, textAlign));
      style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, horizontal));
      return style;
    }

    private Style CreateHeaderStyle(TextAlignment textAlign, HorizontalAlignment horizontal)
    {
      var baseHeader = TryFindResource("SpReportDataGridColumnHeader") as Style;
      var style = new Style(typeof(DataGridColumnHeader), baseHeader);
      style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, horizontal));
      return style;
    }

    private DataGridTemplateColumn CreateCommentsColumn(ReportTableColumnConfig config)
    {
      var factory = new FrameworkElementFactory(typeof(TableCommentCell));
      factory.SetValue(TableCommentCell.ColumnConfigProperty, config);
      factory.SetValue(TableCommentCell.GroupsProperty, _groups);

      return new DataGridTemplateColumn
      {
        Header = config.EffectiveHeader,
        Width = new DataGridLength(1.4, DataGridLengthUnitType.Star),
        MinWidth = 160,
        IsReadOnly = false,
        HeaderStyle = CreateHeaderStyle(config.ToTextAlignment(), config.ToHorizontalAlignment()),
        CellTemplate = new DataTemplate { VisualTree = factory }
      };
    }

    private DataGridTemplateColumn CreateSnapshotColumn(ReportTableColumnConfig config)
    {
      return new DataGridTemplateColumn
      {
        Header = config.EffectiveHeader,
        Width = new DataGridLength(170),
        MinWidth = 120,
        IsReadOnly = true,
        HeaderStyle = CreateHeaderStyle(config.ToTextAlignment(), config.ToHorizontalAlignment()),
        CellTemplate = TryFindResource("SnapshotOnlyCellTemplate") as DataTemplate
      };
    }

    private DataGridTemplateColumn CreateDescriptionSnapshotColumn(ReportTableColumnConfig config)
    {
      return new DataGridTemplateColumn
      {
        Header = config.EffectiveHeader,
        Width = new DataGridLength(2, DataGridLengthUnitType.Star),
        MinWidth = 160,
        IsReadOnly = false,
        HeaderStyle = CreateHeaderStyle(config.ToTextAlignment(), config.ToHorizontalAlignment()),
        CellTemplate = TryFindResource("DescriptionSnapshotCellTemplate") as DataTemplate
      };
    }

    private DataGridTemplateColumn CreateTitleSnapshotColumn(ReportTableColumnConfig config)
    {
      return new DataGridTemplateColumn
      {
        Header = config.EffectiveHeader,
        Width = new DataGridLength(1.4, DataGridLengthUnitType.Star),
        MinWidth = 140,
        IsReadOnly = false,
        HeaderStyle = CreateHeaderStyle(config.ToTextAlignment(), config.ToHorizontalAlignment()),
        CellTemplate = TryFindResource("TitleSnapshotCellTemplate") as DataTemplate
      };
    }

    private void IssuesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
      if (e.EditAction != DataGridEditAction.Commit)
        return;

      if (e.EditingElement is TextBox textBox)
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

      MarkDirtyAndSync(e.Row?.Item as ReportTableRow);
    }

    private void TableCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (sender is not ComboBox combo)
        return;
      // Игнор инициализации биндинга при виртуализации строк.
      if (!combo.IsKeyboardFocusWithin && !combo.IsDropDownOpen)
        return;

      MarkDirtyAndSync(combo.DataContext as ReportTableRow);
    }

    private void TableAssignedTo_LostFocus(object sender, RoutedEventArgs e)
    {
      if (sender is not ComboBox combo)
        return;

      combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
      MarkDirtyAndSync(combo.DataContext as ReportTableRow);
    }

    private void TableDescription_LostFocus(object sender, RoutedEventArgs e)
    {
      if (sender is not TextBox textBox)
        return;

      textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
      MarkDirtyAndSync(textBox.DataContext as ReportTableRow);
    }

    private void TableTitle_LostFocus(object sender, RoutedEventArgs e)
    {
      if (sender is not TextBox textBox)
        return;

      textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
      MarkDirtyAndSync(textBox.DataContext as ReportTableRow);
    }

    private void TableLabelsCombo_Loaded(object sender, RoutedEventArgs e)
    {
      if (sender is not ComboBox combo)
        return;

      var row = combo.DataContext as ReportTableRow;
      Topic topic = row?.Issue?.Topic;
      if (topic == null)
        return;

      BcfIssueHelper.SyncLabelsFromTopic(topic);
      string selected = topic.SelectedLabels?.FirstOrDefault()
                       ?? topic.Labels?.FirstOrDefault();

      SuppressEvent.SetSuppress(combo, true);
      try
      {
        combo.SelectedItem = selected;
      }
      finally
      {
        SuppressEvent.SetSuppress(combo, false);
      }
    }

    private void TableLabelsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (sender is not ComboBox combo || SuppressEvent.GetSuppress(combo))
        return;

      var row = combo.DataContext as ReportTableRow;
      Topic topic = row?.Issue?.Topic;
      if (topic == null)
        return;

      if (topic.SelectedLabels == null)
        topic.SelectedLabels = new ObservableCollection<string>();

      topic.SelectedLabels.Clear();
      if (combo.SelectedItem is string label && !string.IsNullOrWhiteSpace(label))
        topic.SelectedLabels.Add(label);

      BcfIssueHelper.SyncLabelsToTopic(topic);
      MarkDirtyAndSync(row);
    }

    private void TableDueDate_Loaded(object sender, RoutedEventArgs e)
    {
      if (sender is not DatePicker picker)
        return;

      var row = picker.DataContext as ReportTableRow;
      Topic topic = row?.Issue?.Topic;
      if (topic == null)
        return;

      SuppressEvent.SetSuppress(picker, true);
      try
      {
        picker.SelectedDate = topic.DueDateSpecified ? topic.DueDate : (DateTime?)null;
      }
      finally
      {
        SuppressEvent.SetSuppress(picker, false);
      }
    }

    private void TableDueDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
      if (sender is not DatePicker picker || SuppressEvent.GetSuppress(picker))
        return;

      var row = picker.DataContext as ReportTableRow;
      Topic topic = row?.Issue?.Topic;
      if (topic == null)
        return;

      BcfIssueHelper.ApplyDueDate(topic, picker.SelectedDate);
      MarkDirtyAndSync(row);
    }

    private void MarkDirtyAndSync(ReportTableRow row)
    {
      BcfFile file = GetSelectedFile();
      if (file != null)
        file.HasBeenSaved = false;

      ReportTableBuilder.SyncRowTexts(row);
    }

    private void SyncAllRowTexts()
    {
      foreach (ReportTableRow row in _rows)
        ReportTableBuilder.SyncRowTexts(row);
    }

    /// <summary>
    /// Добавляет снимок/вид к замечанию: Revit — диалог вида, Win — выбор изображения.
    /// </summary>
    private void AddSnapshot_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        var row = (sender as FrameworkElement)?.DataContext as ReportTableRow;
        if (row?.Issue == null)
          return;

        Window owner = Window.GetWindow(this);
        IInputElement target = owner ?? (IInputElement)this;
        if (Commands.AddView.CanExecute(row.Issue, target))
          Commands.AddView.Execute(row.Issue, target);

        BcfFile file = GetSelectedFile();
        if (file != null)
          file.HasBeenSaved = false;

        RefreshRows();
      }
      catch (Exception ex)
      {
        ExceptionUi.Show(ex);
      }
    }

    private static string SanitizeFileName(string name)
    {
      if (string.IsNullOrWhiteSpace(name))
        return "BCF";
      foreach (char c in System.IO.Path.GetInvalidFileNameChars())
        name = name.Replace(c, '_');
      return name;
    }

    /// <summary>
    /// Флаг подавления обработчика SelectionChanged/SelectedDateChanged при программной установке
    /// значения. Отдельное attached-свойство, а не Tag — Tag уже занят темой (watermark в DatePicker).
    /// </summary>
    private static class SuppressEvent
    {
      private static readonly DependencyProperty SuppressProperty = DependencyProperty.RegisterAttached(
        "Suppress", typeof(bool), typeof(SuppressEvent), new PropertyMetadata(false));

      public static void SetSuppress(DependencyObject obj, bool value) => obj.SetValue(SuppressProperty, value);

      public static bool GetSuppress(DependencyObject obj) => (bool)obj.GetValue(SuppressProperty);
    }
  }
}
