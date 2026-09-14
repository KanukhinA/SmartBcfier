using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Bcfier.Data;
using Bcfier.Localization;
using Bcfier.ReportTable;
using Bcfier.Themes;

namespace Bcfier.Windows
{
  /// <summary>Окно колонок таблицы, групп пользователей и колонок комментариев.</summary>
  public partial class ReportTableColumnsWindow : Window, INotifyPropertyChanged
  {
    private readonly ObservableCollection<ReportTableColumnConfig> _items;
    private readonly ObservableCollection<ReportTableUserGroup> _groups;
    private readonly ObservableCollection<string> _authorCandidates;
    private readonly ObservableCollection<MemberToggle> _memberToggles =
      new ObservableCollection<MemberToggle>();
    private bool _suppressGroupUi;

    public event PropertyChangedEventHandler PropertyChanged;

    public List<ReportTableColumnConfig> ResultColumns { get; private set; }

    public List<ReportTableUserGroup> ResultGroups { get; private set; }

    public ObservableCollection<ReportTableUserGroup> Groups => _groups;

    public ObservableCollection<string> AuthorCandidates => _authorCandidates;

    public ReportTableColumnsWindow(
      IEnumerable<ReportTableColumnConfig> columns,
      IEnumerable<string> authorCandidates = null)
    {
      Loc.ApplyCultureFromSettings();
      InitializeComponent();
      DataContext = this;

      _items = new ObservableCollection<ReportTableColumnConfig>(
        (columns ?? ReportTableSettings.LoadColumns())
          .OrderBy(c => c.Order)
          .Select(Clone));

      _groups = new ObservableCollection<ReportTableUserGroup>(
        ReportTableUserGroups.Load().Select(CloneGroup));

      _authorCandidates = new ObservableCollection<string>(
        BuildAuthorCandidates(authorCandidates, _groups));

      ColumnsList.ItemsSource = _items;
      GroupsList.ItemsSource = _groups;
      MembersList.ItemsSource = _memberToggles;

      if (_items.Count > 0)
        ColumnsList.SelectedIndex = 0;
      if (_groups.Count > 0)
        GroupsList.SelectedIndex = 0;
      else
        SyncGroupEditor(null);
    }

    private static List<string> BuildAuthorCandidates(
      IEnumerable<string> extra,
      IEnumerable<ReportTableUserGroup> groups)
    {
      var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

      void Add(string value)
      {
        if (!string.IsNullOrWhiteSpace(value))
          set.Add(value.Trim());
      }

      foreach (string a in Globals.AvailAssignees ?? Enumerable.Empty<string>())
        Add(a);
      foreach (string a in extra ?? Enumerable.Empty<string>())
        Add(a);
      foreach (ReportTableUserGroup g in groups ?? Enumerable.Empty<ReportTableUserGroup>())
      {
        foreach (string m in g.Members ?? Enumerable.Empty<string>())
          Add(m);
      }

      Add(BcfAuthorContext.ResolveAuthor());
      return set.ToList();
    }

    private static ReportTableColumnConfig Clone(ReportTableColumnConfig source)
    {
      var clone = new ReportTableColumnConfig
      {
        Id = source.Id,
        Kind = source.Kind,
        Visible = source.Visible,
        DisplayName = source.DisplayName ?? string.Empty,
        Order = source.Order,
        TextAlign = source.TextAlign,
        CommentScope = source.CommentScope,
        CommentUser = source.CommentUser ?? string.Empty,
        CommentGroupId = source.CommentGroupId ?? string.Empty
      };
      clone.EnsureId();
      return clone;
    }

    private static ReportTableUserGroup CloneGroup(ReportTableUserGroup source)
    {
      return new ReportTableUserGroup
      {
        Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id,
        Name = source.Name ?? string.Empty,
        Members = (source.Members ?? new List<string>()).ToList()
      };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
      ApplyMembersToSelectedGroup();

      for (int i = 0; i < _items.Count; i++)
        _items[i].Order = i;

      ResultColumns = _items.ToList();
      ResultGroups = _groups.ToList();
      ReportTableUserGroups.Save(ResultGroups);
      DialogResult = true;
      Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
      DialogResult = false;
      Close();
    }

    private void HeaderClose_Click(object sender, RoutedEventArgs e)
    {
      DialogResult = false;
      Close();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
      int index = ColumnsList.SelectedIndex;
      if (index <= 0)
        return;
      _items.Move(index, index - 1);
      ColumnsList.SelectedIndex = index - 1;
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
      int index = ColumnsList.SelectedIndex;
      if (index < 0 || index >= _items.Count - 1)
        return;
      _items.Move(index, index + 1);
      ColumnsList.SelectedIndex = index + 1;
    }

