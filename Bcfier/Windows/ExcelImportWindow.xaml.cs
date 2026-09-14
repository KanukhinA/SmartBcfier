using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Bcfier.Bcf;
using Bcfier.Data;
using Bcfier.Localization;
using Bcfier.ReportTable;
using Bcfier.Themes;

namespace Bcfier.Windows
{
  /// <summary>Диалог импорта Excel: предпросмотр и сопоставление колонок с полями BCF.</summary>
  public partial class ExcelImportWindow : Window, INotifyPropertyChanged
  {
    private readonly string _path;
    private readonly BcfFile _file;
    private readonly List<ReportTableColumnConfig> _savedColumns;
    private readonly ObservableCollection<ExcelImportFieldMapItem> _mapItems =
      new ObservableCollection<ExcelImportFieldMapItem>();
    private ObservableCollection<ExcelImportColumnOption> _columnOptions =
      new ObservableCollection<ExcelImportColumnOption>();
    private bool _suppressReload;

    public event PropertyChangedEventHandler PropertyChanged;

    public ObservableCollection<ExcelImportColumnOption> ColumnOptions
    {
      get => _columnOptions;
      private set
      {
        _columnOptions = value;
        OnPropertyChanged();
      }
    }

    public int CreatedCount { get; private set; }

    public ExcelImportWindow(string path, BcfFile file, IList<ReportTableColumnConfig> savedColumns = null)
    {
      Loc.ApplyCultureFromSettings();
      InitializeComponent();
      DataContext = this;
      _path = path;
      _file = file;
      _savedColumns = savedColumns?.ToList() ?? ReportTableSettings.LoadColumns();
      MappingList.ItemsSource = _mapItems;
      HasHeaderCheck.IsChecked = true;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      try
      {
        SpWindowChrome.Apply(this);
        SpWindowChrome.EnsureHittableBackground(this);
        ApplyChromeClip();
      }
      catch
      {
        // chrome не критичен
      }

      try
      {
        IReadOnlyList<string> sheets = ExcelImportPreviewLoader.GetSheetNames(_path);
        _suppressReload = true;
        SheetCombo.ItemsSource = sheets;
        if (sheets.Count > 0)
          SheetCombo.SelectedIndex = 0;
        _suppressReload = false;
        ReloadPreview();
      }
      catch (Exception ex)
      {
        ShowPreviewError(Loc.Format("ExcelImportReadError", ex.Message));
      }
    }

