using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Bcfier.Data.Utils;

namespace Bcfier.Bcf
{
  /// <summary>
  /// Общие операции с компонентами viewpoint для фильтрации и resolve в Revit.
  /// </summary>
  public static class BcfViewpointComponents
  {
    /// <summary>
    /// Solibri: [Имя:Тип:3094860](uuid) — uuid необязателен.
    /// </summary>
    private static readonly Regex ElementIdHintRegex = new Regex(
      @"\[([^\[\]]*):(\d+)\](?:\(([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\))?",
      RegexOptions.CultureInvariant);

    /// <summary>
    /// Подставляет Revit Id из описания/комментариев в AuthoringToolId для любой категории.
    /// Сначала по GUID, затем оставшиеся Id по порядку упоминания в тексте.
    /// </summary>
    public static void ApplyAuthoringToolIdsFromIssueText(Markup issue)
    {
      if (issue == null)
        return;

      try
      {
        var orderedIds = new List<long>();
        Dictionary<string, long> idByGuid = ParseElementIdsFromIssueText(issue, orderedIds);
        if (idByGuid.Count == 0 && orderedIds.Count == 0)
          return;

        foreach (ViewPoint viewpoint in issue.Viewpoints ?? Enumerable.Empty<ViewPoint>())
        {
          List<Component> components = EnumerateViewpointComponents(viewpoint).ToList();
          foreach (Component component in components)
            TryAssignAuthoringToolId(component, idByGuid);

          AssignRemainingIdsInOrder(components, orderedIds);
        }
      }
      catch
      {
        // Разбор текста не должен ломать открытие BCF
      }
    }

    /// <summary>
    /// Разбирает Id элементов из описания замечания и комментариев.
    /// </summary>
    public static Dictionary<string, long> ParseElementIdsFromIssueText(Markup issue)
    {
      return ParseElementIdsFromIssueText(issue, null);
    }

    /// <summary>
    /// Разбирает Id элементов из описания и комментариев, сохраняя порядок упоминания.
    /// </summary>
    public static Dictionary<string, long> ParseElementIdsFromIssueText(Markup issue, IList<long> orderedIds)
    {
      var idByGuid = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
      if (issue == null)
        return idByGuid;

      try
      {
        AddElementIdHints(idByGuid, orderedIds, issue.Topic?.Description);
        AddElementIdHints(idByGuid, orderedIds, issue.Topic?.Title);

        if (issue.Comment == null)
          return idByGuid;

        foreach (Comment comment in issue.Comment)
          AddElementIdHints(idByGuid, orderedIds, comment?.Comment1);
      }
      catch
      {
        return idByGuid;
      }

      return idByGuid;
    }

    /// <summary>
    /// Добавляет в словарь пары GUID → ElementId из markdown Solibri.
    /// </summary>
    public static void AddElementIdHints(IDictionary<string, long> idByGuid, string text)
    {
      AddElementIdHints(idByGuid, null, text);
    }

    /// <summary>
    /// Добавляет пары GUID → ElementId и уникальные Id в порядке появления в тексте.
    /// </summary>
    public static void AddElementIdHints(IDictionary<string, long> idByGuid, IList<long> orderedIds, string text)
    {
      if (string.IsNullOrWhiteSpace(text))
        return;

      try
      {
        foreach (Match match in ElementIdHintRegex.Matches(text))
        {
          if (!match.Success)
            continue;

          if (!long.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long elementId)
              || elementId <= 0)
          {
            continue;
          }

          if (orderedIds != null && !orderedIds.Contains(elementId))
            orderedIds.Add(elementId);

          if (idByGuid == null || match.Groups.Count < 4 || !match.Groups[3].Success)
            continue;

          foreach (string key in EnumerateGuidKeys(match.Groups[3].Value))
          {
            if (!idByGuid.ContainsKey(key))
              idByGuid[key] = elementId;
          }
        }
      }
      catch
      {
        // Один кривой комментарий не должен ломать разбор остальных
      }
    }

    /// <summary>
    /// Записывает числовой Id в AuthoringToolId, если поле пустое и GUID совпал.
    /// </summary>
    private static void TryAssignAuthoringToolId(Component component, IDictionary<string, long> idByGuid)
    {
      if (component == null || idByGuid == null || idByGuid.Count == 0)
        return;

      if (!string.IsNullOrWhiteSpace(component.AuthoringToolId))
        return;

      foreach (string key in EnumerateGuidKeys(component.IfcGuid))
      {
        if (!idByGuid.TryGetValue(key, out long elementId) || elementId <= 0)
          continue;

        component.AuthoringToolId = elementId.ToString(CultureInfo.InvariantCulture);
        return;
      }
    }

    /// <summary>
    /// Назначает оставшимся компонентам Id из текста по порядку, без сверки GUID.
    /// </summary>
    private static void AssignRemainingIdsInOrder(IList<Component> components, IList<long> orderedIds)
    {
      if (components == null || orderedIds == null || orderedIds.Count == 0)
        return;

      var usedIds = new HashSet<long>();
      foreach (Component component in components)
      {
        if (TryParseNumericAuthoringToolId(component?.AuthoringToolId, out long existingId))
          usedIds.Add(existingId);
      }

      int nextIdIndex = 0;
      foreach (Component component in components)
      {
        if (component == null || !string.IsNullOrWhiteSpace(component.AuthoringToolId))
          continue;

        while (nextIdIndex < orderedIds.Count && usedIds.Contains(orderedIds[nextIdIndex]))
          nextIdIndex++;

        if (nextIdIndex >= orderedIds.Count)
          return;

        long elementId = orderedIds[nextIdIndex++];
        usedIds.Add(elementId);
        component.AuthoringToolId = elementId.ToString(CultureInfo.InvariantCulture);
      }
    }

