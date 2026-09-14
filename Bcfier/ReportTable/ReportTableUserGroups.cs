using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Bcfier.Data.Utils;
using Newtonsoft.Json;

namespace Bcfier.ReportTable
{
  /// <summary>Группа пользователей для колонок комментариев в табличном режиме.</summary>
  public sealed class ReportTableUserGroup : INotifyPropertyChanged
  {
    private string _name = string.Empty;
    private List<string> _members = new List<string>();

    public event PropertyChangedEventHandler PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

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
      }
    }

    public List<string> Members
    {
      get => _members;
      set
      {
        _members = value ?? new List<string>();
        OnPropertyChanged();
      }
    }

    private void OnPropertyChanged([CallerMemberName] string name = null)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
  }

  /// <summary>Persist групп пользователей табличного режима.</summary>
  public static class ReportTableUserGroups
  {
    public const string SettingsKey = "ReportTableUserGroups";

    public static List<ReportTableUserGroup> Load()
    {
      try
      {
        string json = UserSettings.Get(SettingsKey);
        if (string.IsNullOrWhiteSpace(json))
          return new List<ReportTableUserGroup>();

        var list = JsonConvert.DeserializeObject<List<ReportTableUserGroup>>(json);
        if (list == null)
          return new List<ReportTableUserGroup>();

        foreach (ReportTableUserGroup group in list)
        {
          if (string.IsNullOrWhiteSpace(group.Id))
            group.Id = Guid.NewGuid().ToString("N");
          if (group.Members == null)
            group.Members = new List<string>();
          group.Members = group.Members
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        }

        return list;
      }
      catch
      {
        return new List<ReportTableUserGroup>();
      }
    }

    public static void Save(IEnumerable<ReportTableUserGroup> groups)
    {
      var list = (groups ?? Enumerable.Empty<ReportTableUserGroup>())
        .Where(g => g != null)
        .Select(g => new ReportTableUserGroup
        {
          Id = string.IsNullOrWhiteSpace(g.Id) ? Guid.NewGuid().ToString("N") : g.Id,
          Name = (g.Name ?? string.Empty).Trim(),
          Members = (g.Members ?? new List<string>())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
        })
        .ToList();

      UserSettings.Set(SettingsKey, JsonConvert.SerializeObject(list));
    }

    public static ReportTableUserGroup Find(IEnumerable<ReportTableUserGroup> groups, string groupId)
    {
      if (string.IsNullOrWhiteSpace(groupId) || groups == null)
        return null;
      return groups.FirstOrDefault(g =>
        g != null && string.Equals(g.Id, groupId, StringComparison.OrdinalIgnoreCase));
    }
  }
}