    /// <summary>Клонирует строку комментариев (кнопка "+" на самой строке).</summary>
    private void DuplicateCommentColumn_Click(object sender, RoutedEventArgs e)
    {
      if ((sender as FrameworkElement)?.DataContext is not ReportTableColumnConfig source)
        return;
      if (source.Kind != ReportTableColumnKind.Comments)
        return;

      ReportTableColumnConfig config = Clone(source);
      config.Id = null;
      config.EnsureId();

      int index = _items.IndexOf(source);
      _items.Insert(index + 1, config);
      ColumnsList.SelectedItem = config;
      ColumnsList.ScrollIntoView(config);
    }

    /// <summary>Удаляет строку комментариев (кнопка "×" на самой строке); хотя бы одна остаётся всегда.</summary>
    private void RemoveCommentColumn_Click(object sender, RoutedEventArgs e)
    {
      if ((sender as FrameworkElement)?.DataContext is not ReportTableColumnConfig config)
        return;
      if (config.Kind != ReportTableColumnKind.Comments)
        return;

      int commentCount = _items.Count(c => c.Kind == ReportTableColumnKind.Comments);
      if (commentCount <= 1)
        return;

      _items.Remove(config);
    }

    private void CommentScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      // Обновляет visibility биндингов IsCommentUserScope / IsCommentGroupScope
      if ((sender as FrameworkElement)?.DataContext is ReportTableColumnConfig config)
      {
        config.CommentScope = config.CommentScope;
      }
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
      var group = new ReportTableUserGroup
      {
        Name = Loc.TableCommentGroupsTitle + " " + (_groups.Count + 1)
      };
      _groups.Add(group);
      GroupsList.SelectedItem = group;
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
      if (GroupsList.SelectedItem is not ReportTableUserGroup group)
        return;

      string id = group.Id;
      _groups.Remove(group);

      foreach (ReportTableColumnConfig col in _items.Where(c =>
                   c.Kind == ReportTableColumnKind.Comments
                   && c.CommentScope == ReportTableCommentScope.Group
                   && string.Equals(c.CommentGroupId, id, StringComparison.OrdinalIgnoreCase)))
      {
        col.CommentGroupId = _groups.FirstOrDefault()?.Id ?? string.Empty;
      }

      if (_groups.Count > 0)
        GroupsList.SelectedIndex = 0;
      else
        SyncGroupEditor(null);
    }

    private void GroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      ApplyMembersToSelectedGroup(previous: e.RemovedItems.OfType<ReportTableUserGroup>().FirstOrDefault());
      SyncGroupEditor(GroupsList.SelectedItem as ReportTableUserGroup);
    }

    private void GroupNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
      if (_suppressGroupUi)
        return;
      if (GroupsList.SelectedItem is ReportTableUserGroup group)
        group.Name = GroupNameBox.Text ?? string.Empty;
    }

    private void MemberCheck_Changed(object sender, RoutedEventArgs e)
    {
      if (_suppressGroupUi)
        return;
      ApplyMembersToSelectedGroup();
    }

    private void SyncGroupEditor(ReportTableUserGroup group)
    {
      _suppressGroupUi = true;
      try
      {
        GroupNameBox.IsEnabled = group != null;
        MembersList.IsEnabled = group != null;
        GroupNameBox.Text = group?.Name ?? string.Empty;

        _memberToggles.Clear();
        foreach (string author in _authorCandidates)
        {
          bool selected = group?.Members != null
            && group.Members.Any(m => string.Equals(m, author, StringComparison.OrdinalIgnoreCase));
          _memberToggles.Add(new MemberToggle(author, selected));
        }
      }
      finally
      {
        _suppressGroupUi = false;
      }
    }

    private void ApplyMembersToSelectedGroup(ReportTableUserGroup previous = null)
    {
      ReportTableUserGroup group = previous ?? GroupsList.SelectedItem as ReportTableUserGroup;
      if (group == null)
        return;

      group.Members = _memberToggles
        .Where(m => m.IsSelected)
        .Select(m => m.Name)
        .ToList();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      try
      {
        SpWindowChrome.Apply(this);
        SpWindowChrome.EnsureHittableBackground(this);
        ApplyChromeClip();
      }
      catch
      {
        // chrome не критичен
      }
    }

    private void Window_StateChanged(object sender, EventArgs e) => ApplyChromeClip();

    private void ChromeRoot_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyChromeClip();

    private void ApplyChromeClip()
    {
      try
      {
        double radius = SpWindowChrome.GetWindowClipRadius(this);
        SpWindowChrome.ClipToRoundedRect(ChromeRoot, radius);
      }
      catch
      {
        // ignore
      }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      if (e.ChangedButton == MouseButton.Left)
        DragMove();
    }

    private void OnPropertyChanged([CallerMemberName] string name = null)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private sealed class MemberToggle : INotifyPropertyChanged
    {
      private bool _isSelected;

      public MemberToggle(string name, bool selected)
      {
        Name = name;
        _isSelected = selected;
      }

      public string Name { get; }

      public bool IsSelected
      {
        get => _isSelected;
        set
        {
          if (_isSelected == value)
            return;
          _isSelected = value;
          PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
      }

      public event PropertyChangedEventHandler PropertyChanged;
    }
  }
}
