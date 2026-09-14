using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Bcfier.Bcf.Bcf2;

namespace Bcfier.CustomFields
{
  /// <summary>Чтение/запись пользовательских полей проекта в Documents BCF.</summary>
  public static class CustomFieldsXmlStore
  {
    public const string FileName = "custom-fields.xml";
    public const string DocumentsFolder = "Documents";

    public static string GetDocumentsPath(string tempPath)
    {
      return Path.Combine(tempPath ?? string.Empty, DocumentsFolder);
    }

    public static string GetCanonicalPath(string tempPath)
    {
      return Path.Combine(GetDocumentsPath(tempPath), FileName);
    }

    /// <summary>
    /// Сканирует Documents/*.xml: канон и плоский ключ-значение → поля проекта.
    /// Устаревшие Topic-поля поднимаются на уровень проекта.
    /// </summary>
    public static CustomFieldsLoadResult LoadFromDocuments(string tempPath)
    {
      var result = new CustomFieldsLoadResult();
      string docs = GetDocumentsPath(tempPath);
      if (!Directory.Exists(docs))
        return result;

      foreach (string path in Directory.GetFiles(docs, "*.xml", SearchOption.TopDirectoryOnly))
      {
        try
        {
          XDocument doc = XDocument.Load(path);
          XElement root = doc.Root;
          if (root == null)
            continue;

          if (TryParseCanonical(root, result))
            continue;

          if (TryParseFlat(root, out List<CustomFieldValue> flat) && flat.Count > 0)
          {
            foreach (CustomFieldValue field in flat)
              AddUnique(result.ReportLevel, field);
          }
        }
        catch (Exception ex)
        {
          result.Warnings.Add(Path.GetFileName(path) + ": " + ex.Message);
        }
      }

      return result;
    }

    /// <summary>Пишет custom-fields.xml с полями проекта; пустой набор — удаляет файл.</summary>
    public static void SaveCanonical(string tempPath, IEnumerable<CustomFieldValue> projectFields)
    {
      string docs = GetDocumentsPath(tempPath);
      string path = GetCanonicalPath(tempPath);

      var reportEl = new XElement("Report");
      foreach (CustomFieldValue field in projectFields ?? Enumerable.Empty<CustomFieldValue>())
      {
        if (field == null || string.IsNullOrWhiteSpace(field.Name) && string.IsNullOrWhiteSpace(field.Id))
          continue;

        reportEl.Add(new XElement(
          "Field",
          new XAttribute("Id", field.Id ?? string.Empty),
          new XAttribute("Name", field.Name ?? string.Empty),
          field.Value ?? string.Empty));
      }

      if (!reportEl.HasElements)
      {
        if (File.Exists(path))
          File.Delete(path);
        return;
      }

      if (!Directory.Exists(docs))
        Directory.CreateDirectory(docs);

      var root = new XElement("CustomFields", reportEl);
      new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(path);
    }

    /// <summary>Очищает устаревшие поля на замечаниях (поля теперь только у проекта).</summary>
    public static void ClearIssueFields(IEnumerable<Markup> issues)
    {
      if (issues == null)
        return;

      foreach (Markup issue in issues)
      {
        if (issue?.CustomFields == null)
          continue;
        issue.CustomFields.Clear();
      }
    }

    private static bool TryParseCanonical(XElement root, CustomFieldsLoadResult result)
    {
      if (!string.Equals(root.Name.LocalName, "CustomFields", StringComparison.OrdinalIgnoreCase))
        return false;

      bool any = false;
      foreach (XElement reportEl in root.Elements().Where(e =>
                 string.Equals(e.Name.LocalName, "Report", StringComparison.OrdinalIgnoreCase)))
      {
        foreach (CustomFieldValue field in ReadFields(reportEl))
        {
          AddUnique(result.ReportLevel, field);
          any = true;
        }
      }

      // Legacy: Topic → проект (без дубликатов Id)
      foreach (XElement topicEl in root.Elements().Where(e =>
                 string.Equals(e.Name.LocalName, "Topic", StringComparison.OrdinalIgnoreCase)))
      {
        foreach (CustomFieldValue field in ReadFields(topicEl))
        {
          AddUnique(result.ReportLevel, field);
          any = true;
        }
      }

      // Поля напрямую под корнем (без Report/Topic)
      foreach (XElement fieldEl in root.Elements().Where(e =>
                 string.Equals(e.Name.LocalName, "Field", StringComparison.OrdinalIgnoreCase)))
      {
        CustomFieldValue field = ReadField(fieldEl);
        if (field == null)
          continue;
        AddUnique(result.ReportLevel, field);
        any = true;
      }

      return any;
    }

    private static IEnumerable<CustomFieldValue> ReadFields(XElement parent)
    {
      foreach (XElement fieldEl in parent.Elements().Where(e =>
                 string.Equals(e.Name.LocalName, "Field", StringComparison.OrdinalIgnoreCase)))
      {
        CustomFieldValue field = ReadField(fieldEl);
        if (field != null)
          yield return field;
      }
    }

    private static CustomFieldValue ReadField(XElement fieldEl)
    {
      string id = ((string)fieldEl.Attribute("Id") ?? (string)fieldEl.Attribute("id") ?? string.Empty).Trim();
      string name = ((string)fieldEl.Attribute("Name") ?? (string)fieldEl.Attribute("name") ?? string.Empty).Trim();
      string value = (fieldEl.Value ?? string.Empty).Trim();
      if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(id))
        return null;

      return new CustomFieldValue
      {
        Id = id,
        Name = string.IsNullOrEmpty(name) ? id : name,
        Value = value
      };
    }

    private static bool TryParseFlat(XElement root, out List<CustomFieldValue> fields)
    {
      fields = new List<CustomFieldValue>();
      var children = root.Elements().ToList();
      if (children.Count == 0)
        return false;

      foreach (XElement child in children)
      {
        if (child.HasElements)
          return false;

        string name = child.Name.LocalName;
        if (string.IsNullOrWhiteSpace(name))
          return false;

        fields.Add(new CustomFieldValue
        {
          Id = name,
          Name = name,
          Value = (child.Value ?? string.Empty).Trim()
        });
      }

      return fields.Count > 0;
    }

    private static void AddUnique(ObservableCollection<CustomFieldValue> target, CustomFieldValue field)
    {
      if (field == null)
        return;

      string key = !string.IsNullOrWhiteSpace(field.Id) ? field.Id : field.Name;
      if (string.IsNullOrWhiteSpace(key))
        return;

      bool exists = target.Any(f =>
        string.Equals(
          !string.IsNullOrWhiteSpace(f.Id) ? f.Id : f.Name,
          key,
          StringComparison.OrdinalIgnoreCase));
      if (!exists)
        target.Add(field);
    }
  }

  /// <summary>Результат сканирования Documents XML (поля проекта).</summary>
  public sealed class CustomFieldsLoadResult
  {
    public ObservableCollection<CustomFieldValue> ReportLevel { get; } =
      new ObservableCollection<CustomFieldValue>();

    public List<string> Warnings { get; } = new List<string>();

    public bool HasAny => ReportLevel.Count > 0;
  }
}
