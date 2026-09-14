using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Xunit;

namespace Bcfier.Tests
{
    /// <summary>
    /// Диагностика чтения сторонних BCF вроде Solibri report (1).bcf.
    /// </summary>
    public class BcfReaderOpenDiagnosticsTests
    {
        [Fact]
        public void Open_CopiedReport1_DoesNotThrow()
        {
            string path = Path.Combine(Path.GetTempPath(), "report1.bcf");
            if (!File.Exists(path))
                return;

            BcfFile loaded = BcfReader.Open(path);
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded.Issues.Count);
            Assert.Contains(loaded.Issues, issue => issue.Viewpoints.Any(v => string.IsNullOrEmpty(v.Snapshot)));
        }

        [Fact]
        public void Deserialize_Markup_WithNanosecondDates()
        {
            string xml = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Markup>
    <Topic Guid=""c0a81005-9fb7-153c-819f-c678645d0b3d"">
        <Title>Test</Title>
        <Index>0</Index>
        <CreationDate>2026-08-25T07:32:00.002594100Z</CreationDate>
        <CreationAuthor>a@b.c</CreationAuthor>
    </Topic>
    <Viewpoints Guid=""3e129195-957a-4140-b074-c47f6c931d6e"">
        <Viewpoint>3e129195-957a-4140-b074-c47f6c931d6e.bcfv</Viewpoint>
    </Viewpoints>
</Markup>";

            Markup issue = Deserialize<Markup>(xml);
            Assert.NotNull(issue);
            Assert.NotNull(issue.Topic);
            Assert.Equal("Test", issue.Topic.Title);
        }

        [Fact]
        public void Deserialize_Viewpoint_WithNaNCamera()
        {
            string xml = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<VisualizationInfo>
    <Components>
        <Visibility DefaultVisibility=""false"">
            <Exceptions>
                <Component IfcGuid=""""/>
            </Exceptions>
        </Visibility>
    </Components>
    <PerspectiveCamera>
        <CameraViewPoint><X>NaN</X><Y>NaN</Y><Z>NaN</Z></CameraViewPoint>
        <CameraDirection><X>NaN</X><Y>NaN</Y><Z>NaN</Z></CameraDirection>
        <CameraUpVector><X>0.0</X><Y>0.0</Y><Z>1.0</Z></CameraUpVector>
        <FieldOfView>90.0</FieldOfView>
    </PerspectiveCamera>
</VisualizationInfo>";

            VisualizationInfo vis = Deserialize<VisualizationInfo>(xml);
            Assert.NotNull(vis);
            Assert.NotNull(vis.PerspectiveCamera);
        }

        [Fact]
        public void Open_Archive_WithoutSnapshot_DoesNotThrow()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "bcfier-diag-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string topicGuid = "c0a81005-9fb7-153c-819f-c678645d0b3d";
            string vpGuid = "3e129195-957a-4140-b074-c47f6c931d6e";
            string issueDir = Path.Combine(tempDir, topicGuid);
            Directory.CreateDirectory(issueDir);

            File.WriteAllText(Path.Combine(tempDir, "bcf.version"),
                "<Version VersionId=\"2.1\"/>", Encoding.UTF8);

            File.WriteAllText(Path.Combine(issueDir, "markup.bcf"),
                $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Markup>
    <Topic Guid=""{topicGuid}"">
        <Title>Проверка</Title>
        <Index>0</Index>
        <CreationDate>2026-08-25T07:32:00.002594100Z</CreationDate>
        <CreationAuthor>a@b.c</CreationAuthor>
    </Topic>
    <Viewpoints Guid=""{vpGuid}"">
        <Viewpoint>{vpGuid}.bcfv</Viewpoint>
    </Viewpoints>
</Markup>", Encoding.UTF8);

            File.WriteAllText(Path.Combine(issueDir, vpGuid + ".bcfv"),
                @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<VisualizationInfo>
    <PerspectiveCamera>
        <CameraViewPoint><X>NaN</X><Y>NaN</Y><Z>NaN</Z></CameraViewPoint>
        <CameraDirection><X>NaN</X><Y>NaN</Y><Z>NaN</Z></CameraDirection>
        <CameraUpVector><X>0.0</X><Y>0.0</Y><Z>1.0</Z></CameraUpVector>
        <FieldOfView>90.0</FieldOfView>
    </PerspectiveCamera>
</VisualizationInfo>", Encoding.UTF8);

            string zipPath = Path.Combine(Path.GetTempPath(), "bcfier-diag-" + Guid.NewGuid().ToString("N") + ".bcf");
            try
            {
                ZipFile.CreateFromDirectory(tempDir, zipPath);
                BcfFile loaded = BcfReader.Open(zipPath);
                Assert.Single(loaded.Issues);
                Assert.Single(loaded.Issues[0].Viewpoints);
                Assert.Null(loaded.Issues[0].Viewpoints[0].SnapshotPath);
                Assert.NotNull(loaded.Issues[0].Viewpoints[0].VisInfo);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
            }
        }

        private static T Deserialize<T>(string xml) where T : class
        {
            var serializer = new XmlSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                CloseInput = false
            }))
            {
                return serializer.Deserialize(reader) as T;
            }
        }
    }
}
