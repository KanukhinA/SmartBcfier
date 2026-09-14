using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Bcfier.Bcf;
using Bcfier.CustomFields;

namespace Bcfier.UserControls
{
  /// <summary>Отображение пользовательских полей отчёта отдельной группой.</summary>
  public partial class CustomFieldsDisplay : UserControl
  {
    public static readonly DependencyProperty BcfFileProperty =
      DependencyProperty.Register(
        nameof(BcfFile),
        typeof(BcfFile),
        typeof(CustomFieldsDisplay),
        new PropertyMetadata(null, OnDepsChanged));

    public event EventHandler FieldsChanged;

    public CustomFieldsDisplay()
    {
      InitializeComponent();
    }

    public BcfFile BcfFile
    {
      get => (BcfFile)GetValue(BcfFileProperty);
      set => SetValue(BcfFileProperty, value);
    }

    private static void OnDepsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      (d as CustomFieldsDisplay)?.Refresh();
    }

    public void Refresh()
    {
      if (ReportFieldsList == null || FieldsFrame == null)
        return;

      BcfFile file = BcfFile;
      bool showUi = file != null && file.CustomFieldsVisible;
      ObservableCollection<CustomFieldValue> report = file?.ReportLevelCustomFields;
      bool hasFields = report != null && report.Count > 0;

      Visibility = showUi && hasFields ? Visibility.Visible : Visibility.Collapsed;
      FieldsFrame.Visibility = hasFields ? Visibility.Visible : Visibility.Collapsed;
      if (!showUi || !hasFields)
        return;

      ReportFieldsList.ItemsSource = report;
    }

    private void ReportValue_LostFocus(object sender, RoutedEventArgs e)
    {
      if (BcfFile != null)
        BcfFile.HasBeenSaved = false;
      FieldsChanged?.Invoke(this, EventArgs.Empty);
    }
  }
}
