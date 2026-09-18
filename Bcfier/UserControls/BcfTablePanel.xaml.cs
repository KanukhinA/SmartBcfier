using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Bcfier.CustomFields;
using Bcfier.Data;
using Bcfier.Data.Utils;
using Bcfier.Localization;
using Bcfier.ReportTable;
using Bcfier.SpService;
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
    private bool _tableLayoutScheduled;
    private bool _updatingTableLayout;

    public BcfTablePanel()
    {
      InitializeComponent();
      IssuesGrid.ItemsSource = _rows;
      _columns = ReportTableSettings.LoadColumns();
      AddHandler(TableCommentCell.CommentsChangedEvent, new RoutedEventHandler(OnTableCommentsChanged));
      RebuildColumns();
      Loaded += (_, __) => ScheduleTableAddIssueLayout();
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
        ScheduleTableAddIssueLayout();
        return;
      }

      EmptyHint.Visibility = Visibility.Collapsed;
      IssuesGrid.Visibility = Visibility.Visible;

      foreach (ReportTableRow row in ReportTableBuilder.BuildRows(file))
        _rows.Add(row);

      SyncCustomFieldsPanel();
      ScheduleTableAddIssueLayout();
    }

    private void TableHost_SizeChanged(object sender, SizeChangedEventArgs e) => ScheduleTableAddIssueLayout();

    private void IssuesGrid_LoadingRow(object sender, DataGridRowEventArgs e) => ScheduleTableAddIssueLayout();

    private void ScheduleTableAddIssueLayout()
    {
      if (_tableLayoutScheduled)
        return;
      _tableLayoutScheduled = true;
      Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
      {
        _tableLayoutScheduled = false;
        UpdateTableAddIssueLayout();
      }));
    }

    /// <summary>
    /// Короткая таблица: «+ замечание» сразу под последней строкой.
    /// Длинная: кнопка снизу области, сетка со скроллом.
    /// </summary>
    private void UpdateTableAddIssueLayout()
    {
      if (_updatingTableLayout || TableHost == null || IssuesContentRow == null || TableFillerRow == null)
        return;
      if (TableHost.ActualHeight <= 0)
        return;

      _updatingTableLayout = true;
      try
      {
        AddIssueBtn.Measure(new Size(TableHost.ActualWidth, double.PositiveInfinity));
        double buttonH = Math.Max(AddIssueBtn.DesiredSize.Height, AddIssueBtn.ActualHeight);
        if (buttonH <= 0)
          buttonH = 32;

        double available = TableHost.ActualHeight;
        double maxGridH = Math.Max(0, available - buttonH);
        double contentH = MeasureIssuesGridContentHeight();

        if (contentH <= maxGridH + 0.5)
        {
          IssuesContentRow.Height = new GridLength(Math.Max(contentH, 1));
          TableFillerRow.Height = new GridLength(1, GridUnitType.Star);
        }
        else
        {
          IssuesContentRow.Height = new GridLength(1, GridUnitType.Star);
          TableFillerRow.Height = new GridLength(0);
        }
      }
      finally
      {
        _updatingTableLayout = false;
      }
    }

    private double MeasureIssuesGridContentHeight()
    {
      if (EmptyHint.Visibility == Visibility.Visible || IssuesGrid.Visibility != Visibility.Visible)
        return Math.Min(140, Math.Max(80, TableHost.ActualHeight * 0.25));

      double header = IssuesGrid.ColumnHeaderHeight;
      if (double.IsNaN(header) || header <= 0)
        header = 36;

      double minRow = IssuesGrid.MinRowHeight;
      if (double.IsNaN(minRow) || minRow <= 0)
        minRow = 36;

      if (_rows.Count == 0)
        return header + minRow;

      double measuredSum = 0;
      int measuredCount = 0;
      for (int i = 0; i < _rows.Count; i++)
      {
        if (IssuesGrid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row
            && row.ActualHeight > 0)
        {
          measuredSum += row.ActualHeight;
          measuredCount++;
        }
      }

      double avg = measuredCount > 0 ? measuredSum / measuredCount : minRow;
      if (avg < minRow)
        avg = minRow;

      return header + avg * _rows.Count + 2;
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

    private void DocumentBtn_Click(object sender, RoutedEventArgs e)
    {
      BcfFile file = GetSelectedFile();
      if (file == null)
        return;

      var window = new ReportDocumentWindow(file.DocumentSettings, file.ReportLevelCustomFields)
      {
        Owner = Window.GetWindow(this)
      };

      if (window.ShowDialog() != true)
        return;

      file.DocumentSettings.CopyFrom(window.ResultDocument);

      file.ReportLevelCustomFields.Clear();
      foreach (CustomFieldValue field in window.ResultFields ?? new List<CustomFieldValue>())
        file.ReportLevelCustomFields.Add(field);

      file.HasBeenSaved = false;
      file.HasCustomFields = file.ReportLevelCustomFields.Count > 0;

      try
      {
        CustomFieldsXmlStore.SaveCanonical(
          file.TempPath,
          file.ReportLevelCustomFields,
          file.DocumentSettings);
      }
      catch
      {
      }

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
          CustomFieldsXmlStore.SaveCanonical(
            file.TempPath,
            file.ReportLevelCustomFields,
            file.DocumentSettings);
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
        CustomFieldsXmlStore.SaveCanonical(
          file.TempPath,
          file.ReportLevelCustomFields,
          file.DocumentSettings);
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

    private async void ExportGoogleSheetsBtn_Click(object sender, RoutedEventArgs e)
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

        if (!file.IsFromServer || file.ServerBcfFileId == null)
        {
          MessageBox.Show(
            Loc.GoogleSheetsExportNeedServer,
            Loc.Warning,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
          return;
        }

        var dialog = new GoogleSheetsExportWindow { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
          return;

        SyncAllRowTexts();
        List<ReportTableColumnConfig> visible = GetVisibleColumns()
          .Where(c => c.Kind != ReportTableColumnKind.Snapshot
                      && c.Kind != ReportTableColumnKind.Comments
                      && c.Kind != ReportTableColumnKind.DescriptionAndSnapshot
                      && c.Kind != ReportTableColumnKind.TitleAndSnapshot)
          .ToList();

        bool hasGuid = visible.Any(c => c.Kind == ReportTableColumnKind.Guid);
        if (!hasGuid)
        {
          visible.Add(new ReportTableColumnConfig
          {
            Kind = ReportTableColumnKind.Guid,
            Visible = true,
            Order = int.MaxValue
          });
        }

        var headers = new List<string>();
        var kinds = new List<string>();
        foreach (ReportTableColumnConfig config in visible)
        {
          headers.Add(config.EffectiveHeader);
          kinds.Add(config.Kind.ToString());
        }

        var rows = new List<List<string>>();
        foreach (ReportTableRow row in _rows)
        {
          var cells = new List<string>();
          foreach (ReportTableColumnConfig config in visible)
            cells.Add(row.GetText(config.Kind) ?? string.Empty);
          rows.Add(cells);
        }

        SpBcfServiceSettings settings = SpBcfServiceSettingsStore.Load();
        using var client = new SpBcfServiceClient(settings.BaseUrl);
        await client.LoginAsync(settings.Login, settings.Password).ConfigureAwait(true);

        if (Guid.TryParse(settings.ProjectId, out Guid projectId))
        {
          SpBcfServiceClient.ProjectMyRole role = await client.GetMyRoleAsync(projectId).ConfigureAwait(true);
          if (role == null || !role.CanModerate)
          {
            MessageBox.Show(
              Loc.GoogleSheetsExportNeedModerator,
              Loc.Warning,
              MessageBoxButton.OK,
              MessageBoxImage.Warning);
            return;
          }
        }

        var request = new SpBcfServiceClient.GoogleSheetsExportRequest
        {
          SpreadsheetId = string.IsNullOrWhiteSpace(dialog.SpreadsheetId) ? null : dialog.SpreadsheetId,
          SheetName = dialog.SheetName,
          Headers = headers,
          ColumnKinds = kinds,
          Rows = rows
        };

        SpBcfServiceClient.GoogleSheetsExportResult result =
          await client.ExportGoogleSheetsAsync(file.ServerBcfFileId.Value, request).ConfigureAwait(true);

        MessageBox.Show(
          Loc.Format("GoogleSheetsExportOk", result.SpreadsheetUrl ?? result.SpreadsheetId),
          Loc.GoogleSheetsExportTitle,
          MessageBoxButton.OK,
          MessageBoxImage.Information);
      }
      catch (Exception ex)
      {
        ExceptionUi.Show(ex);
      }
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
        var context = new ReportExportContext(
          file.Filename,
          file.DocumentSettings,
          file.ReportLevelCustomFields,
          _groups);

        switch (format)
        {
          case ExportFormat.Excel:
            ReportTableExporter.ExportExcel(dialog.FileName, _rows.ToList(), visible, _groups);
            break;
          case ExportFormat.Pdf:
            ReportTableExporter.ExportPdf(
              dialog.FileName, _rows.ToList(), visible, file.Filename, _groups, context);
            break;
          default:
            ReportTableExporter.ExportHtml(
              dialog.FileName, _rows.ToList(), visible, file.Filename, _groups, context);
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

    /// <summary>Замечание, которому принадлежит вид — у ViewPoint нет обратной ссылки.</summary>
    private Markup FindIssueForViewpoint(ViewPoint view)
    {
      if (view == null)
        return null;

      foreach (ReportTableRow row in _rows)
      {
        if (row?.Issue?.Viewpoints == null)
          continue;
        if (row.Issue.Viewpoints.Contains(view))
          return row.Issue;
      }

      return null;
    }

    private void ExecuteViewCommand(System.Windows.Input.RoutedCommand command, object sender)
    {
      var view = (sender as FrameworkElement)?.DataContext as ViewPoint;
      Markup issue = FindIssueForViewpoint(view);
      if (view == null || issue == null)
        return;

      Window owner = Window.GetWindow(this);
      IInputElement target = owner ?? (IInputElement)this;
      // OnEditView/OnDeleteView ждут пару [вид, замечание]
      var parameter = new object[] { view, issue };
      if (command.CanExecute(parameter, target))
        command.Execute(parameter, target);

      BcfFile file = GetSelectedFile();
      if (file != null)
        file.HasBeenSaved = false;

      RefreshRows();
    }

    private void EditViewMenuItem_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        ExecuteViewCommand(Commands.EditView, sender);
      }
      catch (Exception ex)
      {
        ExceptionUi.Show(ex);
      }
    }

    private void DeleteViewMenuItem_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        ExecuteViewCommand(Commands.DeleteViews, sender);
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
