using System.Windows;

namespace Bcfier.Windows
{
  public partial class GoogleSheetsExportWindow : Window
  {
    public GoogleSheetsExportWindow()
    {
      InitializeComponent();
    }

    public string SpreadsheetId => SpreadsheetIdBox.Text?.Trim() ?? string.Empty;

    public string SheetName =>
      string.IsNullOrWhiteSpace(SheetNameBox.Text) ? "BCF" : SheetNameBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
      DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
      DialogResult = false;
    }
  }
}
