using System;
using System.Collections.Generic;
using System.Linq;
using Bcfier.Data.Utils;

namespace Bcfier.Data
{
    /// <summary>
    /// Глобальные списки для UI: из настроек + значения, встреченные в открытых BCF.
    /// </summary>
    public static class Globals
    {
        public static List<string> OpenStatuses = new List<string>();
        public static List<string> OpenTypes = new List<string>();
        public static List<string> OpenPriorities = new List<string>();
        public static List<string> OpenLabels = new List<string>();
        public static List<string> OpenAssignees = new List<string>();

        private static List<TopicStatusEntry> statusEntries = new List<TopicStatusEntry>();
        private static IEnumerable<string> availTypes = new List<string>();
        private static IEnumerable<string> availPriorities = new List<string>();
        private static IEnumerable<string> availLabels = new List<string>();
        private static IEnumerable<string> availAssignees = new List<string>();

        /// <summary>Статусы из настроек (имя + цвет).</summary>
        public static IReadOnlyList<TopicStatusEntry> StatusEntries => statusEntries;

        // Avail* = настройки ∪ значения из Open*
        public static IEnumerable<string> AvailStatuses =>
            statusEntries.Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Union(OpenStatuses);

        public static IEnumerable<string> AvailTypes => availTypes.Union(OpenTypes);
        public static IEnumerable<string> AvailPriorities => availPriorities.Union(OpenPriorities);
        public static IEnumerable<string> AvailLabels => availLabels.Union(OpenLabels);
        public static IEnumerable<string> AvailAssignees => availAssignees.Union(OpenAssignees);

        public static void SetStatuses(string statusString)
        {
            statusEntries = TopicStatusListCodec.Parse(statusString);
        }

        public static void SetStatusEntries(IEnumerable<TopicStatusEntry> entries)
        {
            statusEntries = entries?
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Name))
                .Select(e => e.Clone())
                .ToList()
                ?? new List<TopicStatusEntry>();
        }

        /// <summary>Цвет статуса по имени (из настроек) или нейтральный fallback.</summary>
        public static string GetStatusColor(string statusName)
        {
            if (string.IsNullOrWhiteSpace(statusName) || statusEntries == null)
                return TopicStatusListCodec.FallbackColor;

            var match = statusEntries.FirstOrDefault(s =>
                string.Equals(s.Name, statusName.Trim(), StringComparison.OrdinalIgnoreCase));
            return match != null
                ? TopicStatusListCodec.NormalizeColor(match.Color)
                : TopicStatusListCodec.FallbackColor;
        }

        public static void SetTypes(string typesString) =>
            availTypes = SplitCsv(typesString);

        public static void SetPriorities(string prioritiesString) =>
            availPriorities = SplitCsv(prioritiesString);

        public static void SetLabels(string labelsString) =>
            availLabels = SplitCsv(labelsString);

        public static void SetAssignees(string assigneesString) =>
            availAssignees = SplitCsv(assigneesString);

        /// <summary>
        /// Подгружает списки из языкозависимых настроек (или дефолтов текущего языка).
        /// </summary>
        public static void LoadFromUserSettings(string language = null)
        {
            SetStatuses(UserSettings.GetLanguageBound("Stauses", language));
            SetTypes(UserSettings.GetLanguageBound("Types", language));
            SetPriorities(UserSettings.GetLanguageBound("Priorities", language));
            SetLabels(UserSettings.GetLanguageBound("Labels", language));
            SetAssignees(UserSettings.GetLanguageBound("Assignees", language));
            EnsureDefaults(language);
        }

        /// <summary>
        /// Значения по умолчанию при первом запуске — зависят от языка.
        /// </summary>
        public static void EnsureDefaults(string language = null)
        {
            string lang = UserSettings.NormalizeLanguage(language ?? UserSettings.Get("Language"));

            if (statusEntries == null || statusEntries.Count == 0)
                SetStatuses(UserSettings.GetDefaultTopicList("Stauses", lang));
            if (!availTypes.Any())
                SetTypes(UserSettings.GetDefaultTopicList("Types", lang));
            if (!availPriorities.Any())
                SetPriorities(UserSettings.GetDefaultTopicList("Priorities", lang));
            if (!availLabels.Any())
                SetLabels(UserSettings.GetDefaultTopicList("Labels", lang));
            if (!availAssignees.Any())
                SetAssignees(UserSettings.GetDefaultTopicList("Assignees", lang));
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
