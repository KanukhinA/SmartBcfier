namespace Bcfier.ReportTable
{
  /// <summary>Область колонки комментариев в табличном режиме.</summary>
  public enum ReportTableCommentScope
  {
    /// <summary>Все комментарии, только чтение.</summary>
    All = 0,
    /// <summary>Комментарии одного пользователя.</summary>
    User = 1,
    /// <summary>Комментарии членов группы.</summary>
    Group = 2
  }
}
