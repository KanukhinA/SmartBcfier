using System;
using System.Collections.Generic;
using System.Linq;
using Bcfier.CustomFields;

namespace Bcfier.ReportTable
{
  /// <summary>Пара "подпись — значение" в шапке протокола.</summary>
  public sealed class ReportDocumentField
  {
    public ReportDocumentField(string label, string value)
    {
      Label = label ?? string.Empty;
      Value = value ?? string.Empty;
    }

    public string Label { get; }

    public string Value { get; }
  }

  /// <summary>Блок шапки: необязательный заголовок ("Участники:") и таблица полей.</summary>
  public sealed class ReportDocumentBlock
  {
    public ReportDocumentBlock(string heading, List<ReportDocumentField> fields)
    {
      Heading = heading ?? string.Empty;
      Fields = fields ?? new List<ReportDocumentField>();
    }

    public string Heading { get; }

    public List<ReportDocumentField> Fields { get; }
  }

  /// <summary>Готовит шапку протокола из полей уровня отчёта.</summary>
  public static class ReportDocumentLayout
  {
    /// <summary>
    /// Группирует поля по Section, сохраняя порядок: поля без секции идут первым блоком
    /// без заголовка, дальше — блоки в порядке первого появления секции.
    /// </summary>
    public static List<ReportDocumentBlock> BuildBlocks(IEnumerable<CustomFieldValue> fields)
    {
      var order = new List<string>();
      var grouped = new Dictionary<string, List<ReportDocumentField>>(StringComparer.OrdinalIgnoreCase);

      foreach (CustomFieldValue field in fields ?? Enumerable.Empty<CustomFieldValue>())
      {
        if (field == null)
          continue;

        string label = field.DisplayName;
        if (string.IsNullOrWhiteSpace(label) && string.IsNullOrWhiteSpace(field.Value))
          continue;

        string section = (field.Section ?? string.Empty).Trim();
        if (!grouped.TryGetValue(section, out List<ReportDocumentField> list))
        {
          list = new List<ReportDocumentField>();
          grouped[section] = list;
          order.Add(section);
        }

        list.Add(new ReportDocumentField(label, field.Value));
      }

      return order
        .Select(section => new ReportDocumentBlock(section, grouped[section]))
        .Where(block => block.Fields.Count > 0)
        .ToList();
    }
  }

  /// <summary>Всё, что нужно экспортёрам сверх самих строк таблицы.</summary>
  public sealed class ReportExportContext
  {
    public ReportExportContext(
      string reportName,
      ReportDocumentSettings document = null,
      IEnumerable<CustomFieldValue> reportFields = null,
      IList<ReportTableUserGroup> groups = null)
    {
      ReportName = reportName ?? string.Empty;
      Document = document ?? new ReportDocumentSettings();
      Blocks = Document.ShowFieldBlocks
        ? ReportDocumentLayout.BuildBlocks(reportFields)
        : new List<ReportDocumentBlock>();
      Groups = groups;
    }

    public string ReportName { get; }

    public ReportDocumentSettings Document { get; }

    public List<ReportDocumentBlock> Blocks { get; }

    public IList<ReportTableUserGroup> Groups { get; }

    public string Title => Document.ResolveTitle(ReportName);

    public string Subtitle => Document.Subtitle ?? string.Empty;

    public bool ShowRowNumbers => Document.ShowRowNumbers;
  }
}
