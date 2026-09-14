using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data.Utils;
using Bcfier.Localization;

namespace Bcfier.Bcf
{
  /// <summary>
  /// Фиксация правок существующего viewpoint: сравнение снимка/элементов и автокомментарий.
  /// </summary>
  public static class ViewpointEditAudit
  {
    /// <summary>
    /// Собирает набор Id связанных элементов viewpoint.
    /// </summary>
    public static HashSet<string> CollectElementIds(ViewPoint viewpoint)
    {
      var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      if (viewpoint?.VisInfo?.Components == null)
        return ids;

      foreach (Component component in BcfViewpointComponents.EnumerateViewpointComponents(viewpoint))
      {
        string id = component?.AuthoringToolId?.Trim();
        if (!string.IsNullOrEmpty(id))
          ids.Add(id);
      }

      return ids;
    }

    /// <summary>
    /// Собирает Id из выбранных в диалоге компонентов.
    /// </summary>
    public static HashSet<string> CollectElementIds(IEnumerable<Component> components)
    {
      var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      if (components == null)
        return ids;

      foreach (Component component in components)
      {
        string id = component?.AuthoringToolId?.Trim();
        if (!string.IsNullOrEmpty(id))
          ids.Add(id);
      }

      return ids;
    }

    /// <summary>
    /// Хэш файла снимка для сравнения до/после правки.
    /// </summary>
    public static string ComputeSnapshotHash(string snapshotPath)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
          return string.Empty;

        using (var stream = new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var sha = SHA256.Create())
        {
          byte[] hash = sha.ComputeHash(stream);
          return Convert.ToBase64String(hash);
        }
      }
      catch
      {
        return string.Empty;
      }
    }

    /// <summary>
    /// true, если наборы Id элементов отличаются.
    /// </summary>
    public static bool ElementsChanged(HashSet<string> before, HashSet<string> after)
    {
      before = before ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      after = after ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      return !before.SetEquals(after);
    }

    /// <summary>
    /// Добавляет системный комментарий о правке снимка и/или элементов.
    /// </summary>
    public static void AppendChangeComment(
      Markup issue,
      ViewPoint viewpoint,
      bool snapshotChanged,
      bool elementsChanged)
    {
      if (issue == null || viewpoint == null || (!snapshotChanged && !elementsChanged))
        return;

      try
      {
        if (issue.Comment == null)
          issue.Comment = new System.Collections.ObjectModel.ObservableCollection<Comment>();

        string author = Utils.GetUsername();
        string text;
        if (snapshotChanged && elementsChanged)
          text = Loc.Format("ViewEditSnapshotAndElementsComment", author);
        else if (snapshotChanged)
          text = Loc.Format("ViewEditSnapshotComment", author);
        else
          text = Loc.Format("ViewEditElementsComment", author);

        issue.Comment.Add(new Comment
        {
          Comment1 = text,
          Author = author,
          Date = DateTime.Now,
          Viewpoint = new CommentViewpoint { Guid = viewpoint.Guid }
        });

        issue.RegisterEvents();
      }
      catch (Exception ex)
      {
        ExceptionUi.Show(ex);
      }
    }
  }
}
