using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Bcfier.Localization;

namespace Bcfier.ReportTable
{
  /// <summary>Результат импорта Excel в BCF-отчёт.</summary>
  public sealed class ExcelImportResult
  {
    public int CreatedCount { get; set; }

    public int SkippedCount { get; set; }

    public string ErrorMessage { get; set; }

    public bool Success => string.IsNullOrWhiteSpace(ErrorMessage) && CreatedCount > 0;
  }

  /// <summary>Создаёт замечания Markup из строк Excel по маппингу колонок.</summary>
  public static class ExcelImportService
  {
    /// <summary>Импортирует все строки листа в указанный BcfFile.</summary>
    public static ExcelImportResult Import(
      string path,
      ExcelImportMapping mapping,
      BcfFile file,
      string creationAuthor)
    {
      var result = new ExcelImportResult();
      if (file == null)
      {
        result.ErrorMessage = Loc.ExcelImportNoReport;
        return result;
      }

      if (mapping == null)
      {
        result.ErrorMessage = Loc.ExcelImportNoReport;
        return result;
      }

      IReadOnlyList<IReadOnlyList<string>> rows = ExcelImportPreviewLoader.LoadAllDataRows(
        path,
        mapping.SheetName,
        mapping.HasHeaderRow,
        out _,
        out IReadOnlyList<byte[]> rowImages,
        out string error);

      if (!string.IsNullOrWhiteSpace(error))
      {
        result.ErrorMessage = error;
        return result;
      }

      if (rows.Count == 0)
      {
        result.ErrorMessage = Loc.ExcelImportNoRows;
        return result;
      }

      string author = string.IsNullOrWhiteSpace(creationAuthor)
        ? BcfAuthorContext.ResolveAuthor()
        : creationAuthor;

      for (int i = 0; i < rows.Count; i++)
      {
        IReadOnlyList<string> row = rows[i];
        try
        {
          var issue = new Markup(DateTime.UtcNow);
          BcfIssueHelper.InitializeNewIssue(issue, author);
          ApplyRow(issue.Topic, row, mapping, author);

          byte[] image = rowImages != null && i < rowImages.Count ? rowImages[i] : null;
          if (image != null)
            TryAttachSnapshot(issue, image, file.TempPath);

          file.Issues.Add(issue);
          result.CreatedCount++;
        }
        catch
        {
          result.SkippedCount++;
        }
      }

      if (result.CreatedCount > 0)
        file.HasBeenSaved = false;

      if (result.CreatedCount == 0 && string.IsNullOrWhiteSpace(result.ErrorMessage))
        result.ErrorMessage = Loc.ExcelImportNoRows;

      return result;
    }

    /// <summary>Сохраняет картинку из ячейки Excel как снимок (viewpoint) нового замечания.</summary>
    private static void TryAttachSnapshot(Markup issue, byte[] imageBytes, string bcfTempFolder)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(bcfTempFolder) || string.IsNullOrWhiteSpace(issue.Topic?.Guid))
          return;

        string topicDir = Path.Combine(bcfTempFolder, issue.Topic.Guid);
        Directory.CreateDirectory(topicDir);

