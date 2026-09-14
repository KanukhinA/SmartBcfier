using System.Collections.Generic;
using System.Linq;
using Bcfier.Bcf;
using Bcfier.Localization;

namespace Bcfier.CustomFields
{
  /// <summary>Строка предпросмотра пользовательских полей при открытии BCF.</summary>
  public sealed class CustomFieldPreviewRow
  {
    public string Scope { get; set; }
    public string FieldName { get; set; }
    public string Value { get; set; }

    public static List<CustomFieldPreviewRow> BuildFrom(BcfFile file)
    {
      var rows = new List<CustomFieldPreviewRow>();
      if (file == null)
        return rows;

      foreach (CustomFieldValue field in file.ReportLevelCustomFields ?? Enumerable.Empty<CustomFieldValue>())
      {
        rows.Add(new CustomFieldPreviewRow
        {
          Scope = Loc.CustomFieldsReportLevel,
          FieldName = field.DisplayName,
          Value = field.Value ?? string.Empty
        });
      }

      return rows;
    }
  }
}
