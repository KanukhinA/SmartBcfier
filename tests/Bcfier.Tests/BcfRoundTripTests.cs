using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data.Utils;
using Xunit;

namespace Bcfier.Tests
{
    public class BcfRoundTripTests
    {
        [Fact]
        public void Write_Read_Bcf30_PreservesTopicFields()
        {
            var bcf = CreateSampleBcf();
            string path = Path.Combine(Path.GetTempPath(), "bcfier-test-" + Guid.NewGuid().ToString("N") + ".bcfzip");

            try
            {
                Assert.True(BcfWriter.Save(bcf, path));
                Assert.True(File.Exists(path));

                var loaded = BcfReader.Open(path);
                Assert.Single(loaded.Issues);
                var topic = loaded.Issues[0].Topic;
                Assert.Equal("Clash", topic.TopicType);
                Assert.Equal("Open", topic.TopicStatus);
                Assert.Equal("author", topic.CreationAuthor);
                Assert.Equal("user@company.com", topic.AssignedTo);
                Assert.Equal("High", topic.Priority);
                Assert.Contains("Coordination", topic.Labels);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void Write_Bcf21_UsesSelectedVersionWithoutExtensions()
        {
            UserSettings.Set(BcfVersionReader.WriteVersionSettingKey, "2.1");
            var bcf = CreateSampleBcf();
            string path = Path.Combine(Path.GetTempPath(), "bcfier-test-" + Guid.NewGuid().ToString("N") + ".bcfzip");

            try
            {
                Assert.True(BcfWriter.Save(bcf, path));

                using (var archive = ZipFile.OpenRead(path))
                {
                    var versionEntry = archive.GetEntry("bcf.version");
                    Assert.NotNull(versionEntry);
                    using (var reader = new StreamReader(versionEntry.Open()))
                    {
                        string versionXml = reader.ReadToEnd();
                        Assert.Contains("VersionId=\"2.1\"", versionXml);
                    }

                    Assert.Null(archive.GetEntry("extensions.xml"));
                }
            }
            finally
            {
                UserSettings.Set(BcfVersionReader.WriteVersionSettingKey, "3.0");
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void Read_Bcf21_IgnoresCommentStatusWhenTopicFieldsPresent()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "bcfier-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                WriteVersion(tempDir, "2.1");
                WriteMarkup(tempDir, "topic-1", "Coordination", "Open", "Ignored", "IgnoredV");

                string path = Path.Combine(Path.GetTempPath(), "bcfier-test-" + Guid.NewGuid().ToString("N") + ".bcfzip");
                ZipFile.CreateFromDirectory(tempDir, path);

                var loaded = BcfReader.Open(path);
                Assert.Equal("Coordination", loaded.Issues[0].Topic.TopicType);
                Assert.Equal("Open", loaded.Issues[0].Topic.TopicStatus);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Read_Bcf10_MapsCommentStatusToTopic()
        {
            var issue = new Markup
            {
                Topic = new Topic(),
                Comment = new ObservableCollection<Comment>
                {
                    new Comment { Status = "TypeA", VerbalStatus = "StatusA", Date = DateTime.UtcNow }
                }
            };

            BcfCompatibilityMapper.ApplyReadCompatibility(issue, BcfFormatVersion.V10);

            Assert.Equal("TypeA", issue.Topic.TopicType);
            Assert.Equal("StatusA", issue.Topic.TopicStatus);
        }

        private static BcfFile CreateSampleBcf()
        {
            var issue = new Markup
            {
                Topic = new Topic
                {
                    Guid = Guid.NewGuid().ToString().ToLowerInvariant(),
                    Title = "Test issue",
                    TopicType = "Clash",
                    TopicStatus = "Open",
                    CreationAuthor = "author",
                    AssignedTo = "user@company.com",
                    Priority = "High",
                    Labels = new[] { "Coordination" }
                },
                Comment = new ObservableCollection<Comment>
                {
                    new Comment
                    {
                        Guid = Guid.NewGuid().ToString(),
                        Author = "author",
                        Date = DateTime.UtcNow,
                        Comment1 = "comment"
                    }
                },
                Viewpoints = new ObservableCollection<ViewPoint>()
            };

            var bcf = new BcfFile { Filename = "test" };
            bcf.Issues.Add(issue);
            return bcf;
        }

        private static void WriteVersion(string dir, string version)
        {
            File.WriteAllText(Path.Combine(dir, "bcf.version"),
                $"<Version VersionId=\"{version}\" DetailedVersion=\"{version}\" />",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "project.bcfp"),
                "<ProjectExtension><Project><Name>Test</Name><ProjectId>id</ProjectId></Project></ProjectExtension>",
                Encoding.UTF8);
        }

        private static void WriteMarkup(string dir, string topicGuid, string topicType, string topicStatus, string commentStatus, string verbalStatus)
        {
            string folder = Path.Combine(dir, topicGuid);
            Directory.CreateDirectory(folder);
            string markup = $@"<Markup>
  <Topic Guid=""{topicGuid}"" TopicType=""{topicType}"" TopicStatus=""{topicStatus}"" Title=""Issue"" CreationDate=""2020-01-01T00:00:00Z"" />
  <Comment Guid=""c1"" Date=""2020-01-02T00:00:00Z"" Author=""a"" Status=""{commentStatus}"" VerbalStatus=""{verbalStatus}""><Comment>text</Comment></Comment>
</Markup>";
            File.WriteAllText(Path.Combine(folder, "markup.bcf"), markup, Encoding.UTF8);
        }

        private static void WriteMarkupV10(string dir, string topicGuid, string status, string verbalStatus)
        {
            WriteMarkup(dir, topicGuid, null, null, status, verbalStatus);
        }
    }
}
