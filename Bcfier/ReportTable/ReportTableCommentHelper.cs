using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;

namespace Bcfier.ReportTable
{
  /// <summary>Фильтрация и форматирование комментариев для колонок таблицы.</summary>
  public static class ReportTableCommentHelper
  {
    public static IReadOnlyList<string> GetAuthors(
      ReportTableColumnConfig config,
      IEnumerable<ReportTableUserGroup> groups)
    {
      if (config == null || config.Kind != ReportTableColumnKind.Comments)
        return Array.Empty<string>();

      switch (config.CommentScope)
      {
        case ReportTableCommentScope.User:
          return string.IsNullOrWhiteSpace(config.CommentUser)
            ? Array.Empty<string>()
            : new[] { config.CommentUser.Trim() };
        case ReportTableCommentScope.Group:
          ReportTableUserGroup group = ReportTableUserGroups.Find(groups, config.CommentGroupId);
          if (group?.Members == null || group.Members.Count == 0)
            return Array.Empty<string>();
          return group.Members
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        default:
          return null; // All — без фильтра
      }
    }

    public static bool CanCurrentUserContribute(
      ReportTableColumnConfig config,
      IEnumerable<ReportTableUserGroup> groups)
    {
      if (config == null || config.Kind != ReportTableColumnKind.Comments)
        return false;
      if (config.CommentScope == ReportTableCommentScope.All)
        return false;

      string current = BcfAuthorContext.ResolveAuthor();
      if (string.IsNullOrWhiteSpace(current))
        return false;

      IReadOnlyList<string> authors = GetAuthors(config, groups);
      if (authors == null || authors.Count == 0)
        return false;

      return authors.Any(a => string.Equals(a, current, StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<Comment> FilterComments(
      Markup issue,
      ReportTableColumnConfig config,
      IEnumerable<ReportTableUserGroup> groups)
    {
      if (issue?.Comment == null)
        yield break;

      IReadOnlyList<string> authors = GetAuthors(config, groups);
      foreach (Comment comment in issue.Comment)
      {
        if (comment == null || comment.IsDeleted)
          continue;
        if (string.IsNullOrWhiteSpace(comment.Comment1) && !comment.CanModify)
          continue;

        if (authors != null
            && !authors.Any(a =>
              string.Equals(a, comment.Author?.Trim(), StringComparison.OrdinalIgnoreCase)))
          continue;

        yield return comment;
      }
    }

    public static string FormatForColumn(
      Markup issue,
      ReportTableColumnConfig config,
      IEnumerable<ReportTableUserGroup> groups)
    {
      var sb = new StringBuilder();
      foreach (Comment comment in FilterComments(issue, config, groups))
      {
        if (string.IsNullOrWhiteSpace(comment.Comment1))
          continue;
        if (sb.Length > 0)
          sb.AppendLine();

        string author = string.IsNullOrWhiteSpace(comment.Author) ? "?" : comment.Author.Trim();
        sb.Append(author);
        sb.Append(": ");
        sb.Append(comment.Comment1.Trim());
      }

      return sb.ToString();
    }

    public static Comment CreateComment(string text)
    {
      return new Comment
      {
        Guid = Guid.NewGuid().ToString(),
        Comment1 = (text ?? string.Empty).Trim(),
        Date = DateTime.Now,
        Author = BcfAuthorContext.ResolveAuthor(),
        Viewpoint = new CommentViewpoint { Guid = string.Empty }
      };
    }
  }
}
