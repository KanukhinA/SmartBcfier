using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Bcfier.CustomFields;
using Bcfier.Localization;
using Bcfier.ReportTable;
using Bcfier.Themes;

namespace Bcfier.Windows
{
  /// <summary>Оформление протокола: шапка документа и блоки пользовательских полей.</summary>
  public partial class ReportDocumentWindow : Window
  {
    private readonly ObservableCollection<CustomFieldValue> _fields;

    /// <summary>Настройки после нажатия "Сохранить".</summary>
    public ReportDocumentSettings ResultDocument { get; private set; }

    /// <summary>Поля шапки в заданном порядке, со значениями и блоками.</summary>
    public List<CustomFieldValue> ResultFields { get; private set; }

    public ObservableCollection<string> SectionSuggestions { get; } =
      new ObservableCollection<string>();

    public ReportDocumentWindow(
      ReportDocumentSettings document,
      IEnumerable<CustomFieldValue> reportFields)
    {
      Loc.ApplyCultureFromSettings();
      InitializeComponent();
      DataContext = this;

      ReportDocumentSettings source = document ?? new ReportDocumentSettings();
      TitleBox.Text = source.Title ?? string.Empty;
      SubtitleBox.Text = source.Subtitle ?? string.Empty;
      ShowBlocksCheck.IsChecked = source.ShowFieldBlocks;
      ShowNumbersCheck.IsChecked = source.ShowRowNumbers;

      _fields = new ObservableCollection<CustomFieldValue>(
        (reportFields ?? Enumerable.Empty<CustomFieldValue>()).Select(Clone));

      foreach (string section in _fields
                 .Select(f => (f.Section ?? string.Empty).Trim())
                 .Where(s => !string.IsNullOrEmpty(s))
                 .Distinct(StringComparer.OrdinalIgnoreCase))
      {
        SectionSuggestions.Add(section);
      }

      FieldsList.ItemsSource = _fields;
      NoFieldsText.Visibility = _fields.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
      if (_fields.Count > 0)
        FieldsList.SelectedIndex = 0;
    }

    private static CustomFieldValue Clone(CustomFieldValue source)
    {
      return new CustomFieldValue
      {
        Id = source?.Id ?? string.Empty,
        Name = source?.Name ?? string.Empty,
        Section = source?.Section ?? string.Empty,
        Value = source?.Value ?? string.Empty
      };
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
      int index = FieldsList.SelectedIndex;
      if (index <= 0)
        return;
      _fields.Move(index, index - 1);
      FieldsList.SelectedIndex = index - 1;
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
      int index = FieldsList.SelectedIndex;
      if (index < 0 || index >= _fields.Count - 1)
        return;
      _fields.Move(index, index + 1);
      FieldsList.SelectedIndex = index + 1;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
      ResultDocument = new ReportDocumentSettings
      {
        Title = (TitleBox.Text ?? string.Empty).Trim(),
        Subtitle = (SubtitleBox.Text ?? string.Empty).Trim(),
        ShowFieldBlocks = ShowBlocksCheck.IsChecked == true,
        ShowRowNumbers = ShowNumbersCheck.IsChecked == true
      };

      foreach (CustomFieldValue field in _fields)
        field.Section = (field.Section ?? string.Empty).Trim();

      ResultFields = _fields.ToList();
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
        // chrome не критичен
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
        // ignore
      }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      if (e.ChangedButton == MouseButton.Left)
        DragMove();
    }
  }
}