        var view = new ViewPoint(!issue.Viewpoints.Any());
        string snapshotPath = Path.Combine(topicDir, view.Snapshot);
        File.WriteAllBytes(snapshotPath, imageBytes);
        view.SnapshotPath = snapshotPath;
        issue.Viewpoints.Add(view);
      }
      catch
      {
        // Отсутствие снимка не должно срывать импорт самого замечания
      }
    }

    private static void ApplyRow(
      Topic topic,
      IReadOnlyList<string> row,
      ExcelImportMapping mapping,
      string defaultAuthor)
    {
      if (topic == null)
        return;

      topic.Title = GetMapped(row, mapping, ReportTableColumnKind.Title)?.Trim() ?? string.Empty;

      string description = GetMapped(row, mapping, ReportTableColumnKind.Description);
      if (description != null)
        topic.Description = description;

      string status = GetMapped(row, mapping, ReportTableColumnKind.TopicStatus);
      if (!string.IsNullOrWhiteSpace(status))
        topic.TopicStatus = status.Trim();

      string type = GetMapped(row, mapping, ReportTableColumnKind.TopicType);
      if (!string.IsNullOrWhiteSpace(type))
        topic.TopicType = type.Trim();

      string priority = GetMapped(row, mapping, ReportTableColumnKind.Priority);
      if (!string.IsNullOrWhiteSpace(priority))
        topic.Priority = priority.Trim();

      string assigned = GetMapped(row, mapping, ReportTableColumnKind.AssignedTo);
      if (!string.IsNullOrWhiteSpace(assigned))
        topic.AssignedTo = assigned.Trim();

      string stage = GetMapped(row, mapping, ReportTableColumnKind.Stage);
      if (!string.IsNullOrWhiteSpace(stage))
        topic.Stage = stage.Trim();

      string labels = GetMapped(row, mapping, ReportTableColumnKind.Labels);
      if (!string.IsNullOrWhiteSpace(labels))
      {
        topic.SelectedLabels = new ObservableCollection<string>(
          labels.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        BcfIssueHelper.SyncLabelsToTopic(topic);
      }

      string dueRaw = GetMapped(row, mapping, ReportTableColumnKind.DueDate);
      if (TryParseDate(dueRaw, out DateTime dueDate))
        BcfIssueHelper.ApplyDueDate(topic, dueDate);

      string creationAuthor = GetMapped(row, mapping, ReportTableColumnKind.CreationAuthor);
      if (!string.IsNullOrWhiteSpace(creationAuthor))
        topic.CreationAuthor = creationAuthor.Trim();
      else if (string.IsNullOrWhiteSpace(topic.CreationAuthor))
        topic.CreationAuthor = defaultAuthor ?? string.Empty;

      string creationDate = GetMapped(row, mapping, ReportTableColumnKind.CreationDate);
      if (TryParseDate(creationDate, out DateTime created))
        topic.CreationDate = created.ToUniversalTime();

      string modifiedAuthor = GetMapped(row, mapping, ReportTableColumnKind.ModifiedAuthor);
      if (!string.IsNullOrWhiteSpace(modifiedAuthor))
        topic.ModifiedAuthor = modifiedAuthor.Trim();

      string modifiedDate = GetMapped(row, mapping, ReportTableColumnKind.ModifiedDate);
      if (TryParseDate(modifiedDate, out DateTime modified))
      {
        topic.ModifiedDate = modified.ToUniversalTime();
        topic.ModifiedDateSpecified = true;
      }

      string indexRaw = GetMapped(row, mapping, ReportTableColumnKind.Index);
      if (int.TryParse(indexRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
          || int.TryParse(indexRaw, NumberStyles.Integer, CultureInfo.CurrentCulture, out index))
      {
        topic.Index = index;
        topic.IndexSpecified = true;
      }

      string guidRaw = GetMapped(row, mapping, ReportTableColumnKind.Guid);
      if (!string.IsNullOrWhiteSpace(guidRaw) && Guid.TryParse(guidRaw.Trim(), out Guid guid))
        topic.Guid = guid.ToString().ToLowerInvariant();

      BcfIssueHelper.FillDropdownsFromGlobals(topic);
    }

    private static string GetMapped(
      IReadOnlyList<string> row,
      ExcelImportMapping mapping,
      ReportTableColumnKind kind)
    {
      int? index = mapping.GetColumn(kind);
      if (!index.HasValue || row == null || index.Value < 0 || index.Value >= row.Count)
        return null;
      return row[index.Value];
    }

    private static bool TryParseDate(string raw, out DateTime value)
    {
      value = default;
      if (string.IsNullOrWhiteSpace(raw))
        return false;

      if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out value))
        return true;
      if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out value))
        return true;
      if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double oa)
          && oa > 20000 && oa < 60000)
      {
        try
        {
          value = DateTime.FromOADate(oa);
          return true;
        }
        catch
        {
          return false;
        }
      }

      return false;
    }
  }
}
