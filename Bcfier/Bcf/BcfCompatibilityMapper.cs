using System;
using System.Collections.Generic;
using System.Linq;
using Bcfier.Bcf.Bcf2;

namespace Bcfier.Bcf
{
    /// <summary>
    /// Миграция Status/VerbalStatus комментариев в TopicType/TopicStatus по BCF 3.0.
    /// </summary>
    public static class BcfCompatibilityMapper
    {
        /// <summary>
        /// Нормализует topic после чтения markup.bcf с учётом версии BCF.
        /// </summary>
        public static void ApplyReadCompatibility(Markup issue, BcfFormatVersion formatVersion)
        {
            if (issue?.Topic == null)
                return;

            bool hasTopicType = !string.IsNullOrWhiteSpace(issue.Topic.TopicType);
            bool hasTopicStatus = !string.IsNullOrWhiteSpace(issue.Topic.TopicStatus);

            if (formatVersion == BcfFormatVersion.V10)
            {
                // BCF 1.0 хранит тип/статус только в комментарии
                Comment last = GetLastComment(issue);
                if (last == null)
                    return;

                if (!hasTopicType && !string.IsNullOrWhiteSpace(last.Status))
                    issue.Topic.TopicType = last.Status;

                if (!hasTopicStatus && !string.IsNullOrWhiteSpace(last.VerbalStatus))
                    issue.Topic.TopicStatus = last.VerbalStatus;

                return;
            }

            // BCF 2.0+: если в Topic есть тип/статус — поля комментариев не используются
            if (hasTopicType || hasTopicStatus)
                return;

            Comment fallback = GetLastComment(issue);
            if (fallback == null)
                return;

            if (!hasTopicType && !string.IsNullOrWhiteSpace(fallback.Status))
                issue.Topic.TopicType = fallback.Status;

            if (!hasTopicStatus && !string.IsNullOrWhiteSpace(fallback.VerbalStatus))
                issue.Topic.TopicStatus = fallback.VerbalStatus;
        }

        /// <summary>
        /// Перед записью BCF 2.0+ дублирует TopicType/TopicStatus в последний комментарий.
        /// </summary>
        public static void ApplyWriteCompatibility(Markup issue)
        {
            if (issue?.Topic == null)
                return;

            string topicType = issue.Topic.TopicType ?? string.Empty;
            string topicStatus = issue.Topic.TopicStatus ?? string.Empty;

            if (issue.Comment == null || issue.Comment.Count == 0)
            {
                // Нет комментариев — создаём пустой bootstrap, чтобы Status/VerbalStatus ушли в файл
                var bootstrap = new Comment
                {
                    Guid = Guid.NewGuid().ToString(),
                    Date = issue.Topic.CreationDate != default ? issue.Topic.CreationDate : DateTime.UtcNow,
                    Author = issue.Topic.CreationAuthor ?? string.Empty,
                    Comment1 = string.Empty,
                    Status = topicType,
                    VerbalStatus = topicStatus
                };
                issue.Comment = new System.Collections.ObjectModel.ObservableCollection<Comment> { bootstrap };
                return;
            }

            Comment target = GetLastComment(issue);
            if (target == null)
                return;

            target.Status = topicType;
            target.VerbalStatus = topicStatus;
        }

        private static Comment GetLastComment(Markup issue)
        {
            if (issue.Comment == null || issue.Comment.Count == 0)
                return null;

            return issue.Comment
                .OrderBy(c => c.Date)
                .LastOrDefault();
        }
    }
}
