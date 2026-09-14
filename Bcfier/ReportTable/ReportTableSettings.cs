using System;
using System.Collections.Generic;
using System.Linq;
using Bcfier.Data.Utils;
using Newtonsoft.Json;

namespace Bcfier.ReportTable
{
  /// <summary>Persist конфигурации колонок табличного режима.</summary>
  public static class ReportTableSettings
  {
    public const string ColumnsKey = "ReportTableColumns";
    public const string DisplayModeKey = "UiDisplayMode";
    public const string ModeClassic = "Classic";
    public const string ModeTable = "Table";

    private static readonly ReportTableColumnKind[] DefaultOrder =
    {
      ReportTableColumnKind.Title,
      ReportTableColumnKind.DescriptionAndSnapshot,
      ReportTableColumnKind.TitleAndSnapshot,
      ReportTableColumnKind.TopicStatus,
      ReportTableColumnKind.TopicType,
      ReportTableColumnKind.Priority,
      ReportTableColumnKind.AssignedTo,
      ReportTableColumnKind.DueDate,
      ReportTableColumnKind.CreationAuthor,
      ReportTableColumnKind.CreationDate,
      ReportTableColumnKind.Labels,
      ReportTableColumnKind.Stage,
      ReportTableColumnKind.ModifiedAuthor,
      ReportTableColumnKind.ModifiedDate,
      ReportTableColumnKind.Index,
      ReportTableColumnKind.Guid,
      ReportTableColumnKind.Snapshot,
      ReportTableColumnKind.Description,
      ReportTableColumnKind.Comments
    };

    private static readonly HashSet<ReportTableColumnKind> DefaultVisible =
      new HashSet<ReportTableColumnKind>
      {
        ReportTableColumnKind.Title,
        ReportTableColumnKind.DescriptionAndSnapshot,
        ReportTableColumnKind.TopicStatus,
        ReportTableColumnKind.TopicType,
        ReportTableColumnKind.Priority,
        ReportTableColumnKind.AssignedTo,
        ReportTableColumnKind.DueDate,
        ReportTableColumnKind.CreationAuthor
      };

