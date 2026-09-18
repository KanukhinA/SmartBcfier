using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Bcfier.Bcf.Bcf2;
using Bcfier.Bcf.Bcf3;

namespace Bcfier.Bcf
{
    /// <summary>
    /// Запись BCF 3.0 (.bcf / .bcfzip) с обратной совместимостью comment Status/VerbalStatus.
    /// </summary>
    public static class BcfWriter
    {
        public static bool Save(BcfFile bcffile, string filename)
        {
            if (bcffile.Issues.Count == 0)
                return false;

            if (!Directory.Exists(bcffile.TempPath))
                Directory.CreateDirectory(bcffile.TempPath);

            string writeVersion = BcfVersionReader.GetWriteVersion();

            var bcfProject = new ProjectExtension
            {
                Project = new Project
                {
                    Name = string.IsNullOrEmpty(bcffile.ProjectName) ? bcffile.Filename : bcffile.ProjectName,
                    ProjectId = bcffile.ProjectId.Equals(Guid.Empty)
                        ? Guid.NewGuid().ToString()
                        : bcffile.ProjectId.ToString()
                },
                ExtensionSchema = string.Empty
            };

            var bcfVersion = new Bcfier.Bcf.Bcf2.Version
            {
                VersionId = writeVersion,
                DetailedVersion = writeVersion
            };

            Serialize(Path.Combine(bcffile.TempPath, "project.bcfp"), bcfProject);
            Serialize(Path.Combine(bcffile.TempPath, "bcf.version"), bcfVersion);

            // BCF 3.0 — extensions.xml; иначе удаляем оставшийся файл от прошлой сессии
            string extensionsPath = Path.Combine(bcffile.TempPath, "extensions.xml");
            if (BcfVersionReader.WritesExtensions(writeVersion))
                ExtensionsWriter.Write(bcffile.TempPath);
            else if (File.Exists(extensionsPath))
                File.Delete(extensionsPath);

            var serializerV = new XmlSerializer(typeof(VisualizationInfo));
            var serializerM = new XmlSerializer(typeof(Markup));

            int index = 0;
            bool writeTopicIndex = BcfVersionReader.WritesTopicIndex(writeVersion);
            foreach (var issue in bcffile.Issues)
            {
                // Дублируем TopicType/TopicStatus в комментарий для читателей 2.x
                BcfCompatibilityMapper.ApplyWriteCompatibility(issue);

                if (writeTopicIndex)
                {
                    issue.Topic.Index = index;
                    issue.Topic.IndexSpecified = true;
                    index++;
                }
                else
                {
                    // BCF 3.0: Index не пишем
                    issue.Topic.IndexSpecified = false;
                }

                string issuePath = Path.Combine(bcffile.TempPath, issue.Topic.Guid);
                if (!Directory.Exists(issuePath))
                    Directory.CreateDirectory(issuePath);

                for (int i = 0; i < issue.Viewpoints.Count; i++)
                {
                    issue.Viewpoints[i].Index = i;
                    issue.Viewpoints[i].IndexSpecified = true;
                }

                // BCF 1.0: фиксированные имена viewpoint/snapshot
                if (writeVersion == "1.0"
                    && issue.Viewpoints.Any()
                    && (issue.Viewpoints.Count == 1 || issue.Viewpoints.All(o => o.Viewpoint != "viewpoint.bcfv")))
                {
                    string vpPath = Path.Combine(issuePath, issue.Viewpoints[0].Viewpoint);
                    if (File.Exists(vpPath))
                        File.Delete(vpPath);

                    issue.Viewpoints[0].Viewpoint = "viewpoint.bcfv";
                    string snapPath = Path.Combine(issuePath, issue.Viewpoints[0].Snapshot);
                    if (File.Exists(snapPath) && issue.Viewpoints[0].Snapshot != "snapshot.png")
                        File.Move(snapPath, Path.Combine(issuePath, "snapshot.png"));

                    issue.Viewpoints[0].Snapshot = "snapshot.png";
                }

                Serialize(Path.Combine(issuePath, "markup.bcf"), issue);

                foreach (var viewpoint in issue.Viewpoints)
                {
                    Serialize(Path.Combine(issuePath, viewpoint.Viewpoint), viewpoint.VisInfo, serializerV);
                }
            }

            if (File.Exists(filename))
                File.Delete(filename);

            Bcfier.CustomFields.CustomFieldsXmlStore.SaveCanonical(
                bcffile.TempPath,
                bcffile.ReportLevelCustomFields,
                bcffile.DocumentSettings);

            ZipFile.CreateFromDirectory(
                bcffile.TempPath,
                filename,
                CompressionLevel.Optimal,
                includeBaseDirectory: false,
                entryNameEncoding: new ZipForwardSlashEncoder());

            bcffile.HasBeenSaved = true;
            bcffile.Filename = Path.GetFileName(filename);
            bcffile.Fullname = filename;
            return true;
        }

        private static void Serialize<T>(string path, T obj, XmlSerializer serializer = null)
        {
            serializer ??= new XmlSerializer(typeof(T));
            using (var stream = new FileStream(path, FileMode.Create))
            {
                serializer.Serialize(stream, obj);
            }
        }

        /// <summary>
        /// Кодировка zip с прямым слэшем в путях (требование BCF 3.0).
        /// </summary>
        private sealed class ZipForwardSlashEncoder : UTF8Encoding
        {
            public override byte[] GetBytes(string s)
            {
                return base.GetBytes(s.Replace("\\", "/"));
            }
        }
    }
}
