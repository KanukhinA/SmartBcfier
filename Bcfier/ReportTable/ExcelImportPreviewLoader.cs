using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bcfier.Localization;
using ClosedXML.Excel;

namespace Bcfier.ReportTable
{
  /// <summary>Чтение Excel для предпросмотра и полного импорта (ClosedXML).</summary>
  public static class ExcelImportPreviewLoader
  {
    public const int DefaultPreviewRows = 40;

    /// <summary>Список имён листов книги.</summary>
    public static IReadOnlyList<string> GetSheetNames(string path)
    {
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        return Array.Empty<string>();

      using (var workbook = new XLWorkbook(path))
      {
        return workbook.Worksheets.Select(w => w.Name).ToList();
      }
    }

    /// <summary>Загружает заголовки и sample-строки выбранного листа.</summary>
    public static ExcelImportPreview Load(
      string path,
      string sheetName,
      bool hasHeaderRow,
      int maxDataRows = DefaultPreviewRows)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
          return new ExcelImportPreview
          {
            ErrorMessage = Loc.ExcelImportFileMissing
          };
        }

        using (var workbook = new XLWorkbook(path))
        {
          IReadOnlyList<string> sheets = workbook.Worksheets.Select(w => w.Name).ToList();
          if (sheets.Count == 0)
          {
            return new ExcelImportPreview
            {
              SheetNames = sheets,
              ErrorMessage = Loc.ExcelImportSheetEmpty
            };
          }

          IXLWorksheet sheet = ResolveSheet(workbook, sheetName) ?? workbook.Worksheets.First();
          if (!TryGetUsedBounds(sheet, out int firstRow, out int lastRow, out int firstCol, out int lastCol))
          {
            return new ExcelImportPreview
            {
              SheetNames = sheets,
              SheetName = sheet.Name,
              HasHeaderRow = hasHeaderRow,
              ErrorMessage = Loc.ExcelImportSheetEmpty
            };
          }

          int colCount = lastCol - firstCol + 1;
          var headers = new List<string>(colCount);
          for (int c = 0; c < colCount; c++)
          {
            if (hasHeaderRow)
            {
              string header = GetCellText(sheet, firstRow, firstCol + c)?.Trim() ?? string.Empty;
              headers.Add(string.IsNullOrWhiteSpace(header)
                ? Loc.Format("ExcelImportColumnN", c + 1)
                : header);
            }
            else
            {
              headers.Add(Loc.Format("ExcelImportColumnN", c + 1));
            }
          }

          int dataStart = hasHeaderRow ? firstRow + 1 : firstRow;
          var rows = new List<IReadOnlyList<string>>();
          int end = Math.Min(lastRow, dataStart + Math.Max(0, maxDataRows) - 1);
          for (int r = dataStart; r <= end; r++)
          {
            var cells = new string[colCount];
            bool any = false;
            for (int c = 0; c < colCount; c++)
            {
              cells[c] = GetCellText(sheet, r, firstCol + c) ?? string.Empty;
              if (!string.IsNullOrWhiteSpace(cells[c]))
                any = true;
            }

            if (any)
              rows.Add(cells);
          }

          return new ExcelImportPreview
          {
            SheetNames = sheets,
            SheetName = sheet.Name,
            HasHeaderRow = hasHeaderRow,
            Headers = headers,
            Rows = rows
          };
        }
      }
      catch (Exception ex)
      {
        return new ExcelImportPreview
        {
          ErrorMessage = Loc.Format("ExcelImportReadError", ex.Message)
        };
      }
    }

    /// <summary>Читает все непустые строки данных листа (для импорта).</summary>
    public static IReadOnlyList<IReadOnlyList<string>> LoadAllDataRows(
      string path,
      string sheetName,
      bool hasHeaderRow,
      out IReadOnlyList<string> headers,
      out IReadOnlyList<byte[]> rowImages,
      out string error)
    {
      headers = Array.Empty<string>();
      rowImages = Array.Empty<byte[]>();
      error = null;

      try
      {
        using (var workbook = new XLWorkbook(path))
        {
          IXLWorksheet sheet = ResolveSheet(workbook, sheetName) ?? workbook.Worksheets.FirstOrDefault();
          if (sheet == null
              || !TryGetUsedBounds(sheet, out int firstRow, out int lastRow, out int firstCol, out int lastCol))
          {
            error = Loc.ExcelImportSheetEmpty;
            return Array.Empty<IReadOnlyList<string>>();
          }

          int colCount = lastCol - firstCol + 1;
          var headerList = new List<string>(colCount);
          for (int c = 0; c < colCount; c++)
          {
            if (hasHeaderRow)
            {
              string header = GetCellText(sheet, firstRow, firstCol + c)?.Trim() ?? string.Empty;
              headerList.Add(string.IsNullOrWhiteSpace(header)
                ? Loc.Format("ExcelImportColumnN", c + 1)
                : header);
            }
            else
            {
              headerList.Add(Loc.Format("ExcelImportColumnN", c + 1));
            }
          }

          headers = headerList;

          // Картинки, вставленные в ячейки — по номеру строки листа; берём первую на строку.
          var imagesBySheetRow = new Dictionary<int, byte[]>();
          try
          {
            foreach (var picture in sheet.Pictures)
            {
              int r = picture.TopLeftCell.Address.RowNumber;
              if (imagesBySheetRow.ContainsKey(r))
                continue;

              using (var ms = new MemoryStream())
              {
                picture.ImageStream.Position = 0;
                picture.ImageStream.CopyTo(ms);
                imagesBySheetRow[r] = ms.ToArray();
              }
            }
          }
          catch
          {
            // Картинки не критичны для импорта текста
          }

          int dataStart = hasHeaderRow ? firstRow + 1 : firstRow;
          var rows = new List<IReadOnlyList<string>>();
          var images = new List<byte[]>();
          for (int r = dataStart; r <= lastRow; r++)
          {
            var cells = new string[colCount];
            bool any = imagesBySheetRow.ContainsKey(r);
            for (int c = 0; c < colCount; c++)
            {
              cells[c] = GetCellText(sheet, r, firstCol + c) ?? string.Empty;
              if (!string.IsNullOrWhiteSpace(cells[c]))
                any = true;
            }

            if (any)
            {
              rows.Add(cells);
              images.Add(imagesBySheetRow.TryGetValue(r, out byte[] img) ? img : null);
            }
          }

          rowImages = images;
          return rows;
        }
      }
      catch (Exception ex)
      {
        error = Loc.Format("ExcelImportReadError", ex.Message);
        return Array.Empty<IReadOnlyList<string>>();
      }
    }

    private static IXLWorksheet ResolveSheet(XLWorkbook workbook, string sheetName)
    {
      if (string.IsNullOrWhiteSpace(sheetName))
        return null;
      return workbook.Worksheets.FirstOrDefault(w =>
        string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetUsedBounds(
      IXLWorksheet sheet,
      out int firstRow,
      out int lastRow,
      out int firstCol,
      out int lastCol)
    {
      firstRow = lastRow = firstCol = lastCol = 0;
      if (sheet == null || sheet.IsEmpty())
        return false;

      IXLRange used = sheet.RangeUsed();
      if (used == null)
        return false;

      firstRow = used.FirstRow().RowNumber();
      lastRow = used.LastRow().RowNumber();
      firstCol = used.FirstColumn().ColumnNumber();
      lastCol = used.LastColumn().ColumnNumber();
      return lastRow >= firstRow && lastCol >= firstCol;
    }

    private static string GetCellText(IXLWorksheet sheet, int row, int col)
    {
      IXLCell cell = sheet.Cell(row, col);
      if (cell == null || cell.IsEmpty())
        return string.Empty;

      if (cell.DataType == XLDataType.DateTime)
      {
        try
        {
          return cell.GetDateTime().ToString("g");
        }
        catch
        {
          // fall through
        }
      }

      return cell.GetFormattedString()?.Trim() ?? cell.GetString()?.Trim() ?? string.Empty;
    }
  }
}
