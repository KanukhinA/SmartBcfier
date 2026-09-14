using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Bcfier.Localization;
using ClosedXML.Excel;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Bcfier.ReportTable
{
  /// <summary>Экспорт табличного режима в Excel (.xlsx), HTML и PDF.</summary>
  public static class ReportTableExporter
  {
    private const int ImageMaxHeightPx = 96;
    private const int ImageMaxWidthPx = 160;

    private const double PdfMargin = 36;
    private const double PdfImageMaxWidth = 220;
    private const double PdfImageMaxHeight = 140;
    private const double PdfLineHeight = 13;

    /// <summary>Сохраняет .xlsx с видимыми колонками; первый столбец — порядковый номер.</summary>
    public static void ExportExcel(
      string path,
      IList<ReportTableRow> rows,
      IList<ReportTableColumnConfig> visibleColumns,
      IList<ReportTableUserGroup> groups = null)
    {
      if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("path");

      using (var workbook = new XLWorkbook())
      {
        IXLWorksheet sheet = workbook.Worksheets.Add("BCF");
        WriteHeader(sheet, visibleColumns);

        int r = 2;
        foreach (ReportTableRow row in rows ?? Array.Empty<ReportTableRow>())
        {
          IXLCell numCell = sheet.Cell(r, 1);
          numCell.Value = row.RowNumber;
          numCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

          int c = 2;
          bool hasImage = false;
          foreach (ReportTableColumnConfig config in visibleColumns)
          {
            IXLCell cell = sheet.Cell(r, c);
            ApplyExcelAlign(cell, config.TextAlign);

            if (config.Kind == ReportTableColumnKind.Snapshot)
            {
              hasImage = TryAddPicture(sheet, cell, row.SnapshotPath) || hasImage;
            }
            else if (config.Kind == ReportTableColumnKind.DescriptionAndSnapshot)
            {
              cell.Value = row.Description ?? string.Empty;
              cell.Style.Alignment.WrapText = true;
              hasImage = TryAddPicture(sheet, cell, row.SnapshotPath, offsetY: 18) || hasImage;
            }
            else if (config.Kind == ReportTableColumnKind.TitleAndSnapshot)
            {
              cell.Value = row.GetText(ReportTableColumnKind.Title);
              cell.Style.Alignment.WrapText = true;
              hasImage = TryAddPicture(sheet, cell, row.SnapshotPath, offsetY: 18) || hasImage;
            }
            else if (config.Kind == ReportTableColumnKind.Comments)
            {
              cell.Value = ReportTableCommentHelper.FormatForColumn(row.Issue, config, groups);
              cell.Style.Alignment.WrapText = true;
            }
            else
            {
              cell.Value = row.GetText(config.Kind);
              cell.Style.Alignment.WrapText = true;
            }

            c++;
          }

          if (hasImage)
            sheet.Row(r).Height = 90;

          r++;
        }

        sheet.Columns().AdjustToContents(1, 48);
        sheet.Column(1).Width = 6;
        workbook.SaveAs(path);
      }
    }

    /// <summary>Сохраняет самодостаточный HTML с base64-картинками.</summary>
    public static void ExportHtml(
      string path,
      IList<ReportTableRow> rows,
      IList<ReportTableColumnConfig> visibleColumns,
      string reportTitle,
      IList<ReportTableUserGroup> groups = null)
    {
      if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("path");

      var sb = new StringBuilder();
      sb.AppendLine("<!DOCTYPE html>");
      sb.AppendLine("<html><head><meta charset=\"utf-8\"/>");
      sb.Append("<title>").Append(HtmlEncode(reportTitle ?? "BCF")).AppendLine("</title>");
      sb.AppendLine("<style>");
      sb.AppendLine("body{font-family:Segoe UI,Roboto,Arial,sans-serif;font-size:13px;color:#1a1d26;margin:24px;background:#f5f6f8;}");
      sb.AppendLine("h1{font-size:18px;font-weight:600;margin:0 0 16px;}");
      sb.AppendLine("table{border-collapse:collapse;width:100%;background:#fff;border:1px solid #d8dbe3;border-radius:8px;overflow:hidden;}");
      sb.AppendLine("th,td{border-bottom:1px solid #e6e8ef;padding:8px 10px;vertical-align:top;}");
      sb.AppendLine("th{background:#eef0f5;font-weight:600;font-size:12px;}");
      sb.AppendLine("tr:nth-child(even) td{background:#fafbfc;}");
      sb.AppendLine("td.num{width:48px;color:#6b7280;text-align:center;}");
      sb.AppendLine("th.num{text-align:center;}");
      sb.AppendLine("img.snap{max-width:160px;max-height:96px;display:inline-block;margin-top:6px;border-radius:4px;}");
      sb.AppendLine(".desc{white-space:pre-wrap;}");
      sb.AppendLine(".a-left{text-align:left;}");
      sb.AppendLine(".a-center{text-align:center;}");
      sb.AppendLine(".a-right{text-align:right;}");
      sb.AppendLine("</style></head><body>");
      sb.Append("<h1>").Append(HtmlEncode(reportTitle ?? "BCF")).AppendLine("</h1>");
      sb.AppendLine("<table><thead><tr>");
      sb.Append("<th class=\"num\">#</th>");
      foreach (ReportTableColumnConfig col in visibleColumns)
      {
        sb.Append("<th class=\"").Append(CssAlign(col.TextAlign)).Append("\">")
          .Append(HtmlEncode(col.EffectiveHeader)).Append("</th>");
      }

      sb.AppendLine("</tr></thead><tbody>");

      foreach (ReportTableRow row in rows ?? Array.Empty<ReportTableRow>())
      {
        sb.Append("<tr><td class=\"num\">").Append(row.RowNumber).Append("</td>");
        foreach (ReportTableColumnConfig col in visibleColumns)
        {
          sb.Append("<td class=\"").Append(CssAlign(col.TextAlign)).Append("\">")
            .Append(RenderHtmlCell(row, col, groups)).Append("</td>");
        }

        sb.AppendLine("</tr>");
      }

      sb.AppendLine("</tbody></table></body></html>");
      File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>Сохраняет PDF: один "блок" на замечание — заголовок, поля, снимок.</summary>
    public static void ExportPdf(
      string path,
      IList<ReportTableRow> rows,
      IList<ReportTableColumnConfig> visibleColumns,
      string reportTitle,
      IList<ReportTableUserGroup> groups = null)
    {
      if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("path");

      // Unicode-кодировка обязательна — иначе кириллица не отрисуется в PDF.
      var fontOptions = new XPdfFontOptions(PdfFontEncoding.Unicode);
      var titleFont = new XFont("Arial", 16, XFontStyleEx.Bold, fontOptions);
      var headingFont = new XFont("Arial", 12, XFontStyleEx.Bold, fontOptions);
      var labelFont = new XFont("Arial", 9, XFontStyleEx.Bold, fontOptions);
      var textFont = new XFont("Arial", 10, XFontStyleEx.Regular, fontOptions);

      var document = new PdfDocument();
      document.Info.Title = reportTitle ?? "BCF";

      PdfPage page = null;
      XGraphics gfx = null;
      double y = 0;
      double pageWidth = 0;
      double pageHeight = 0;
      double contentWidth = 0;

      void NewPage()
      {
        page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        gfx?.Dispose();
        gfx = XGraphics.FromPdfPage(page);
        pageWidth = page.Width.Point;
        pageHeight = page.Height.Point;
        contentWidth = pageWidth - PdfMargin * 2;
        y = PdfMargin;
      }

      void EnsureSpace(double needed)
      {
        if (y + needed > pageHeight - PdfMargin)
          NewPage();
      }

      List<string> WrapText(string text, XFont font, double maxWidth)
      {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text))
          return result;

        foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
          var line = new StringBuilder();
          foreach (string word in paragraph.Split(' '))
          {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && gfx.MeasureString(candidate, font).Width > maxWidth)
            {
              result.Add(line.ToString());
              line.Clear();
              line.Append(word);
            }
            else
            {
              line.Clear();
              line.Append(candidate);
            }
          }

          result.Add(line.ToString());
        }

        return result;
      }

      void DrawField(string label, string value)
      {
        if (string.IsNullOrWhiteSpace(value))
          return;

        List<string> lines = WrapText(value, textFont, contentWidth - 10);
        EnsureSpace(PdfLineHeight + lines.Count * PdfLineHeight);
        gfx.DrawString(label + ":", labelFont, XBrushes.Black, new XRect(PdfMargin, y, contentWidth, PdfLineHeight), XStringFormats.TopLeft);
        y += PdfLineHeight;
        foreach (string line in lines)
        {
          EnsureSpace(PdfLineHeight);
          gfx.DrawString(line, textFont, XBrushes.Black, new XRect(PdfMargin + 10, y, contentWidth - 10, PdfLineHeight), XStringFormats.TopLeft);
          y += PdfLineHeight;
        }

        y += 3;
      }

      NewPage();
      gfx.DrawString(reportTitle ?? "BCF", titleFont, XBrushes.Black, new XRect(PdfMargin, y, contentWidth, 24), XStringFormats.TopLeft);
      y += 30;

      foreach (ReportTableRow row in rows ?? Array.Empty<ReportTableRow>())
      {
        List<string> headingLines = WrapText(
          row.RowNumber + ". " + row.GetText(ReportTableColumnKind.Title),
          headingFont,
          contentWidth);
        foreach (string line in headingLines)
        {
          EnsureSpace(18);
          gfx.DrawString(line, headingFont, XBrushes.Black, new XRect(PdfMargin, y, contentWidth, 18), XStringFormats.TopLeft);
          y += 18;
        }

        y += 4;

        string imagePath = null;
        foreach (ReportTableColumnConfig config in visibleColumns)
        {
          switch (config.Kind)
          {
            case ReportTableColumnKind.Title:
              break; // уже вынесен в заголовок блока
            case ReportTableColumnKind.Snapshot:
              if (!string.IsNullOrWhiteSpace(row.SnapshotPath) && File.Exists(row.SnapshotPath))
                imagePath = row.SnapshotPath;
              break;
            case ReportTableColumnKind.DescriptionAndSnapshot:
              DrawField(Loc.Description, row.Description);
              if (!string.IsNullOrWhiteSpace(row.SnapshotPath) && File.Exists(row.SnapshotPath))
                imagePath = row.SnapshotPath;
              break;
            case ReportTableColumnKind.TitleAndSnapshot:
              if (!string.IsNullOrWhiteSpace(row.SnapshotPath) && File.Exists(row.SnapshotPath))
                imagePath = row.SnapshotPath;
              break;
            case ReportTableColumnKind.Comments:
              DrawField(config.EffectiveHeader, ReportTableCommentHelper.FormatForColumn(row.Issue, config, groups));
              break;
            default:
              DrawField(config.EffectiveHeader, row.GetText(config.Kind));
              break;
          }
        }

        if (imagePath != null)
        {
          try
          {
            using (XImage image = XImage.FromFile(imagePath))
            {
              double ratio = Math.Min(PdfImageMaxWidth / image.PixelWidth, PdfImageMaxHeight / image.PixelHeight);
              double w = image.PixelWidth * ratio;
              double h = image.PixelHeight * ratio;
              EnsureSpace(h + 6);
              gfx.DrawImage(image, PdfMargin, y, w, h);
              y += h + 6;
            }
          }
          catch
          {
            // Битый/недоступный файл снимка не должен прерывать экспорт
          }
        }

        y += 6;
        EnsureSpace(1);
        gfx.DrawLine(XPens.LightGray, PdfMargin, y, pageWidth - PdfMargin, y);
        y += 10;
      }

      document.Save(path);
      gfx?.Dispose();
    }

    private static void WriteHeader(IXLWorksheet sheet, IList<ReportTableColumnConfig> visibleColumns)
    {
      IXLCell numHeader = sheet.Cell(1, 1);
      numHeader.Value = "#";
      numHeader.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

      int col = 2;
      foreach (ReportTableColumnConfig config in visibleColumns)
      {
        IXLCell cell = sheet.Cell(1, col);
        cell.Value = config.EffectiveHeader;
        ApplyExcelAlign(cell, config.TextAlign);
        col++;
      }

      IXLRange header = sheet.Range(1, 1, 1, Math.Max(1, visibleColumns.Count + 1));
      header.Style.Font.Bold = true;
      header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF0F5");
    }

    private static void ApplyExcelAlign(IXLCell cell, ReportTableTextAlign align)
    {
      switch (align)
      {
        case ReportTableTextAlign.Center:
          cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
          break;
        case ReportTableTextAlign.Right:
          cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
          break;
        default:
          cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
          break;
      }
    }

    private static string CssAlign(ReportTableTextAlign align)
    {
      switch (align)
      {
        case ReportTableTextAlign.Center:
          return "a-center";
        case ReportTableTextAlign.Right:
          return "a-right";
        default:
          return "a-left";
      }
    }

    private static bool TryAddPicture(
      IXLWorksheet sheet,
      IXLCell cell,
      string imagePath,
      int offsetY = 2)
    {
      if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        return false;

      try
      {
        sheet.AddPicture(imagePath)
          .MoveTo(cell, 4, offsetY)
          .WithSize(ImageMaxWidthPx, ImageMaxHeightPx);
        return true;
      }
      catch
      {
        return false;
      }
    }

    private static string RenderHtmlCell(
      ReportTableRow row,
      ReportTableColumnConfig config,
      IList<ReportTableUserGroup> groups)
    {
      if (config.Kind == ReportTableColumnKind.Snapshot)
        return RenderHtmlImage(row.SnapshotPath);

      if (config.Kind == ReportTableColumnKind.DescriptionAndSnapshot)
      {
        return "<div class=\"desc\">" + HtmlEncode(row.Description) + "</div>"
               + RenderHtmlImage(row.SnapshotPath);
      }

      if (config.Kind == ReportTableColumnKind.TitleAndSnapshot)
      {
        return "<div class=\"desc\">" + HtmlEncode(row.GetText(ReportTableColumnKind.Title)) + "</div>"
               + RenderHtmlImage(row.SnapshotPath);
      }

      if (config.Kind == ReportTableColumnKind.Comments)
      {
        return "<div class=\"desc\">"
               + HtmlEncode(ReportTableCommentHelper.FormatForColumn(row.Issue, config, groups))
               + "</div>";
      }

      return "<div class=\"desc\">" + HtmlEncode(row.GetText(config.Kind)) + "</div>";
    }

    private static string RenderHtmlImage(string path)
    {
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        return string.Empty;

      try
      {
        byte[] bytes = File.ReadAllBytes(path);
        string mime = "image/png";
        string ext = Path.GetExtension(path)?.ToLowerInvariant();
        if (ext == ".jpg" || ext == ".jpeg")
          mime = "image/jpeg";
        else if (ext == ".gif")
          mime = "image/gif";
        else if (ext == ".bmp")
          mime = "image/bmp";

        return "<img class=\"snap\" alt=\"\" src=\"data:" + mime + ";base64,"
               + Convert.ToBase64String(bytes) + "\"/>";
      }
      catch
      {
        return string.Empty;
      }
    }

    private static string HtmlEncode(string value)
    {
      return WebUtility.HtmlEncode(value ?? string.Empty);
    }
  }
}
