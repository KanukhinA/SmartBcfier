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
    private const double PdfImageMaxHeight = 110;
    private const double PdfLineHeight = 11;
    private const double PdfCellPadding = 3;

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

    /// <summary>Сохраняет самодостаточный HTML-протокол с base64-картинками.</summary>
    public static void ExportHtml(
      string path,
      IList<ReportTableRow> rows,
      IList<ReportTableColumnConfig> visibleColumns,
      string reportTitle,
      IList<ReportTableUserGroup> groups = null,
      ReportExportContext context = null)
    {
      if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("path");

      context = context ?? new ReportExportContext(reportTitle, null, null, groups);
      IList<ReportTableUserGroup> commentGroups = context.Groups ?? groups;

      var sb = new StringBuilder();
      sb.AppendLine("<!DOCTYPE html>");
      sb.AppendLine("<html><head><meta charset=\"utf-8\"/>");
      sb.Append("<title>").Append(HtmlEncode(context.Title)).AppendLine("</title>");
      sb.AppendLine("<style>");
      sb.AppendLine("@page{size:A4;margin:18mm 14mm;}");
      sb.AppendLine("body{font-family:'Times New Roman',Times,serif;font-size:12pt;color:#000;margin:0;background:#fff;}");
      sb.AppendLine(".doc{max-width:900px;margin:0 auto;padding:24px;}");
      sb.AppendLine(".doc-title{text-align:center;font-weight:bold;font-size:12pt;margin:0 0 2px;}");
      sb.AppendLine(".section{font-weight:bold;margin:14px 0 4px;}");
      sb.AppendLine("table{border-collapse:collapse;width:100%;margin:0 0 10px;}");
      sb.AppendLine("table.meta{margin-top:10px;}");
      sb.AppendLine("table.meta td{border:1px solid #000;padding:3px 6px;vertical-align:top;}");
      sb.AppendLine("table.meta td.lbl{width:34%;font-weight:bold;}");
      sb.AppendLine("table.grid th,table.grid td{border:1px solid #000;padding:4px 6px;vertical-align:top;}");
      sb.AppendLine("table.grid th{font-weight:bold;text-align:center;background:#e8e8e8;}");
      sb.AppendLine("td.num{width:42px;text-align:center;}");
      sb.AppendLine("img.snap{max-width:220px;max-height:150px;display:block;margin-top:5px;}");
      sb.AppendLine(".desc{white-space:pre-wrap;}");
      sb.AppendLine(".a-left{text-align:left;}");
      sb.AppendLine(".a-center{text-align:center;}");
      sb.AppendLine(".a-right{text-align:right;}");
      sb.AppendLine("</style></head><body><div class=\"doc\">");

      if (!string.IsNullOrWhiteSpace(context.Title))
        sb.Append("<p class=\"doc-title\">").Append(HtmlEncode(context.Title)).AppendLine("</p>");
      if (!string.IsNullOrWhiteSpace(context.Subtitle))
        sb.Append("<p class=\"doc-title\">").Append(HtmlEncode(context.Subtitle)).AppendLine("</p>");

      foreach (ReportDocumentBlock block in context.Blocks)
      {
        if (!string.IsNullOrWhiteSpace(block.Heading))
          sb.Append("<p class=\"section\">").Append(HtmlEncode(block.Heading)).AppendLine("</p>");

        sb.AppendLine("<table class=\"meta\">");
        foreach (ReportDocumentField field in block.Fields)
        {
          sb.Append("<tr><td class=\"lbl\">").Append(HtmlEncode(field.Label)).Append("</td><td>")
            .Append(HtmlEncode(field.Value)).AppendLine("</td></tr>");
        }

        sb.AppendLine("</table>");
      }

      sb.AppendLine("<table class=\"grid\"><thead><tr>");
      if (context.ShowRowNumbers)
        sb.Append("<th class=\"num\">").Append(HtmlEncode(Loc.TableRowNumberHeader)).Append("</th>");
      foreach (ReportTableColumnConfig col in visibleColumns)
      {
        sb.Append("<th class=\"").Append(CssAlign(col.TextAlign)).Append("\">")
          .Append(HtmlEncode(col.EffectiveHeader)).Append("</th>");
      }

      sb.AppendLine("</tr></thead><tbody>");

      foreach (ReportTableRow row in rows ?? Array.Empty<ReportTableRow>())
      {
        sb.Append("<tr>");
        if (context.ShowRowNumbers)
          sb.Append("<td class=\"num\">").Append(row.RowNumber).Append("</td>");
        foreach (ReportTableColumnConfig col in visibleColumns)
        {
          sb.Append("<td class=\"").Append(CssAlign(col.TextAlign)).Append("\">")
            .Append(RenderHtmlCell(row, col, commentGroups)).Append("</td>");
        }

        sb.AppendLine("</tr>");
      }

      sb.AppendLine("</tbody></table></div></body></html>");
      File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>Сохраняет PDF-протокол: шапка, блоки полей и таблица замечаний с рамками.</summary>
    public static void ExportPdf(
      string path,
      IList<ReportTableRow> rows,
      IList<ReportTableColumnConfig> visibleColumns,
      string reportTitle,
      IList<ReportTableUserGroup> groups = null,
      ReportExportContext context = null)
    {
      if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("path");

      context = context ?? new ReportExportContext(reportTitle, null, null, groups);
      IList<ReportTableUserGroup> commentGroups = context.Groups ?? groups;

      // Unicode-кодировка обязательна — иначе кириллица не отрисуется в PDF.
      var fontOptions = new XPdfFontOptions(PdfFontEncoding.Unicode);
      var titleFont = new XFont("Times New Roman", 12, XFontStyleEx.Bold, fontOptions);
      var sectionFont = new XFont("Times New Roman", 11, XFontStyleEx.Bold, fontOptions);
      var headFont = new XFont("Times New Roman", 9, XFontStyleEx.Bold, fontOptions);
      var textFont = new XFont("Times New Roman", 9, XFontStyleEx.Regular, fontOptions);
      var headerFill = new XSolidBrush(XColor.FromArgb(232, 232, 232));

      var document = new PdfDocument();
      document.Info.Title = context.Title;

      PdfPage page = null;
      XGraphics gfx = null;
      double y = 0;
      double pageHeight = 0;
      double contentWidth = 0;

      void NewPage()
      {
        page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        gfx?.Dispose();
        gfx = XGraphics.FromPdfPage(page);
        pageHeight = page.Height.Point;
        contentWidth = page.Width.Point - PdfMargin * 2;
        y = PdfMargin;
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
            // Слово шире колонки рубим посимвольно, иначе текст вылезет за рамку.
            string piece = word;
            while (gfx.MeasureString(piece, font).Width > maxWidth && piece.Length > 1)
            {
              int fit = piece.Length;
              while (fit > 1 && gfx.MeasureString(piece.Substring(0, fit), font).Width > maxWidth)
                fit--;

              if (line.Length > 0)
              {
                result.Add(line.ToString());
                line.Clear();
              }

              result.Add(piece.Substring(0, fit));
              piece = piece.Substring(fit);
            }

            string candidate = line.Length == 0 ? piece : line + " " + piece;
            if (line.Length > 0 && gfx.MeasureString(candidate, font).Width > maxWidth)
            {
              result.Add(line.ToString());
              line.Clear();
              line.Append(piece);
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

      XSize MeasureImage(string imagePath, double maxWidth)
      {
        try
        {
          using (XImage image = XImage.FromFile(imagePath))
          {
            double ratio = Math.Min(
              Math.Min(maxWidth / image.PixelWidth, PdfImageMaxHeight / image.PixelHeight),
              1.0);
            return new XSize(image.PixelWidth * ratio, image.PixelHeight * ratio);
          }
        }
        catch
        {
          return new XSize(0, 0);
        }
      }

      // Одна строка таблицы: измеряем все ячейки, при нехватке места переносим на новую страницу.
      void DrawGridRow(IList<PdfTableCell> cells, IList<double> widths, bool isHeader, Action repeatHeader)
      {
        XFont font = isHeader ? headFont : textFont;
        var cellLines = new List<List<string>>();
        var cellImages = new List<XSize>();
        double rowHeight = PdfCellPadding * 2;

        for (int i = 0; i < cells.Count; i++)
        {
          double inner = widths[i] - PdfCellPadding * 2;
          List<string> lines = WrapText(cells[i].Text, font, inner);
          XSize imageSize = string.IsNullOrEmpty(cells[i].ImagePath)
            ? new XSize(0, 0)
            : MeasureImage(cells[i].ImagePath, inner);

          cellLines.Add(lines);
          cellImages.Add(imageSize);

          double height = PdfCellPadding * 2 + lines.Count * PdfLineHeight;
          if (imageSize.Height > 0)
            height += imageSize.Height + (lines.Count > 0 ? 4 : 0);
          rowHeight = Math.Max(rowHeight, height);
        }

        double usable = pageHeight - PdfMargin * 2;
        if (rowHeight > usable)
          rowHeight = usable;

        if (y + rowHeight > pageHeight - PdfMargin)
        {
          NewPage();
          repeatHeader?.Invoke();
        }

        double x = PdfMargin;
        for (int i = 0; i < cells.Count; i++)
        {
          gfx.DrawRectangle(XPens.Black, x, y, widths[i], rowHeight);
          if (isHeader)
            gfx.DrawRectangle(headerFill, x + 0.5, y + 0.5, widths[i] - 1, rowHeight - 1);

          double textY = y + PdfCellPadding;
          double inner = widths[i] - PdfCellPadding * 2;
          XStringFormat format = cells[i].Align == ReportTableTextAlign.Center
            ? XStringFormats.TopCenter
            : cells[i].Align == ReportTableTextAlign.Right
              ? XStringFormats.TopRight
              : XStringFormats.TopLeft;

          foreach (string line in cellLines[i])
          {
            if (textY + PdfLineHeight > y + rowHeight - PdfCellPadding + 1)
              break;
            gfx.DrawString(line, font, XBrushes.Black,
              new XRect(x + PdfCellPadding, textY, inner, PdfLineHeight), format);
            textY += PdfLineHeight;
          }

          if (cellImages[i].Height > 0 && textY + cellImages[i].Height <= y + rowHeight)
          {
            try
            {
              using (XImage image = XImage.FromFile(cells[i].ImagePath))
              {
                gfx.DrawImage(image, x + PdfCellPadding, textY + 2,
                  cellImages[i].Width, cellImages[i].Height);
              }
            }
            catch
            {
              // Битый снимок не должен прерывать экспорт
            }
          }

          x += widths[i];
        }

        y += rowHeight;
      }

      void DrawCenteredLine(string text, XFont font)
      {
        if (string.IsNullOrWhiteSpace(text))
          return;

        foreach (string line in WrapText(text, font, contentWidth))
        {
          if (y + 16 > pageHeight - PdfMargin)
            NewPage();
          gfx.DrawString(line, font, XBrushes.Black,
            new XRect(PdfMargin, y, contentWidth, 16), XStringFormats.TopCenter);
          y += 16;
        }
      }

      NewPage();

      DrawCenteredLine(context.Title, titleFont);
      DrawCenteredLine(context.Subtitle, titleFont);
      y += 10;

      foreach (ReportDocumentBlock block in context.Blocks)
      {
        if (!string.IsNullOrWhiteSpace(block.Heading))
        {
          if (y + 18 > pageHeight - PdfMargin)
            NewPage();
          gfx.DrawString(block.Heading, sectionFont, XBrushes.Black,
            new XRect(PdfMargin, y, contentWidth, 16), XStringFormats.TopLeft);
          y += 18;
        }

        var metaWidths = new[] { contentWidth * 0.34, contentWidth * 0.66 };
        foreach (ReportDocumentField field in block.Fields)
        {
          DrawGridRow(
            new[]
            {
              new PdfTableCell { Text = field.Label, Align = ReportTableTextAlign.Left },
              new PdfTableCell { Text = field.Value, Align = ReportTableTextAlign.Left }
            },
            metaWidths,
            false,
            null);
        }

        y += 8;
      }

      double[] columnWidths = BuildPdfColumnWidths(visibleColumns, contentWidth, context.ShowRowNumbers);

      List<PdfTableCell> BuildHeaderCells()
      {
        var cells = new List<PdfTableCell>();
        if (context.ShowRowNumbers)
          cells.Add(new PdfTableCell { Text = Loc.TableRowNumberHeader, Align = ReportTableTextAlign.Center });
        foreach (ReportTableColumnConfig col in visibleColumns)
          cells.Add(new PdfTableCell { Text = col.EffectiveHeader, Align = ReportTableTextAlign.Center });
        return cells;
      }

      void DrawHeader() => DrawGridRow(BuildHeaderCells(), columnWidths, true, null);

      DrawHeader();

      foreach (ReportTableRow row in rows ?? Array.Empty<ReportTableRow>())
      {
        var cells = new List<PdfTableCell>();
        if (context.ShowRowNumbers)
        {
          cells.Add(new PdfTableCell
          {
            Text = row.RowNumber.ToString(),
            Align = ReportTableTextAlign.Center
          });
        }

        foreach (ReportTableColumnConfig col in visibleColumns)
          cells.Add(BuildPdfCell(row, col, commentGroups));

        DrawGridRow(cells, columnWidths, false, DrawHeader);
      }

      document.Save(path);
      gfx?.Dispose();
    }

    private static PdfTableCell BuildPdfCell(
      ReportTableRow row,
      ReportTableColumnConfig config,
      IList<ReportTableUserGroup> groups)
    {
      var cell = new PdfTableCell { Align = config.TextAlign };
      string snapshot = !string.IsNullOrWhiteSpace(row.SnapshotPath) && File.Exists(row.SnapshotPath)
        ? row.SnapshotPath
        : null;

      switch (config.Kind)
      {
        case ReportTableColumnKind.Snapshot:
          cell.ImagePath = snapshot;
          break;
        case ReportTableColumnKind.DescriptionAndSnapshot:
          cell.Text = row.Description ?? string.Empty;
          cell.ImagePath = snapshot;
          break;
        case ReportTableColumnKind.TitleAndSnapshot:
          cell.Text = row.GetText(ReportTableColumnKind.Title);
          cell.ImagePath = snapshot;
          break;
        case ReportTableColumnKind.Comments:
          cell.Text = ReportTableCommentHelper.FormatForColumn(row.Issue, config, groups);
          break;
        default:
          cell.Text = row.GetText(config.Kind);
          break;
      }

      return cell;
    }

    /// <summary>Ширины колонок PDF: текстовым колонкам больше места, датам/статусам меньше.</summary>
    private static double[] BuildPdfColumnWidths(
      IList<ReportTableColumnConfig> visibleColumns,
      double contentWidth,
      bool showRowNumbers)
    {
      double numberWidth = showRowNumbers ? 28 : 0;
      double rest = contentWidth - numberWidth;

      var weights = new List<double>();
      foreach (ReportTableColumnConfig col in visibleColumns)
        weights.Add(PdfColumnWeight(col.Kind));

      double total = 0;
      foreach (double w in weights)
        total += w;
      if (total <= 0)
        total = 1;

      var widths = new List<double>();
      if (showRowNumbers)
        widths.Add(numberWidth);
      foreach (double w in weights)
        widths.Add(rest * w / total);

      return widths.ToArray();
    }

    private static double PdfColumnWeight(ReportTableColumnKind kind)
    {
      switch (kind)
      {
        case ReportTableColumnKind.Title:
        case ReportTableColumnKind.Description:
        case ReportTableColumnKind.TitleAndSnapshot:
        case ReportTableColumnKind.DescriptionAndSnapshot:
        case ReportTableColumnKind.Comments:
          return 3.0;
        case ReportTableColumnKind.Snapshot:
          return 2.0;
        case ReportTableColumnKind.AssignedTo:
        case ReportTableColumnKind.Labels:
        case ReportTableColumnKind.CreationAuthor:
        case ReportTableColumnKind.ModifiedAuthor:
        case ReportTableColumnKind.Guid:
          return 1.5;
        default:
          return 1.1;
      }
    }

    /// <summary>Ячейка таблицы PDF.</summary>
    private sealed class PdfTableCell
    {
      public string Text { get; set; } = string.Empty;

      public string ImagePath { get; set; }

      public ReportTableTextAlign Align { get; set; } = ReportTableTextAlign.Left;
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
