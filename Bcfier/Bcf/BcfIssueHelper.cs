using System;
using System.Collections.Generic;
using System.Linq;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;

namespace Bcfier.Bcf
{
    /// <summary>
    /// Вспомогательные операции с полями topic при создании/сохранении.
    /// </summary>
    public static class BcfIssueHelper
    {
        public static void InitializeNewIssue(Markup issue, string creationAuthor)
        {
            if (issue?.Topic == null)
                return;

            issue.Topic.CreationAuthor = creationAuthor ?? string.Empty;
            issue.Topic.CreationDate = DateTime.UtcNow;
            issue.Topic.ModifiedDate = issue.Topic.CreationDate;

            if (issue.Topic.SelectedLabels == null)
                issue.Topic.SelectedLabels = new System.Collections.ObjectModel.ObservableCollection<string>();

            FillDropdownsFromGlobals(issue.Topic);
            SyncLabelsToTopic(issue.Topic);
        }

        /// <summary>Labels[] (XML) → SelectedLabels (UI).</summary>
        public static void SyncLabelsFromTopic(Topic topic)
        {
            if (topic == null)
                return;

            topic.SelectedLabels = new System.Collections.ObjectModel.ObservableCollection<string>(
                topic.Labels ?? Array.Empty<string>());
        }

        /// <summary>SelectedLabels (UI) → Labels[] перед сериализацией.</summary>
        public static void SyncLabelsToTopic(Topic topic)
        {
            if (topic?.SelectedLabels == null)
                return;

            topic.Labels = topic.SelectedLabels
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Заполняет выпадающие списки topic значениями из настроек.
        /// </summary>
        public static void FillDropdownsFromGlobals(Topic topic)
        {
            if (topic == null)
                return;

            try
            {
                ReplaceCollection(topic.TopicStatusesCollection, Globals.AvailStatuses, topic.TopicStatus);
                ReplaceCollection(topic.TopicTypesCollection, Globals.AvailTypes, topic.TopicType);
                ReplaceCollection(topic.PrioritiesCollection, Globals.AvailPriorities, topic.Priority);
                ReplaceCollection(topic.LabelsCollection, Globals.AvailLabels, topic.SelectedLabels?.FirstOrDefault());
                ReplaceCollection(topic.AssigneesCollection, Globals.AvailAssignees, topic.AssignedTo);
                topic.NotifyDropdownCollectionsChanged();
            }
            catch
            {
                // Списки UI не должны ломать создание замечания
            }
        }

        /// <summary>
        /// Пересобирает коллекцию ComboBox на месте и сохраняет выбранное значение.
        /// </summary>
        private static void ReplaceCollection(
            System.Collections.ObjectModel.ObservableCollection<string> target,
            IEnumerable<string> source,
            string selected)
        {
            if (target == null)
                return;

            target.Clear();
            if (!string.IsNullOrWhiteSpace(selected))
                target.Add(selected);

            foreach (string item in source ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(item) || target.Contains(item))
                    continue;

                target.Add(item);
            }
        }

        public static void ApplyDueDate(Topic topic, DateTime? dueDate)
        {
            if (topic == null)
                return;

            if (dueDate.HasValue)
            {
                topic.DueDate = dueDate.Value;
                topic.DueDateSpecified = true;
            }
            else
            {
                // Без флага XSD не сериализует DueDate
                topic.DueDateSpecified = false;
            }
        }
    }
}