    /// <summary>
    /// Id для выделения в модели: найденный LinkedElementId или числовой AuthoringToolId.
    /// </summary>
    public static bool TryGetSelectableElementId(Component component, out int elementId)
    {
      elementId = 0;
      if (component == null)
        return false;

      if (component.LinkedElementId > 0)
      {
        elementId = component.LinkedElementId;
        return true;
      }

      return TryParseIntAuthoringToolId(component.AuthoringToolId, out elementId);
    }

    /// <summary>
    /// Разбирает AuthoringToolId как целое ElementId.
    /// </summary>
    public static bool TryParseIntAuthoringToolId(string authoringToolId, out int elementId)
    {
      elementId = 0;
      if (!TryParseNumericAuthoringToolId(authoringToolId, out long parsed) || parsed <= 0 || parsed > int.MaxValue)
        return false;

      elementId = (int)parsed;
      return true;
    }

    /// <summary>
    /// Разбирает числовой Id: целое или хвост после последнего двоеточия.
    /// </summary>
    public static bool TryParseNumericAuthoringToolId(string authoringToolId, out long elementId)
    {
      elementId = 0;
      if (string.IsNullOrWhiteSpace(authoringToolId))
        return false;

      string trimmed = authoringToolId.Trim();
      if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out elementId) && elementId > 0)
        return true;

      int colon = trimmed.LastIndexOf(':');
      if (colon < 0 || colon >= trimmed.Length - 1)
        return false;

      return long.TryParse(
        trimmed.Substring(colon + 1).Trim(),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out elementId) && elementId > 0;
    }

    /// <summary>
    /// Ключи GUID для сопоставления: стандартный UUID и сжатый IfcGuid.
    /// </summary>
    private static IEnumerable<string> EnumerateGuidKeys(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        yield break;

      string trimmed = value.Trim().Trim('"').Trim('{', '}');
      if (string.IsNullOrWhiteSpace(trimmed))
        yield break;

      yield return trimmed;

      if (Guid.TryParse(trimmed, out Guid guid))
      {
        yield return guid.ToString("D");
        yield return guid.ToString("N");
        string compressed = null;
        try
        {
          compressed = IfcGuid.ToIfcGuid(guid);
        }
        catch
        {
          compressed = null;
        }

        if (!string.IsNullOrWhiteSpace(compressed))
          yield return compressed;
        yield break;
      }

      if (trimmed.Length != 22)
        yield break;

      if (TryFromIfcGuid(trimmed, out Guid fromIfc))
      {
        yield return fromIfc.ToString("D");
        yield return fromIfc.ToString("N");
      }
    }

    /// <summary>
    /// Декодирует сжатый IfcGuid в стандартный GUID.
    /// </summary>
    private static bool TryFromIfcGuid(string value, out Guid guid)
    {
      guid = Guid.Empty;
      try
      {
        guid = IfcGuid.FromIfcGUID(value);
        return guid != Guid.Empty;
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Возвращает все компоненты замечания из всех viewpoint без дублей.
    /// </summary>
    public static IEnumerable<Component> EnumerateIssueComponents(Markup issue)
    {
      if (issue?.Viewpoints == null)
        yield break;

      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (ViewPoint viewpoint in issue.Viewpoints)
      {
        foreach (Component component in EnumerateViewpointComponents(viewpoint))
        {
          string key = BuildComponentKey(component);
          if (seen.Add(key))
            yield return component;
        }
      }
    }

    /// <summary>
    /// Возвращает компоненты одного viewpoint: Selection, Visibility и Coloring.
    /// </summary>
    public static IEnumerable<Component> EnumerateViewpointComponents(ViewPoint viewpoint)
    {
      Components components = viewpoint?.VisInfo?.Components;
      if (components == null)
        yield break;

      foreach (Component component in components.DisplayComponents ?? Array.Empty<Component>())
      {
        if (component != null)
          yield return component;
      }

      foreach (ComponentColoringColor coloring in components.Coloring ?? Array.Empty<ComponentColoringColor>())
      {
        foreach (Component component in coloring?.Component ?? Array.Empty<Component>())
        {
          if (component != null)
            yield return component;
        }
      }
    }

    /// <summary>
    /// Проверяет, сопоставлен ли компонент с элементом активного документа.
    /// </summary>
    public static bool HasActiveDocumentLink(Component component)
    {
      if (component == null)
        return false;

      if (component.HasModelLink && component.LinkedElementId > 0)
        return true;

      var cached = ComponentListHost.TryGetCachedElementId?.Invoke(component);
      if (cached == null || !cached.Value.found || !cached.Value.elementId.HasValue)
        return false;

      // Сначала Id, потом флаг: UI покажет истинный ElementId вместо IfcGuid.
      component.LinkedElementId = cached.Value.elementId.Value;
      component.HasModelLink = true;
      return true;
    }

    /// <summary>
    /// Формирует сводку originating system по всем замечаниям отчёта.
    /// </summary>
    public static string GetOriginatingSystemsSummary(IEnumerable<Markup> issues)
    {
      if (issues == null)
        return string.Empty;

      var systems = issues
        .SelectMany(EnumerateIssueComponents)
        .Select(component => component?.OriginatingSystem?.Trim())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

      return systems.Length == 0
        ? string.Empty
        : string.Join(", ", systems);
    }

    /// <summary>
    /// Формирует ключ компонента для dedup и кэша resolve.
    /// </summary>
    public static string BuildComponentKey(Component component)
    {
      string authoringId = component?.AuthoringToolId?.Trim() ?? string.Empty;
      string ifcGuid = component?.IfcGuid?.Trim() ?? string.Empty;
      return authoringId + "|" + ifcGuid;
    }
  }
}
