using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Bcfier.Data.Utils;
using Newtonsoft.Json;

namespace Bcfier.CustomFields
{
  /// <summary>Persist каталога определений пользовательских полей.</summary>
  public static class CustomFieldDefinitionsStore
  {
    public const string SettingsKey = "CustomFieldDefinitions";

    public static List<CustomFieldDefinition> Load()
    {
      try
      {
        string json = UserSettings.Get(SettingsKey);
        if (string.IsNullOrWhiteSpace(json))
          return new List<CustomFieldDefinition>();

        var list = JsonConvert.DeserializeObject<List<CustomFieldDefinition>>(json);
        if (list == null)
          return new List<CustomFieldDefinition>();

        foreach (CustomFieldDefinition item in list)
        {
          if (string.IsNullOrWhiteSpace(item.Id))
            item.Id = Guid.NewGuid().ToString("N");
          item.Name = (item.Name ?? string.Empty).Trim();
        }

        return list
          .Where(d => !string.IsNullOrWhiteSpace(d.Name))
          .ToList();
      }
      catch
      {
        return new List<CustomFieldDefinition>();
      }
    }

    public static void Save(IEnumerable<CustomFieldDefinition> definitions)
    {
      var list = (definitions ?? Enumerable.Empty<CustomFieldDefinition>())
        .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Name))
        .Select(d => new CustomFieldDefinition
        {
          Id = string.IsNullOrWhiteSpace(d.Id) ? Guid.NewGuid().ToString("N") : d.Id,
          Name = d.Name.Trim(),
          FieldType = d.FieldType
        })
        .ToList();

      UserSettings.Set(SettingsKey, JsonConvert.SerializeObject(list));
    }
  }

  /// <summary>Значение пользовательского поля на Topic/отчёте.</summary>
  public sealed class CustomFieldValue : INotifyPropertyChanged
  {
    private string _name = string.Empty;
    private string _value = string.Empty;
    private string _section = string.Empty;

    public event PropertyChangedEventHandler PropertyChanged;

    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Заголовок блока в экспортируемом протоколе ("Участники:"); пусто — блок без заголовка
    /// сразу под названием документа.
    /// </summary>
    public string Section
    {
      get => _section;
      set
      {
        string next = value ?? string.Empty;
        if (string.Equals(_section, next, StringComparison.Ordinal))
          return;
        _section = next;
        OnPropertyChanged();
      }
    }

    public string Name
    {
      get => _name;
      set
      {
        string next = value ?? string.Empty;
        if (string.Equals(_name, next, StringComparison.Ordinal))
          return;
        _name = next;
        OnPropertyChanged();
        OnPropertyChanged(nameof(DisplayName));
      }
    }

    public string Value
    {
      get => _value;
      set
      {
        string next = value ?? string.Empty;
        if (string.Equals(_value, next, StringComparison.Ordinal))
          return;
        _value = next;
        OnPropertyChanged();
      }
    }

    /// <summary>Имя для UI: из каталога по Id, иначе Name из XML.</summary>
    public string DisplayName
    {
      get
      {
        if (!string.IsNullOrWhiteSpace(Id))
        {
          CustomFieldDefinition def = CustomFieldDefinitionsStore.Load()
            .FirstOrDefault(d => string.Equals(d.Id, Id, StringComparison.OrdinalIgnoreCase));
          if (def != null && !string.IsNullOrWhiteSpace(def.Name))
            return def.Name;
        }

        return string.IsNullOrWhiteSpace(Name) ? Id : Name;
      }
    }

    public void RefreshDisplayName()
    {
      OnPropertyChanged(nameof(DisplayName));
    }

    private void OnPropertyChanged([CallerMemberName] string name = null)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
  }
}
