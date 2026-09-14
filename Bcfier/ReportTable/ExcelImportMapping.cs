using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Bcfier.ReportTable
{
  /// <summary>Сопоставление полей BCF Topic с индексами колонок Excel (0-based).</summary>
  public sealed class ExcelImportMapping
  {
    public string SheetName { get; set; }

    public bool HasHeaderRow { get; set; } = true;

    /// <summary>Kind → индекс колонки; null = не задано.</summary>
    public Dictionary<ReportTableColumnKind, int?> ColumnByKind { get; } =
      new Dictionary<ReportTableColumnKind, int?>();

    public static readonly ReportTableColumnKind[] ImportableKinds =
    {
      ReportTableColumnKind.Title,
      ReportTableColumnKind.Description,
      ReportTableColumnKind.TopicStatus,
      ReportTableColumnKind.TopicType,
      ReportTableColumnKind.Priority,
      ReportTableColumnKind.AssignedTo,
      ReportTableColumnKind.Stage,
      ReportTableColumnKind.Labels,
      ReportTableColumnKind.DueDate,
      ReportTableColumnKind.CreationAuthor,
      ReportTableColumnKind.CreationDate,
      ReportTableColumnKind.ModifiedAuthor,
      ReportTableColumnKind.ModifiedDate,
      ReportTableColumnKind.Index,
      ReportTableColumnKind.Guid
    };

    public int? GetColumn(ReportTableColumnKind kind)
    {
      return ColumnByKind.TryGetValue(kind, out int? index) ? index : null;
    }

    public void SetColumn(ReportTableColumnKind kind, int? index)
    {
      ColumnByKind[kind] = index;
    }

    /// <summary>Автосопоставление по заголовкам Excel и именам BCF-колонок.</summary>
    public void AutoMap(IReadOnlyList<string> excelHeaders, IList<ReportTableColumnConfig> savedColumns)
    {
      ColumnByKind.Clear();
      if (excelHeaders == null || excelHeaders.Count == 0)
        return;

      var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      for (int i = 0; i < excelHeaders.Count; i++)
      {
        string h = excelHeaders[i]?.Trim();
        if (string.IsNullOrWhiteSpace(h) || headerIndex.ContainsKey(h))
          continue;
        headerIndex[h] = i;
      }

      foreach (ReportTableColumnKind kind in ImportableKinds)
      {
        int? found = null;

        ReportTableColumnConfig saved = savedColumns?.FirstOrDefault(c => c.Kind == kind);
        if (saved != null && !string.IsNullOrWhiteSpace(saved.DisplayName)
            && headerIndex.TryGetValue(saved.DisplayName.Trim(), out int byCustom))
        {
          found = byCustom;
        }

        if (!found.HasValue)
        {
          string defaultHeader = ReportTableColumnConfig.GetDefaultHeader(kind);
          if (!string.IsNullOrWhiteSpace(defaultHeader)
              && headerIndex.TryGetValue(defaultHeader, out int byDefault))
          {
            found = byDefault;
          }
        }

        if (!found.HasValue && headerIndex.TryGetValue(kind.ToString(), out int byKind))
          found = byKind;

        ColumnByKind[kind] = found;
      }
    }
  }

  /// <summary>Строка UI маппинга: поле BCF → выбранная колонка Excel.</summary>
  public sealed class ExcelImportFieldMapItem : INotifyPropertyChanged
  {
    private int? _selectedColumnIndex;

    public event PropertyChangedEventHandler PropertyChanged;

    public ReportTableColumnKind Kind { get; set; }

    public string FieldName { get; set; }

    public bool IsRequired { get; set; }

    public int? SelectedColumnIndex
    {
      get => _selectedColumnIndex;
      set
      {
        if (_selectedColumnIndex == value)
          return;
        _selectedColumnIndex = value;
        OnPropertyChanged();
      }
    }

    private void OnPropertyChanged([CallerMemberName] string name = null)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
  }

  /// <summary>Вариант колонки Excel в ComboBox маппинга.</summary>
  public sealed class ExcelImportColumnOption
  {
    public int? Index { get; set; }

    public string Display { get; set; }

    public override string ToString() => Display ?? string.Empty;
  }
}
