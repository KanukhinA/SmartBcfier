using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;

namespace Bcfier.ReportTable
{
  /// <summary>Строит строки таблицы из открытого BCF-файла.</summary>
  public static class ReportTableBuilder
  {
    /// <summary>
    /// Строит строки по текущему представлению замечаний (с учётом фильтра View, если есть).
    /// </summary>
    public static List<ReportTableRow> BuildRows(BcfFile file)
    {
      var rows = new List<ReportTableRow>();
      if (file == null)
        return rows;

      IEnumerable<Markup> issues;
      if (file.View != null)
        issues = file.View.Cast<Markup>();
      else
        issues = file.Issues ?? Enumerable.Empty<Markup>();

      int number = 1;
      foreach (Markup issue in issues)
      {
        if (issue?.Topic == null)
          continue;

        rows.Add(BuildRow(issue, number++));
      }

      return rows;
    }

    private static ReportTableRow BuildRow(Markup issue, int rowNumber)
    {
      Topic topic = issue.Topic;
      string snapshot = issue.Viewpoints != null && issue.Viewpoints.Count > 0
        ? issue.Viewpoints[0]?.SnapshotPath
        : null;

      var row = new ReportTableRow
      {
        RowNumber = rowNumber,
        Issue = issue,
        SnapshotPath = snapshot,
        Description = topic.Description ?? string.Empty
      };

      SyncRowTexts(row);
      return row;
    }

    /// <summary>
    /// Обновляет текстовый снимок строки из Topic (после inline-правок, перед экспортом).
    /// </summary>
    public static void SyncRowTexts(ReportTableRow row)
    {
      if (row?.Issue?.Topic == null)
        return;

      Topic topic = row.Issue.Topic;
      Markup issue = row.Issue;

      row.Description = topic.Description ?? string.Empty;
      row.SetText(ReportTableColumnKind.Title, topic.Title);
      row.SetText(ReportTableColumnKind.Description, topic.Description);
      row.SetText(ReportTableColumnKind.TopicStatus, topic.TopicStatus);
      row.SetText(ReportTableColumnKind.TopicType, topic.TopicType);
      row.SetText(ReportTableColumnKind.Priority, topic.Priority);
      row.SetText(ReportTableColumnKind.AssignedTo, topic.AssignedTo);
      row.SetText(ReportTableColumnKind.Stage, topic.Stage);
      row.SetText(ReportTableColumnKind.Labels, FormatLabels(topic));
      row.SetText(ReportTableColumnKind.DueDate, FormatDueDate(topic));
      row.SetText(ReportTableColumnKind.CreationAuthor, topic.CreationAuthor);
      row.SetText(ReportTableColumnKind.CreationDate, FormatDate(topic.CreationDate));
      row.SetText(ReportTableColumnKind.ModifiedAuthor, topic.ModifiedAuthor);
      row.SetText(
        ReportTableColumnKind.ModifiedDate,
        topic.ModifiedDateSpecified ? FormatDate(topic.ModifiedDate) : string.Empty);
      row.SetText(
        ReportTableColumnKind.Index,
        topic.IndexSpecified ? topic.Index.ToString(CultureInfo.InvariantCulture) : string.Empty);
      row.SetText(ReportTableColumnKind.Guid, topic.Guid);
      row.SetText(ReportTableColumnKind.Comments, FormatComments(issue));
      row.SetText(ReportTableColumnKind.Snapshot, string.Empty);
      row.SetText(ReportTableColumnKind.DescriptionAndSnapshot, topic.Description);
      row.SetText(ReportTableColumnKind.TitleAndSnapshot, topic.Title);
    }

    private static string FormatLabels(Topic topic)
    {
      if (topic.SelectedLabels != null && topic.SelectedLabels.Count > 0)
        return string.Join(", ", topic.SelectedLabels.Where(x => !string.IsNullOrWhiteSpace(x)));

      if (topic.Labels != null && topic.Labels.Length > 0)
        return string.Join(", ", topic.Labels.Where(x => !string.IsNullOrWhiteSpace(x)));

      return string.Empty;
    }

    private static string FormatDueDate(Topic topic)
    {
      if (!topic.DueDateSpecified)
        return string.Empty;
      return FormatDate(topic.DueDate);
    }

    private static string FormatDate(DateTime value)
    {
      if (value == default)
        return string.Empty;
      return value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    private static string FormatComments(Markup issue)
    {
      if (issue.Comment == null || issue.Comment.Count == 0)
        return string.Empty;

      var sb = new StringBuilder();
      foreach (Comment comment in issue.Comment)
      {
        if (comment == null || comment.IsDeleted)
          continue;
        if (string.IsNullOrWhiteSpace(comment.Comment1))
          continue;

        if (sb.Length > 0)
          sb.AppendLine();

        string author = string.IsNullOrWhiteSpace(comment.Author) ? "?" : comment.Author.Trim();
        sb.Append(author);
        sb.Append(": ");
        sb.Append(comment.Comment1.Trim());
      }

      return sb.ToString();
    }
  }
}