    private void SheetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (_suppressReload)
        return;
      ReloadPreview();
    }

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
      if (!IsLoaded || _suppressReload)
        return;
      ReloadPreview();
    }

    private void ReloadPreview()
    {
      string sheet = SheetCombo.SelectedItem as string;
      bool hasHeader = HasHeaderCheck.IsChecked == true;
      ExcelImportPreview preview = ExcelImportPreviewLoader.Load(_path, sheet, hasHeader);

      if (!preview.HasData)
      {
        ShowPreviewError(string.IsNullOrWhiteSpace(preview.ErrorMessage)
          ? Loc.ExcelImportSheetEmpty
          : preview.ErrorMessage);
        BuildColumnOptions(Array.Empty<string>());
        BuildMappingItems(null);
        ImportBtn.IsEnabled = false;
        return;
      }

      PreviewError.Visibility = Visibility.Collapsed;
      PreviewGrid.Visibility = Visibility.Visible;
      BuildPreviewGrid(preview);
      BuildColumnOptions(preview.Headers);
      BuildMappingItems(preview.Headers);
      ImportBtn.IsEnabled = true;
      StatusText.Text = string.Empty;
    }

    private void ShowPreviewError(string message)
    {
      PreviewGrid.ItemsSource = null;
      PreviewGrid.Columns.Clear();
      PreviewGrid.Visibility = Visibility.Collapsed;
      PreviewError.Text = message;
      PreviewError.Visibility = Visibility.Visible;
    }

    private void BuildPreviewGrid(ExcelImportPreview preview)
    {
      PreviewGrid.Columns.Clear();
      var rows = preview.Rows.Select(r => new ExcelImportPreviewRow(r)).ToList();

      for (int i = 0; i < preview.Headers.Count; i++)
      {
        int colIndex = i;
        PreviewGrid.Columns.Add(new DataGridTextColumn
        {
          Header = preview.Headers[i],
          Binding = new Binding("[" + colIndex + "]"),
          Width = new DataGridLength(1, DataGridLengthUnitType.Star),
          MinWidth = 80,
          IsReadOnly = true
        });
      }

      PreviewGrid.ItemsSource = rows;
    }

    private void BuildColumnOptions(IReadOnlyList<string> headers)
    {
      var options = new ObservableCollection<ExcelImportColumnOption>
      {
        new ExcelImportColumnOption { Index = null, Display = Loc.ExcelImportNotMapped }
      };

      if (headers != null)
      {
        for (int i = 0; i < headers.Count; i++)
        {
          string letter = ColumnLetter(i);
          options.Add(new ExcelImportColumnOption
          {
            Index = i,
            Display = letter + " — " + headers[i]
          });
        }
      }

      ColumnOptions = options;
    }

    private void BuildMappingItems(IReadOnlyList<string> headers)
    {
      var mapping = new ExcelImportMapping
      {
        SheetName = SheetCombo.SelectedItem as string,
        HasHeaderRow = HasHeaderCheck.IsChecked == true
      };
      if (headers != null)
        mapping.AutoMap(headers, _savedColumns);

      _mapItems.Clear();
      foreach (ReportTableColumnKind kind in GetMappableKinds())
      {
        _mapItems.Add(new ExcelImportFieldMapItem
        {
          Kind = kind,
          FieldName = ReportTableColumnConfig.GetDefaultHeader(kind),
          IsRequired = kind == ReportTableColumnKind.Title,
          SelectedColumnIndex = mapping.GetColumn(kind)
        });
      }
    }

    /// <summary>
    /// Поля для сопоставления: только колонки, включённые в текущих настройках таблицы —
    /// плюс Title, он всегда доступен как основное поле замечания (само по себе не обязательно:
    /// без него тема просто создастся без названия, его можно будет задать позже в интерфейсе).
    /// </summary>
    private IEnumerable<ReportTableColumnKind> GetMappableKinds()
    {
      var visibleKinds = new HashSet<ReportTableColumnKind>(
        (_savedColumns ?? Enumerable.Empty<ReportTableColumnConfig>())
          .Where(c => c.Visible)
          .Select(c => c.Kind));

      foreach (ReportTableColumnKind kind in ExcelImportMapping.ImportableKinds)
      {
        if (kind == ReportTableColumnKind.Title || visibleKinds.Contains(kind))
          yield return kind;
      }
    }

    private ExcelImportMapping BuildMappingFromUi()
    {
      var mapping = new ExcelImportMapping
      {
        SheetName = SheetCombo.SelectedItem as string,
        HasHeaderRow = HasHeaderCheck.IsChecked == true
      };

      foreach (ExcelImportFieldMapItem item in _mapItems)
        mapping.SetColumn(item.Kind, item.SelectedColumnIndex);

      return mapping;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
      ExcelImportMapping mapping = BuildMappingFromUi();

      try
      {
        ImportBtn.IsEnabled = false;
        StatusText.Text = Loc.ExcelImportWorking;
        ExcelImportResult result = ExcelImportService.Import(
          _path,
          mapping,
          _file,
          BcfAuthorContext.ResolveAuthor());

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage) && result.CreatedCount == 0)
        {
          StatusText.Text = result.ErrorMessage;
          MessageBox.Show(result.ErrorMessage, Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
          ImportBtn.IsEnabled = true;
          return;
        }

        CreatedCount = result.CreatedCount;
        MessageBox.Show(
          Loc.Format("ExcelImportResult", result.CreatedCount, result.SkippedCount),
          Loc.ExcelImportTitle,
          MessageBoxButton.OK,
          MessageBoxImage.Information);
        DialogResult = true;
        Close();
      }
      catch (Exception ex)
      {
        StatusText.Text = ex.Message;
        MessageBox.Show(ex.Message, Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
        ImportBtn.IsEnabled = true;
      }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
      DialogResult = false;
      Close();
    }

    private void HeaderClose_Click(object sender, RoutedEventArgs e)
    {
      DialogResult = false;
      Close();
    }

    private void Window_StateChanged(object sender, EventArgs e) => ApplyChromeClip();

    private void ChromeRoot_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyChromeClip();

    private void ApplyChromeClip()
    {
      try
      {
        double radius = SpWindowChrome.GetWindowClipRadius(this);
        SpWindowChrome.ClipToRoundedRect(ChromeRoot, radius);
      }
      catch
      {
        // ignore
      }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      if (e.ChangedButton == MouseButton.Left)
        DragMove();
    }

    private static string ColumnLetter(int zeroBasedIndex)
    {
      int n = zeroBasedIndex;
      string s = string.Empty;
      do
      {
        s = (char)('A' + n % 26) + s;
        n = n / 26 - 1;
      } while (n >= 0);
      return s;
    }

    private void OnPropertyChanged([CallerMemberName] string name = null)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
  }
}
