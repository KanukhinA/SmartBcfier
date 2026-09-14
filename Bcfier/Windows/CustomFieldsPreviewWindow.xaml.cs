using System;
using System.Windows;
using System.Windows.Input;
using Bcfier.Bcf;
using Bcfier.CustomFields;
using Bcfier.Localization;
using Bcfier.Themes;

namespace Bcfier.Windows
{
  /// <summary>Предпросмотр пользовательских полей из Documents при открытии BCF.</summary>
  public partial class CustomFieldsPreviewWindow : Window
  {
    /// <summary>True, если пользователь выбрал показ полей в UI.</summary>
    public bool ShowCustomFields { get; private set; }

    public CustomFieldsPreviewWindow(BcfFile file)
    {
      Loc.ApplyCultureFromSettings();
      InitializeComponent();

      ReportNameText.Text = file?.Filename ?? string.Empty;
      PreviewGrid.ItemsSource = CustomFieldPreviewRow.BuildFrom(file);
    }

    private void Show_Click(object sender, RoutedEventArgs e)
    {
      ShowCustomFields = true;
      DialogResult = true;
      Close();
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
      ShowCustomFields = false;
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
  }
}
