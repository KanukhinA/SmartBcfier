using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Bcfier.ReportTable;

namespace Bcfier.UserControls
{
  /// <summary>Ячейка колонки комментариев: список по авторам, правка своих, добавление для участника.</summary>
  public partial class TableCommentCell : UserControl
  {
    public static readonly DependencyProperty ColumnConfigProperty =
      DependencyProperty.Register(
        nameof(ColumnConfig),
        typeof(ReportTableColumnConfig),
        typeof(TableCommentCell),
        new PropertyMetadata(null, OnColumnConfigChanged));

    public static readonly DependencyProperty GroupsProperty =
      DependencyProperty.Register(
        nameof(Groups),
        typeof(IList<ReportTableUserGroup>),
        typeof(TableCommentCell),
        new PropertyMetadata(null, OnColumnConfigChanged));

    public static readonly RoutedEvent CommentsChangedEvent =
      EventManager.RegisterRoutedEvent(
        nameof(CommentsChanged),
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(TableCommentCell));

    public event RoutedEventHandler CommentsChanged
    {
      add => AddHandler(CommentsChangedEvent, value);
      remove => RemoveHandler(CommentsChangedEvent, value);
    }

    public TableCommentCell()
    {
      InitializeComponent();
      DataContextChanged += (_, __) => Refresh();
    }

    public ReportTableColumnConfig ColumnConfig
    {
      get => (ReportTableColumnConfig)GetValue(ColumnConfigProperty);
      set => SetValue(ColumnConfigProperty, value);
    }

    public IList<ReportTableUserGroup> Groups
    {
      get => (IList<ReportTableUserGroup>)GetValue(GroupsProperty);
      set => SetValue(GroupsProperty, value);
    }

    private static void OnColumnConfigChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      (d as TableCommentCell)?.Refresh();
    }

    public void Refresh()
    {
      var row = DataContext as ReportTableRow;
      ReportTableColumnConfig config = ColumnConfig;
      if (CommentsList == null || AddPanel == null)
        return;

      if (row?.Issue == null || config == null)
      {
        CommentsList.ItemsSource = null;
        AddPanel.Visibility = Visibility.Collapsed;
        return;
      }

      List<Comment> items = ReportTableCommentHelper
        .FilterComments(row.Issue, config, Groups)
        .ToList();
      CommentsList.ItemsSource = items;

      bool canAdd = ReportTableCommentHelper.CanCurrentUserContribute(config, Groups);
      AddPanel.Visibility = canAdd ? Visibility.Visible : Visibility.Collapsed;
      if (AddBox != null && !canAdd)
        AddBox.Text = string.Empty;
    }

    private void OwnComment_LostFocus(object sender, RoutedEventArgs e)
    {
      if (sender is not TextBox box)
        return;
      if (box.DataContext is not Comment comment || !comment.CanModify)
        return;

      string text = box.Text?.Trim() ?? string.Empty;
      if (string.IsNullOrWhiteSpace(text))
        return;

      comment.ApplyEdit(text, BcfAuthorContext.ResolveAuthor());
      RaiseChanged();
    }

    private void AddBox_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
      {
        e.Handled = true;
        CommitNewComment();
      }
    }

    private void AddComment_Click(object sender, RoutedEventArgs e)
    {
      CommitNewComment();
    }

    private void CommitNewComment()
    {
      var row = DataContext as ReportTableRow;
      if (row?.Issue == null || ColumnConfig == null)
        return;
      if (!ReportTableCommentHelper.CanCurrentUserContribute(ColumnConfig, Groups))
        return;

      string text = AddBox?.Text?.Trim();
      if (string.IsNullOrWhiteSpace(text))
        return;

      if (row.Issue.Comment == null)
        row.Issue.Comment = new ObservableCollection<Comment>();

      row.Issue.Comment.Add(ReportTableCommentHelper.CreateComment(text));
      if (AddBox != null)
        AddBox.Text = string.Empty;

      Refresh();
      RaiseChanged();
    }

    private void RaiseChanged()
    {
      RaiseEvent(new RoutedEventArgs(CommentsChangedEvent, this));
    }
  }
}
