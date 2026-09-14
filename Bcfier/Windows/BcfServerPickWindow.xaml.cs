using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Bcfier.Localization;
using Bcfier.Themes;

namespace Bcfier.Windows
{
  /// <summary>Диалог выбора BCF-файла на SP-Service.</summary>
  public partial class BcfServerPickWindow : Window
  {
    public SpService.SpBcfServiceClient.BcfFileItem SelectedFile { get; private set; }

    public BcfServerPickWindow(IEnumerable<SpService.SpBcfServiceClient.BcfFileItem> files)
    {
      Loc.ApplyCultureFromSettings();
      InitializeComponent();
      FilesList.ItemsSource = (files ?? Enumerable.Empty<SpService.SpBcfServiceClient.BcfFileItem>()).ToList();
      if (FilesList.Items.Count > 0)
        FilesList.SelectedIndex = 0;
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
      try
      {
        if (e.LeftButton == MouseButtonState.Pressed)
          SpWindowChrome.DragMove(this);
      }
      catch
      {
        // ignore
      }
    }

    private void HeaderClose_Click(object sender, RoutedEventArgs e)
    {
      try { DialogResult = false; }
      catch { try { Close(); } catch { /* ignore */ } }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => AcceptSelection();

    private void FilesList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AcceptSelection();

    private void AcceptSelection()
    {
      SelectedFile = FilesList.SelectedItem as SpService.SpBcfServiceClient.BcfFileItem;
      if (SelectedFile == null)
      {
        MessageBox.Show(Loc.Get("SpServiceSelectBcfFile"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }

      DialogResult = true;
    }
  }
}
