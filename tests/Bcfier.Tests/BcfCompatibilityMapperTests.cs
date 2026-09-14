using System;
using System.Collections.ObjectModel;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Xunit;

namespace Bcfier.Tests
{
    public class BcfCompatibilityMapperTests
    {
        [Fact]
        public void Read_Bcf10_UsesLastCommentStatusFields()
        {
            var issue = new Markup
            {
                Topic = new Topic(),
                Comment = new ObservableCollection<Comment>
                {
                    new Comment { Status = "Old", VerbalStatus = "OldV", Date = DateTime.UtcNow.AddDays(-1) },
                    new Comment { Status = "TypeA", VerbalStatus = "StatusA", Date = DateTime.UtcNow }
                }
            };

            BcfCompatibilityMapper.ApplyReadCompatibility(issue, BcfFormatVersion.V10);

            Assert.Equal("TypeA", issue.Topic.TopicType);
            Assert.Equal("StatusA", issue.Topic.TopicStatus);
        }

        [Fact]
        public void Read_Bcf20_PrefersTopicFieldsOverComment()
        {
            var issue = new Markup
            {
                Topic = new Topic { TopicType = "Coordination", TopicStatus = "Open" },
                Comment = new ObservableCollection<Comment>
                {
                    new Comment { Status = "Ignored", VerbalStatus = "IgnoredV", Date = DateTime.UtcNow }
                }
            };

            BcfCompatibilityMapper.ApplyReadCompatibility(issue, BcfFormatVersion.V20);

            Assert.Equal("Coordination", issue.Topic.TopicType);
            Assert.Equal("Open", issue.Topic.TopicStatus);
        }

        [Fact]
        public void Write_DuplicatesTopicFieldsToLastComment()
        {
            var issue = new Markup
            {
                Topic = new Topic { TopicType = "Clash", TopicStatus = "Active", CreationAuthor = "user" },
                Comment = new ObservableCollection<Comment>
                {
                    new Comment { Guid = Guid.NewGuid().ToString(), Date = DateTime.UtcNow, Author = "user", Comment1 = "c" }
                }
            };

            BcfCompatibilityMapper.ApplyWriteCompatibility(issue);
            var last = issue.Comment[issue.Comment.Count - 1];

            Assert.Equal("Clash", last.Status);
            Assert.Equal("Active", last.VerbalStatus);
        }
    }
}