    /// <summary>True, если сохранён табличный режим.</summary>
    public static bool IsTableMode()
    {
      return string.Equals(UserSettings.Get(DisplayModeKey), ModeTable, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetDisplayMode(bool tableMode)
    {
      UserSettings.Set(DisplayModeKey, tableMode ? ModeTable : ModeClassic);
    }

    /// <summary>Загружает конфиг колонок или создаёт значения по умолчанию.</summary>
    public static List<ReportTableColumnConfig> LoadColumns()
    {
      try
      {
        string json = UserSettings.Get(ColumnsKey);
        if (!string.IsNullOrWhiteSpace(json))
        {
          var stored = JsonConvert.DeserializeObject<List<ColumnDto>>(json);
          if (stored != null && stored.Count > 0)
            return MergeWithDefaults(stored);
        }
      }
      catch
      {
        // битый JSON — дефолт
      }

      return CreateDefaults();
    }

    public static void SaveColumns(IEnumerable<ReportTableColumnConfig> columns)
    {
      if (columns == null)
        return;

      var dto = columns
        .OrderBy(c => c.Order)
        .Select(c =>
        {
          c.EnsureId();
          return new ColumnDto
          {
            Id = c.Id,
            Kind = c.Kind.ToString(),
            Visible = c.Visible,
            DisplayName = c.DisplayName ?? string.Empty,
            Order = c.Order,
            TextAlign = c.TextAlign.ToString(),
            CommentScope = c.CommentScope.ToString(),
            CommentUser = c.CommentUser ?? string.Empty,
            CommentGroupId = c.CommentGroupId ?? string.Empty
          };
        })
        .ToList();

      UserSettings.Set(ColumnsKey, JsonConvert.SerializeObject(dto));
    }

    private static List<ReportTableColumnConfig> CreateDefaults()
    {
      var list = new List<ReportTableColumnConfig>();
      for (int i = 0; i < DefaultOrder.Length; i++)
      {
        var kind = DefaultOrder[i];
        var config = new ReportTableColumnConfig
        {
          Kind = kind,
          Order = i,
          Visible = DefaultVisible.Contains(kind),
          DisplayName = string.Empty,
          TextAlign = ReportTableTextAlign.Left,
          CommentScope = ReportTableCommentScope.All
        };
        config.EnsureId();
        list.Add(config);
      }

      return list;
    }

    private static List<ReportTableColumnConfig> MergeWithDefaults(List<ColumnDto> stored)
    {
      var nonCommentStored = new Dictionary<ReportTableColumnKind, ColumnDto>();
      var commentStored = new List<ColumnDto>();

      foreach (ColumnDto item in stored)
      {
        if (!Enum.TryParse(item.Kind, true, out ReportTableColumnKind kind))
          continue;

        if (kind == ReportTableColumnKind.Comments)
          commentStored.Add(item);
        else
          nonCommentStored[kind] = item;
      }

      var result = new List<ReportTableColumnConfig>();
      int order = 0;

      foreach (ReportTableColumnKind kind in DefaultOrder)
      {
        if (kind == ReportTableColumnKind.Comments)
          continue;

        if (nonCommentStored.TryGetValue(kind, out ColumnDto dto))
        {
          result.Add(FromDto(kind, dto, order++));
          nonCommentStored.Remove(kind);
        }
        else
        {
          var config = new ReportTableColumnConfig
          {
            Kind = kind,
            Visible = DefaultVisible.Contains(kind),
            DisplayName = string.Empty,
            Order = order++,
            TextAlign = ReportTableTextAlign.Left,
            CommentScope = ReportTableCommentScope.All
          };
          config.EnsureId();
          result.Add(config);
        }
      }

      if (commentStored.Count == 0)
      {
        var allComments = new ReportTableColumnConfig
        {
          Kind = ReportTableColumnKind.Comments,
          Visible = false,
          DisplayName = string.Empty,
          Order = order++,
          TextAlign = ReportTableTextAlign.Left,
          CommentScope = ReportTableCommentScope.All
        };
        allComments.EnsureId();
        result.Add(allComments);
      }
      else
      {
        foreach (ColumnDto dto in commentStored.OrderBy(x => x.Order))
          result.Add(FromDto(ReportTableColumnKind.Comments, dto, order++));
      }

      foreach (ColumnDto orphan in nonCommentStored.Values.OrderBy(x => x.Order))
      {
        if (!Enum.TryParse(orphan.Kind, true, out ReportTableColumnKind kind))
          continue;
        if (kind == ReportTableColumnKind.Comments)
          continue;
        result.Add(FromDto(kind, orphan, order++));
      }

      return result;
    }

    private static ReportTableColumnConfig FromDto(ReportTableColumnKind kind, ColumnDto dto, int order)
    {
      ReportTableTextAlign align = ReportTableTextAlign.Left;
      if (!string.IsNullOrWhiteSpace(dto.TextAlign))
        Enum.TryParse(dto.TextAlign, true, out align);

      ReportTableCommentScope scope = ReportTableCommentScope.All;
      if (!string.IsNullOrWhiteSpace(dto.CommentScope))
        Enum.TryParse(dto.CommentScope, true, out scope);

      var config = new ReportTableColumnConfig
      {
        Id = string.IsNullOrWhiteSpace(dto.Id) ? null : dto.Id,
        Kind = kind,
        Visible = dto.Visible,
        DisplayName = dto.DisplayName ?? string.Empty,
        Order = order,
        TextAlign = align,
        CommentScope = kind == ReportTableColumnKind.Comments ? scope : ReportTableCommentScope.All,
        CommentUser = dto.CommentUser ?? string.Empty,
        CommentGroupId = dto.CommentGroupId ?? string.Empty
      };
      config.EnsureId();
      return config;
    }

    private sealed class ColumnDto
    {
      public string Id { get; set; }
      public string Kind { get; set; }
      public bool Visible { get; set; }
      public string DisplayName { get; set; }
      public int Order { get; set; }
      public string TextAlign { get; set; }
      public string CommentScope { get; set; }
      public string CommentUser { get; set; }
      public string CommentGroupId { get; set; }
    }
  }
}
