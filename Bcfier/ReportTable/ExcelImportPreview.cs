using System;
using System.Collections.Generic;

namespace Bcfier.ReportTable
{
  /// <summary>Снимок листа Excel для предпросмотра и маппинга колонок.</summary>
  public sealed class ExcelImportPreview
  {
    public IReadOnlyList<string> SheetNames { get; set; } = Array.Empty<string>();

    public string SheetName { get; set; } = string.Empty;

    public bool HasHeaderRow { get; set; } = true;

    /// <summary>Заголовки колонок (или «Столбец N»).</summary>
    public IReadOnlyList<string> Headers { get; set; } = Array.Empty<string>();

    /// <summary>Строки предпросмотра; каждая — массив ячеек по индексу колонки.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; set; } = Array.Empty<IReadOnlyList<string>>();

    public string ErrorMessage { get; set; } = string.Empty;

    public bool HasData =>
      Headers != null
      && Headers.Count > 0
      && string.IsNullOrWhiteSpace(ErrorMessage);

    public int ColumnCount => Headers?.Count ?? 0;
  }

  /// <summary>Строка предпросмотра с индексатором для динамических DataGrid-колонок.</summary>
  public sealed class ExcelImportPreviewRow
  {
    private readonly IReadOnlyList<string> _cells;

    public ExcelImportPreviewRow(IReadOnlyList<string> cells)
    {
      _cells = cells ?? Array.Empty<string>();
    }

    public string this[int index]
    {
      get
      {
        if (index < 0 || index >= _cells.Count)
          return string.Empty;
        return _cells[index] ?? string.Empty;
      }
    }
  }
}
