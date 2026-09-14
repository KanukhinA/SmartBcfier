using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Bcfier.UserControls;

namespace Bcfier.Themes
{
  public partial class ViewCommentDataTemplate : ResourceDictionary
  {
    public ViewCommentDataTemplate()
    {
      InitializeComponent();
    }

    /// <summary>
    /// Enter sends; Shift+Enter inserts a new line.
    /// </summary>
    private void Compose_PreviewKeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key != Key.Enter || Keyboard.Modifiers == ModifierKeys.Shift)
        return;

      e.Handled = true;
      if (!(sender is TextBox textBox))
        return;

      var button = FindVisualChildByName(FindAncestor<Grid>(textBox), "AddCommButton") as ICommandSource;
      if (button?.Command == null)
        return;

      var parameter = button.CommandParameter;
      if (button.Command.CanExecute(parameter))
        button.Command.Execute(parameter);
    }

    private void EditComment_OnClick(object sender, RoutedEventArgs e)
    {
      var comment = TryGetComment(sender as DependencyObject);
      if (comment == null || !comment.CanModify)
        return;
      comment.BeginEdit();
    }

    private void DeleteCommentMenu_OnClick(object sender, RoutedEventArgs e)
    {
      var comment = TryGetComment(sender as DependencyObject);
      if (comment == null || !comment.CanModify)
        return;

      var target = FindAncestorElement<BcfierPanel>(GetPlacementTarget(sender as DependencyObject))
                   ?? FindAncestorElement<BcfierPanel>(sender as DependencyObject);
      var issue = FindIssueFromVisual(GetPlacementTarget(sender as DependencyObject) ?? sender as DependencyObject);
      var parameter = new object[] { comment, issue };

      if (target != null && Commands.DeleteComments.CanExecute(parameter, target))
        Commands.DeleteComments.Execute(parameter, target);
      else
        comment.MarkDeleted(BcfAuthorContext.ResolveAuthor());
    }

    private void SaveEdit_OnClick(object sender, RoutedEventArgs e)
    {
      CommitInlineEdit(sender as DependencyObject);
    }

    private void EditComment_PreviewKeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Escape)
      {
        e.Handled = true;
        var comment = TryGetComment(sender as DependencyObject);
        if (comment != null)
          comment.CancelEdit();
        return;
      }

      if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
      {
        e.Handled = true;
        CommitInlineEdit(sender as DependencyObject);
      }
    }

    private void CommitInlineEdit(DependencyObject source)
    {
      var comment = TryGetComment(source);
      if (comment == null || !comment.CanModify)
        return;

      var textBox = source as TextBox
                    ?? FindVisualChildByName(FindAncestor<DockPanel>(source), "EditCommentBox") as TextBox;
      if (textBox == null || string.IsNullOrWhiteSpace(textBox.Text))
        return;

      var target = FindAncestorElement<BcfierPanel>(source);
      var parameter = new object[] { comment, textBox };
      if (target != null && Commands.EditComment.CanExecute(parameter, target))
        Commands.EditComment.Execute(parameter, target);
      else
        comment.ApplyEdit(textBox.Text.Trim(), BcfAuthorContext.ResolveAuthor());
    }

    private void CopyComment_OnClick(object sender, RoutedEventArgs e)
    {
      try
      {
        var text = TryGetCommentText(sender as DependencyObject);
        if (!string.IsNullOrEmpty(text))
          Clipboard.SetText(text);
      }
      catch
      {
        // Clipboard may be locked by another process
      }
    }

    private static DependencyObject GetPlacementTarget(DependencyObject source)
    {
      if (source is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
        return contextMenu.PlacementTarget;
      return source;
    }

    private static string TryGetCommentText(DependencyObject source)
    {
      var comment = TryGetComment(source);
      if (comment != null && !string.IsNullOrEmpty(comment.Comment1))
        return comment.Comment1;

      if (source is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu
          && contextMenu.PlacementTarget is FrameworkElement target)
      {
        if (target is TextBox textBox && !string.IsNullOrEmpty(textBox.Text))
          return textBox.Text;
        if (target.DataContext is Comment c && !string.IsNullOrEmpty(c.Comment1))
          return c.Comment1;
        if (target.Tag is Comment tagged && !string.IsNullOrEmpty(tagged.Comment1))
          return tagged.Comment1;
      }

      return null;
    }

    private static Comment TryGetComment(DependencyObject source)
    {
      if (source == null)
        return null;

      if (source is FrameworkElement fe)
      {
        if (fe.DataContext is Comment fromDc)
          return fromDc;
        if (fe.Tag is Comment fromTag)
          return fromTag;
      }

      if (source is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
      {
        if (contextMenu.DataContext is Comment fromMenu)
          return fromMenu;
        if (contextMenu.PlacementTarget is FrameworkElement target)
        {
          if (target.Tag is Comment tagged)
            return tagged;
          if (target.DataContext is Comment fromTarget)
            return fromTarget;
        }
      }

      return null;
    }

    private static Markup FindIssueFromVisual(DependencyObject source)
    {
      var current = source;
      while (current != null)
      {
        if (current is FrameworkElement element)
        {
          object found = null;
          try { found = element.FindName("IssueList"); } catch { /* not a namescope owner */ }
          if (found is Selector list && list.SelectedItem is Markup markup)
            return markup;
        }
        current = VisualTreeHelper.GetParent(current)
                  ?? (current as FrameworkElement)?.Parent as DependencyObject;
      }
      return null;
    }

    private static T FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
      var current = start;
      while (current != null)
      {
        if (current is T match)
          return match;
        current = VisualTreeHelper.GetParent(current);
      }
      return null;
    }

    private static T FindAncestorElement<T>(DependencyObject start) where T : DependencyObject
    {
      var current = start;
      while (current != null)
      {
        if (current is T match)
          return match;
        current = VisualTreeHelper.GetParent(current)
                  ?? (current as FrameworkElement)?.Parent as DependencyObject;
      }
      return null;
    }

    private static FrameworkElement FindVisualChildByName(DependencyObject parent, string name)
    {
      if (parent == null)
        return null;
      int count = VisualTreeHelper.GetChildrenCount(parent);
      for (int i = 0; i < count; i++)
      {
        var child = VisualTreeHelper.GetChild(parent, i);
        if (child is FrameworkElement fe && fe.Name == name)
          return fe;
        var nested = FindVisualChildByName(child, name);
        if (nested != null)
          return nested;
      }
      return null;
    }
  }
}
