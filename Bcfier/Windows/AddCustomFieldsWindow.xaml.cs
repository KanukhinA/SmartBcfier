using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Bcfier.Bcf;
using Bcfier.CustomFields;
using Bcfier.Localization;
using Bcfier.Themes;

namespace Bcfier.Windows
{
  /// <summary>Диалог управления пользовательскими полями отчёта: добавить и снять.</summary>
  public partial class AddCustomFieldsWindow : Window
  {
    private readonly BcfFile _reportFile;
    private readonly ObservableCollection<FieldPickItem> _items = new ObservableCollection<FieldPickItem>();

    public AddCustomFieldsWindow(BcfFile reportFile)
    {
      _reportFile = reportFile ?? throw new ArgumentNullException(nameof(reportFile));
      Loc.ApplyCultureFromSettings();
      InitializeComponent();
      HintText.Text = Loc.CustomFieldsManageHint;

      List<CustomFieldValue> existing = (_reportFile.ReportLevelCustomFields
        ?? Enumerable.Empty<CustomFieldValue>()).ToList();
      HashSet<string> existingIds = new HashSet<string>(
        existing.Select(f => f.Id).Where(id => !string.IsNullOrWhiteSpace(id)),
        StringComparer.OrdinalIgnoreCase);

      foreach (CustomFieldDefinition def in CustomFieldDefinitionsStore.Load())
      {
        if (string.IsNullOrWhiteSpace(def?.Id))
          continue;

        _items.Add(new FieldPickItem
        {
          Id = def.Id,
          Name = string.IsNullOrWhiteSpace(def.Name) ? def.Id : def.Name,
          IsSelected = existingIds.Contains(def.Id)
        });
        existingIds.Remove(def.Id);
      }

      // Поля в отчёте, которых уже нет в каталоге — чтобы их можно было снять
      foreach (CustomFieldValue field in existing)
      {
        string id = field?.Id;
        if (string.IsNullOrWhiteSpace(id) || !existingIds.Contains(id))
          continue;

        _items.Add(new FieldPickItem
        {
          Id = id,
          Name = string.IsNullOrWhiteSpace(field.DisplayName) ? id : field.DisplayName,
          IsSelected = true
        });
      }

      FieldsList.ItemsSource = _items;
      EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
      OkButton.IsEnabled = _items.Count > 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
      ObservableCollection<CustomFieldValue> target = _reportFile.ReportLevelCustomFields;
      HashSet<string> selectedIds = new HashSet<string>(
        _items.Where(i => i.IsSelected && !string.IsNullOrWhiteSpace(i.Id)).Select(i => i.Id),
        StringComparer.OrdinalIgnoreCase);

      for (int i = target.Count - 1; i >= 0; i--)
      {
        CustomFieldValue field = target[i];
        string id = field?.Id;
        if (string.IsNullOrWhiteSpace(id) || !selectedIds.Contains(id))
          target.RemoveAt(i);
      }

      HashSet<string> presentIds = new HashSet<string>(
        target.Select(f => f.Id).Where(id => !string.IsNullOrWhiteSpace(id)),
        StringComparer.OrdinalIgnoreCase);

      foreach (FieldPickItem item in _items.Where(i => i.IsSelected))
      {
        if (string.IsNullOrWhiteSpace(item.Id) || presentIds.Contains(item.Id))
          continue;

        target.Add(new CustomFieldValue
        {
          Id = item.Id,
          Name = item.Name,
          Value = string.Empty
        });
        presentIds.Add(item.Id);
      }

      _reportFile.HasCustomFields = target.Count > 0;
      DialogResult = true;
      Close();
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
      }
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
      }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      if (e.ChangedButton == MouseButton.Left)
        DragMove();
    }

    private sealed class FieldPickItem : INotifyPropertyChanged
    {
      private bool _isSelected = true;

      public event PropertyChangedEventHandler PropertyChanged;

      public string Id { get; set; }

      public string Name { get; set; }

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
    }
  }
}
