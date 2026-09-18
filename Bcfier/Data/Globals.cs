using System;
using System.Collections.Generic;
using System.Linq;
using Bcfier.Data.Utils;

namespace Bcfier.Data
{
    /// <summary>
    /// Глобальные списки для UI: из настроек + значения, встреченные в открытых BCF.
    /// Статусы, типы, приоритеты и метки хранятся как имя + цвет.
    /// </summary>
    public static class Globals
    {
        public static List<string> OpenStatuses = new List<string>();
        public static List<string> OpenTypes = new List<string>();
        public static List<string> OpenPriorities = new List<string>();
        public static List<string> OpenLabels = new List<string>();
        public static List<string> OpenAssignees = new List<string>();

        private static List<TopicStatusEntry> statusEntries = new List<TopicStatusEntry>();
        private static List<TopicStatusEntry> typeEntries = new List<TopicStatusEntry>();
        private static List<TopicStatusEntry> priorityEntries = new List<TopicStatusEntry>();
        private static List<TopicStatusEntry> labelEntries = new List<TopicStatusEntry>();
        private static IEnumerable<string> availAssignees = new List<string>();

        public static IReadOnlyList<TopicStatusEntry> StatusEntries => statusEntries;
        public static IReadOnlyList<TopicStatusEntry> TypeEntries => typeEntries;
        public static IReadOnlyList<TopicStatusEntry> PriorityEntries => priorityEntries;
        public static IReadOnlyList<TopicStatusEntry> LabelEntries => labelEntries;

        public static IEnumerable<string> AvailStatuses =>
            NamesOf(statusEntries).Union(OpenStatuses);

        public static IEnumerable<string> AvailTypes =>
            NamesOf(typeEntries).Union(OpenTypes);

        public static IEnumerable<string> AvailPriorities =>
            NamesOf(priorityEntries).Union(OpenPriorities);

        public static IEnumerable<string> AvailLabels =>
            NamesOf(labelEntries).Union(OpenLabels);

        public static IEnumerable<string> AvailAssignees => availAssignees.Union(OpenAssignees);

        public static void SetStatuses(string statusString) =>
            statusEntries = TopicStatusListCodec.Parse(statusString);

        public static void SetTypes(string typesString) =>
            typeEntries = TopicStatusListCodec.Parse(typesString);

        public static void SetPriorities(string prioritiesString) =>
            priorityEntries = TopicStatusListCodec.Parse(prioritiesString);

        public static void SetLabels(string labelsString) =>
            labelEntries = TopicStatusListCodec.Parse(labelsString);

        public static void SetStatusEntries(IEnumerable<TopicStatusEntry> entries) =>
            statusEntries = CloneList(entries);

        public static void SetTypeEntries(IEnumerable<TopicStatusEntry> entries) =>
            typeEntries = CloneList(entries);

        public static void SetPriorityEntries(IEnumerable<TopicStatusEntry> entries) =>
            priorityEntries = CloneList(entries);

        public static void SetLabelEntries(IEnumerable<TopicStatusEntry> entries) =>
            labelEntries = CloneList(entries);

        public static string GetStatusColor(string name) => GetColor(statusEntries, name);
        public static string GetTypeColor(string name) => GetColor(typeEntries, name);
        public static string GetPriorityColor(string name) => GetColor(priorityEntries, name);
        public static string GetLabelColor(string name) => GetColor(labelEntries, name);

        /// <summary>Цвет по виду списка: status (по умолчанию), type, priority, label.</summary>
        public static string GetListColor(string listKind, string name)
        {
            if (string.Equals(listKind, "type", StringComparison.OrdinalIgnoreCase))
                return GetTypeColor(name);
            if (string.Equals(listKind, "priority", StringComparison.OrdinalIgnoreCase))
                return GetPriorityColor(name);
            if (string.Equals(listKind, "label", StringComparison.OrdinalIgnoreCase))
                return GetLabelColor(name);
            return GetStatusColor(name);
        }

        public static void SetAssignees(string assigneesString) =>
            availAssignees = SplitCsv(assigneesString);

        public static void LoadFromUserSettings(string language = null)
        {
            SetStatuses(UserSettings.GetLanguageBound("Stauses", language));
            SetTypes(UserSettings.GetLanguageBound("Types", language));
            SetPriorities(UserSettings.GetLanguageBound("Priorities", language));
            SetLabels(UserSettings.GetLanguageBound("Labels", language));
            SetAssignees(UserSettings.GetLanguageBound("Assignees", language));
            EnsureDefaults(language);
        }

        public static void EnsureDefaults(string language = null)
        {
            string lang = UserSettings.NormalizeLanguage(language ?? UserSettings.Get("Language"));

            if (statusEntries == null || statusEntries.Count == 0)
                SetStatuses(UserSettings.GetDefaultTopicList("Stauses", lang));
            if (typeEntries == null || typeEntries.Count == 0)
                SetTypes(UserSettings.GetDefaultTopicList("Types", lang));
            if (priorityEntries == null || priorityEntries.Count == 0)
                SetPriorities(UserSettings.GetDefaultTopicList("Priorities", lang));
            if (labelEntries == null || labelEntries.Count == 0)
                SetLabels(UserSettings.GetDefaultTopicList("Labels", lang));
            if (!availAssignees.Any())
                SetAssignees(UserSettings.GetDefaultTopicList("Assignees", lang));
        }

        private static IEnumerable<string> NamesOf(IEnumerable<TopicStatusEntry> entries) =>
            entries == null
                ? Enumerable.Empty<string>()
                : entries.Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n));

        private static List<TopicStatusEntry> CloneList(IEnumerable<TopicStatusEntry> entries) =>
            entries?
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Name))
                .Select(e => e.Clone())
                .ToList()
            ?? new List<TopicStatusEntry>();

        private static string GetColor(List<TopicStatusEntry> entries, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || entries == null)
                return TopicStatusListCodec.FallbackColor;

            var match = entries.FirstOrDefault(s =>
                string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            return match != null
                ? TopicStatusListCodec.NormalizeColor(match.Color)
                : TopicStatusListCodec.FallbackColor;
        }

        private static IEnumerable<string> SplitCsv(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Enumerable.Empty<string>();

            return value
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(o => o.Trim())
                .Where(o => o.Length > 0);
        }
    }
}
