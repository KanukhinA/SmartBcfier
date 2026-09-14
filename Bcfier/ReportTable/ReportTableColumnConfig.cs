using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Bcfier.Localization;

namespace Bcfier.ReportTable
{
  /// <summary>Настройка одной колонки таблицы: видимость, порядок, заголовок, выравнивание.</summary>
  public sealed class ReportTableColumnConfig : INotifyPropertyChanged
  {
    private bool _visible = true;
    private string _displayName;
    private int _order;
    private ReportTableTextAlign _textAlign = ReportTableTextAlign.Left;
    private string _id;
    private ReportTableCommentScope _commentScope = ReportTableCommentScope.All;
    private string _commentUser;
    private string _commentGroupId;

    public event PropertyChangedEventHandler PropertyChanged;

    public ReportTableColumnKind Kind { get; set; }

    /// <summary>Стабильный id (нужен для нескольких колонок Comments).</summary>
    public string Id
    {
      get => _id;
      set
      {
        if (string.Equals(_id, value, System.StringComparison.Ordinal))
          return;
        _id = value;
        OnPropertyChanged();
      }
    }

    public bool Visible
    {
      get => _visible;
      set
      {
        if (_visible == value)
          return;
        _visible = value;
        OnPropertyChanged();
      }
    }

    /// <summary>Кастомный заголовок; пусто — локализованное имя по умолчанию.</summary>
    public string DisplayName
    {
      get => _displayName;
      set
      {
        if (_displayName == value)
          return;
        _displayName = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(EffectiveHeader));
      }
    }

    public int Order
    {
      get => _order;
      set
      {
        if (_order == value)
          return;
        _order = value;
        OnPropertyChanged();
      }
    }

    /// <summary>Горизонтальное выравнивание текста в колонке.</summary>
    public ReportTableTextAlign TextAlign
    {
      get => _textAlign;
      set
      {
        if (_textAlign == value)
          return;
        _textAlign = value;
        OnPropertyChanged();
      }
    }

    public ReportTableCommentScope CommentScope
    {
      get => _commentScope;
      set
      {
        if (_commentScope == value)
          return;
        _commentScope = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(EffectiveHeader));
        OnPropertyChanged(nameof(IsCommentUserScope));
        OnPropertyChanged(nameof(IsCommentGroupScope));
        OnPropertyChanged(nameof(IsCommentColumn));
      }
    }

    public string CommentUser
    {
      get => _commentUser;
      set
      {
        string next = value ?? string.Empty;
        if (string.Equals(_commentUser, next, System.StringComparison.Ordinal))
          return;
        _commentUser = next;
        OnPropertyChanged();
        OnPropertyChanged(nameof(EffectiveHeader));
      }
    }

    public string CommentGroupId
    {
      get => _commentGroupId;
      set
      {
        string next = value ?? string.Empty;
        if (string.Equals(_commentGroupId, next, System.StringComparison.Ordinal))
          return;
        _commentGroupId = next;
        OnPropertyChanged();
        OnPropertyChanged(nameof(EffectiveHeader));
      }
    }

    public bool IsCommentColumn => Kind == ReportTableColumnKind.Comments;

    public bool IsCommentUserScope =>
      Kind == ReportTableColumnKind.Comments && CommentScope == ReportTableCommentScope.User;

    public bool IsCommentGroupScope =>
      Kind == ReportTableColumnKind.Comments && CommentScope == ReportTableCommentScope.Group;

    /// <summary>Заголовок для UI и экспорта.</summary>
    public string EffectiveHeader
    {
      get
      {
        if (!string.IsNullOrWhiteSpace(_displayName))
          return _displayName.Trim();

        if (Kind == ReportTableColumnKind.Comments)
        {
          if (CommentScope == ReportTableCommentScope.User
              && !string.IsNullOrWhiteSpace(CommentUser))
            return CommentUser.Trim();

          if (CommentScope == ReportTableCommentScope.Group)
          {
            ReportTableUserGroup group = ReportTableUserGroups.Find(
              ReportTableUserGroups.Load(),
              CommentGroupId);
            if (group != null && !string.IsNullOrWhiteSpace(group.Name))
              return group.Name.Trim();
            return Loc.TableCommentGroupColumn;
          }

          return Loc.GeneralComments;
        }

        return GetDefaultHeader(Kind);
      }
    }

    /// <summary>Локализованное имя поля (для подсказки в окне настройки).</summary>
    public string DefaultHeader
    {
      get
      {
        if (Kind == ReportTableColumnKind.Comments)
        {
          switch (CommentScope)
          {
            case ReportTableCommentScope.User:
              return Loc.TableCommentUserColumn;
            case ReportTableCommentScope.Group:
              return Loc.TableCommentGroupColumn;
            default:
              return Loc.GeneralComments;
          }
        }

        return GetDefaultHeader(Kind);
      }
    }

    public TextAlignment ToTextAlignment()
    {
      switch (_textAlign)
      {
        case ReportTableTextAlign.Center:
          return TextAlignment.Center;
        case ReportTableTextAlign.Right:
          return TextAlignment.Right;
        default:
          return TextAlignment.Left;
      }
    }

    public HorizontalAlignment ToHorizontalAlignment()
    {
      switch (_textAlign)
      {
        case ReportTableTextAlign.Center:
          return HorizontalAlignment.Center;
        case ReportTableTextAlign.Right:
          return HorizontalAlignment.Right;
        default:
          return HorizontalAlignment.Left;
      }
    }

    public void EnsureId()
    {
      if (string.IsNullOrWhiteSpace(Id))
        Id = System.Guid.NewGuid().ToString("N");
    }

    public static string GetDefaultHeader(ReportTableColumnKind kind)
    {
      switch (kind)
      {
        case ReportTableColumnKind.Title:
          return Loc.IssueTitle;
        case ReportTableColumnKind.Description:
          return Loc.Description;
        case ReportTableColumnKind.TopicStatus:
          return Loc.Status;
        case ReportTableColumnKind.TopicType:
          return Loc.ColumnType;
        case ReportTableColumnKind.Priority:
          return Loc.Priority;
        case ReportTableColumnKind.AssignedTo:
          return Loc.AssignedTo;
        case ReportTableColumnKind.Stage:
          return Loc.TableColStage;
        case ReportTableColumnKind.Labels:
          return Loc.Labels;
        case ReportTableColumnKind.DueDate:
          return Loc.DueDate;
        case ReportTableColumnKind.CreationAuthor:
          return Loc.CreationAuthor;
        case ReportTableColumnKind.CreationDate:
          return Loc.TableColCreationDate;
        case ReportTableColumnKind.ModifiedAuthor:
          return Loc.TableColModifiedAuthor;
        case ReportTableColumnKind.ModifiedDate:
          return Loc.TableColModifiedDate;
        case ReportTableColumnKind.Index:
          return Loc.TableColIndex;
        case ReportTableColumnKind.Guid:
          return Loc.TableColGuid;
        case ReportTableColumnKind.Snapshot:
          return Loc.GroupSnapshot;
        case ReportTableColumnKind.DescriptionAndSnapshot:
          return Loc.TableColDescriptionAndSnapshot;
        case ReportTableColumnKind.TitleAndSnapshot:
          return Loc.TableColTitleAndSnapshot;
        case ReportTableColumnKind.Comments:
          return Loc.GeneralComments;
        default:
          return kind.ToString();
      }
    }

    private void OnPropertyChanged([CallerMemberName] string name = null)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
  }
}
