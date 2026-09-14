using System;
using Autodesk.Revit.UI;
using Bcfier.Data.Utils;

namespace Bcfier.Revit
{
  /// <summary>
  /// Диалоги ошибок Revit: копирует текст в буфер и показывает TaskDialog.
  /// </summary>
  internal static class RevitExceptionUi
  {
    /// <summary>
    /// Копирует исключение в буфер обмена и показывает TaskDialog.
    /// </summary>
    public static void Show(Exception ex, string title = "Error!")
    {
      Show(ExceptionUi.Format(ex), title);
    }

    /// <summary>
    /// Копирует текст ошибки в буфер обмена и показывает TaskDialog.
    /// </summary>
    public static void Show(string message, string title = "Error!")
    {
      if (string.IsNullOrWhiteSpace(message))
        return;

      ExceptionUi.CopyToClipboard(message);

      try
      {
        TaskDialog.Show(
          string.IsNullOrWhiteSpace(title) ? "Error!" : title,
          message);
      }
      catch
      {
        ExceptionUi.Show(message, title);
      }
    }
  }
}
