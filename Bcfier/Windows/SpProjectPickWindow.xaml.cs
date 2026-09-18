using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Bcfier.Localization;
using Bcfier.SpService;
using Bcfier.Themes;

namespace Bcfier.Windows
{
  /// <summary>Диалог выбора проекта SP-Service (и при необходимости названия отчёта).</summary>
  public partial class SpProjectPickWindow : Window
  {
    private readonly bool _requireReportName;

    public SpBcfServiceClient.ProjectItem SelectedProject { get; private set; }

    /// <summary>Название отчёта, если поле было показано и заполнено.</summary>
    public string SelectedReportName { get; private set; }

    public SpProjectPickWindow(
      IEnumerable<SpBcfServiceClient.ProjectItem> projects,
      Guid? preselectId = null,
      bool requireReportName = false,
      string initialReportName = null)
    {
      Loc.ApplyCultureFromSettings();
      InitializeComponent();
      _requireReportName = requireReportName;

      var list = (projects ?? Enumerable.Empty<SpBcfServiceClient.ProjectItem>())
        .Select(p => new SpBcfServiceClient.ProjectItem
        {
          Id = p.Id,
          Name = string.IsNullOrWhiteSpace(p.Name) ? p.Id.ToString("D") : p.Name
        })
        .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

      ProjectsList.ItemsSource = list;
      if (list.Count > 0)
      {
        SpBcfServiceClient.ProjectItem preselected = null;
        if (preselectId.HasValue)
          preselected = list.FirstOrDefault(p => p.Id == preselectId.Value);
        ProjectsList.SelectedItem = preselected ?? list[0];
      }

      if (_requireReportName)
      {
        ReportNamePanel.Visibility = Visibility.Visible;
        ReportNameBox.Text = string.IsNullOrWhiteSpace(initialReportName)
          ? string.Empty
          : initialReportName.Trim();
        MinHeight = 340;
        Height = Math.Max(Height, 440);
      }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      try
      {
        SpWindowChrome.Apply(this);
        SpWindowChrome.EnsureHittableBackground(this);
        ApplyChromeClip();
        if (_requireReportName && ReportNameBox != null && string.IsNullOrWhiteSpace(ReportNameBox.Text))
          ReportNameBox.Focus();
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

    private void OkButton_Click(object sender, RoutedEventArgs e) => AcceptSelection();

    private void ProjectsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AcceptSelection();

    private void AcceptSelection()
    {
      SelectedProject = ProjectsList.SelectedItem as SpBcfServiceClient.ProjectItem;
      if (SelectedProject == null)
      {
        MessageBox.Show(Loc.Get("SpServiceSelectProject"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }

      if (_requireReportName)
      {
        string name = ReportNameBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
          MessageBox.Show(Loc.Get("SpServiceReportNameRequired"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
          ReportNameBox?.Focus();
          return;
        }

        SelectedReportName = name;
      }

      DialogResult = true;
    }
  }
}
