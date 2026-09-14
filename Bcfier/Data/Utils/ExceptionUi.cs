using System;
using System.Windows;
using Bcfier.Localization;

namespace Bcfier.Data.Utils
{
  /// <summary>
  /// Показ ошибок с автоматическим копированием текста в буфер обмена.
  /// </summary>
  public static class ExceptionUi
  {
    /// <summary>
    /// Копирует текст в буфер. Не бросает наружу при занятости clipboard.
    /// </summary>
    public static void CopyToClipboard(string text)
    {
      if (string.IsNullOrEmpty(text))
        return;

      try
      {
        Clipboard.SetDataObject(text, true);
      }
      catch
      {
        try
        {
          Clipboard.SetText(text);
        }
        catch
        {
          // буфер может быть занят другим процессом
        }
      }
    }

    /// <summary>
    /// Форматирует исключение для диалога и буфера.
    /// </summary>
    public static string Format(Exception ex)
    {
      return ex == null ? string.Empty : "exception: " + ex;
    }

    /// <summary>
    /// Копирует ошибку в буфер и показывает MessageBox.
    /// </summary>
    public static void Show(Exception ex, string title = null)
    {
      Show(Format(ex), title);
    }

    /// <summary>
    /// Копирует текст ошибки в буфер и показывает MessageBox.
    /// </summary>
    public static void Show(string message, string title = null)
    {
      if (string.IsNullOrWhiteSpace(message))
        return;

      CopyToClipboard(message);

      try
      {
        MessageBox.Show(
          message,
          string.IsNullOrWhiteSpace(title) ? Loc.Error : title,
          MessageBoxButton.OK,
          MessageBoxImage.Error);
      }
      catch
      {
        // UI хост может быть недоступен
      }
    }
  }
}
