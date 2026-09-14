using System;
using System.Collections.Generic;
using System.IO;
using Bcfier.Bcf.Bcf2;

namespace Bcfier.ReportTable
{
  /// <summary>Строка табличного режима: одно замечание BCF.</summary>
  public sealed class ReportTableRow
  {
    public int RowNumber { get; set; }

    /// <summary>Исходное замечание (для команд добавления вида/снимка).</summary>
    public Markup Issue { get; set; }

    public string SnapshotPath { get; set; }

    public string Description { get; set; }

    /// <summary>True, если есть существующий файл снимка.</summary>
    public bool HasSnapshot =>
      !string.IsNullOrWhiteSpace(SnapshotPath) && File.Exists(SnapshotPath);

    /// <summary>Текстовые значения по имени Kind (строковый ключ для WPF Binding).</summary>
    public Dictionary<string, string> Values { get; } =
      new Dictionary<string, string>(StringComparer.Ordinal);

    public string GetText(ReportTableColumnKind kind)
    {
      return Values.TryGetValue(kind.ToString(), out string value) ? value ?? string.Empty : string.Empty;
    }

    public void SetText(ReportTableColumnKind kind, string value)
    {
      Values[kind.ToString()] = value ?? string.Empty;
    }
  }
}
