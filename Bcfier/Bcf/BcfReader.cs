using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using Bcfier.Bcf.Bcf2;
using Bcfier.Bcf.Bcf3;

namespace Bcfier.Bcf
{
    /// <summary>
    /// Чтение BCF (.bcfzip / .bcf) с поддержкой версий 1.0–3.0.
    /// </summary>
    public static class BcfReader
    {
        private static readonly Lazy<XmlSerializer> VisualizationInfoSerializer =
            new Lazy<XmlSerializer>(() => new XmlSerializer(typeof(VisualizationInfo)));

        private static readonly Lazy<XmlSerializer> MarkupSerializer =
            new Lazy<XmlSerializer>(() => new XmlSerializer(typeof(Markup)));

        private static readonly Lazy<XmlSerializer> ProjectSerializer =
            new Lazy<XmlSerializer>(() => new XmlSerializer(typeof(ProjectExtension)));

        private static long _lastProgressTick;

        /// <summary>
        /// Передаёт короткий статус чтения BCF в UI.
        /// </summary>
        public static Action<string> ReportProgress { get; set; }

        public static BcfFile Open(string bcfArchivePath)
        {
            var bcffile = new BcfFile();
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var issues = new List<Markup>();
                if (!BcfVersionReader.IsBcfArchive(bcfArchivePath))
                    return bcffile;

                bcffile.Filename = Path.GetFileNameWithoutExtension(bcfArchivePath);
                bcffile.Fullname = bcfArchivePath;

                ReportStage("Открытие BCF-архива...");
                LogTiming(sw, "start");
                using (ZipArchive archive = ZipFile.OpenRead(bcfArchivePath))
                {
                    ReportStage("Распаковка BCF-архива...");
                    archive.ExtractToDirectory(bcffile.TempPath);
                }
                LogTiming(sw, "extract");

                ReportStage("Чтение версии BCF...");
                BcfFormatVersion formatVersion = BcfVersionReader.ReadFromExtractedFolder(bcffile.TempPath);
                LogTiming(sw, "version");
                ReportStage("Чтение расширений BCF...");
                ExtensionsWriter.ApplyToGlobals(bcffile.TempPath);
                LogTiming(sw, "extensions");

                string projectFile = Path.Combine(bcffile.TempPath, "project.bcfp");
                if (File.Exists(projectFile))
                {
                    ReportStage("Чтение project.bcfp...");
                    try
                    {
                        var project = DeserializeProject(projectFile);
                        if (project?.Project != null)
                        {
                            Guid.TryParse(project.Project.ProjectId, out Guid projectId);
                            bcffile.ProjectId = projectId;
                            bcffile.ProjectName = project.Project.Name;
                        }
                    }
                    catch (Exception ex)
                    {
                        bcffile.ReadWarnings.Add("project.bcfp: " + (ex.InnerException?.Message ?? ex.Message));
                    }
                }
                LogTiming(sw, "project");

                var dir = new DirectoryInfo(bcffile.TempPath);
                DirectoryInfo[] issueFolders = dir.GetDirectories();
                LogTiming(sw, $"folders({issueFolders.Length})");
                for (int i = 0; i < issueFolders.Length; i++)
                {
                    var folder = issueFolders[i];
                    string markupFile = Path.Combine(folder.FullName, "markup.bcf");
                    if (!File.Exists(markupFile))
                        continue;

                    ReportStage($"Чтение markup.bcf для issue {i + 1} из {issueFolders.Length}...");
                    try
                    {
                        var issue = DeserializeMarkup(markupFile);
                        if (issue == null)
                            continue;

                        BcfCompatibilityMapper.ApplyReadCompatibility(issue, formatVersion);
                        LoadViewpoints(folder.FullName, issue, bcffile.ReadWarnings);
                        BcfViewpointComponents.ApplyAuthoringToolIdsFromIssueText(issue);
                        PrepareIssueCollections(issue, i + 1);
                        issues.Add(issue);
                    }
                    catch (Exception ex)
                    {
                        bcffile.ReadWarnings.Add(
                            folder.Name + ": " + (ex.InnerException?.Message ?? ex.Message));
                    }

                    if (i % 50 == 0)
                        LogTiming(sw, $"issue[{i}]");
                }
                LogTiming(sw, "all_issues");

                try
                {
                    bcffile.Issues = new ObservableCollection<Markup>(issues.OrderBy(x => x.Topic.Index));
                }
                catch
                {
                    bcffile.Issues = new ObservableCollection<Markup>(issues);
                }
                LogTiming(sw, "done");

                LoadCustomFields(bcffile);

                return bcffile;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Не удалось открыть " + Path.GetFileName(bcfArchivePath) + ": " +
                    (ex.InnerException?.Message ?? ex.Message),
                    ex);
            }
        }

        /// <summary>
        /// Пишет таймстемп в Debug output для диагностики зависания.
        /// </summary>
        private static void LogTiming(System.Diagnostics.Stopwatch sw, string stage)
        {
            System.Diagnostics.Debug.WriteLine($"[BCFier] {stage}: {sw.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Готовит коллекции issue после чтения XML без подписок на UI-события,
        /// чтобы фоновая загрузка не провоцировала лишние уведомления WPF.
        /// </summary>
        private static void PrepareIssueCollections(Markup issue, int issueIndex)
        {
            if (issue == null)
                return;

            ReportStage($"Сортировка комментариев для issue {issueIndex}...");
            issue.Comment = new ObservableCollection<Comment>(
                (issue.Comment ?? new ObservableCollection<Comment>()).OrderBy(x => x.Date));

            try
            {
                ReportStage($"Сортировка viewpoints для issue {issueIndex}...");
                issue.Viewpoints = new ObservableCollection<ViewPoint>(
                    (issue.Viewpoints ?? new ObservableCollection<ViewPoint>()).OrderBy(x => x.Index));
            }
            catch
            {
                issue.Viewpoints = issue.Viewpoints ?? new ObservableCollection<ViewPoint>();
            }
        }

        /// <summary>
        /// Сообщает текущую стадию чтения BCF без влияния на основной поток выполнения.
        /// </summary>
        private static void ReportStage(string message)
        {
            try
            {
                if (!ShouldReportStage(message))
                    return;

                ReportProgress?.Invoke(message);
            }
            catch
            {
                // Ошибка статуса не должна ломать чтение BCF
            }
        }

        /// <summary>
        /// Ограничивает частоту промежуточных сообщений, чтобы загрузка большого BCF
        /// не забивала очередь UI тысячами почти одинаковых обновлений.
        /// </summary>
        private static bool ShouldReportStage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            if (!message.Contains("issue") && !message.Contains("viewpoint"))
                return true;

            long now = DateTime.UtcNow.Ticks;
            long previous = Interlocked.Read(ref _lastProgressTick);
            if (previous != 0 && now - previous < TimeSpan.FromMilliseconds(150).Ticks)
                return false;

            Interlocked.Exchange(ref _lastProgressTick, now);
            return true;
        }

        /// <summary>
        /// Читает viewpoint.bcfv и путь к снимку. Snapshot в BCF необязателен
        /// (Solibri/координатные проверки часто пишут только .bcfv).
        /// </summary>
        private static void LoadViewpoints(string issueFolder, Markup issue, ICollection<string> warnings)
        {
            if (issue.Viewpoints != null && issue.Viewpoints.Any())
            {
                foreach (var viewpoint in issue.Viewpoints)
                {
                    if (viewpoint == null)
                        continue;

                    ReportStage("Чтение viewpoint...");
                    try
                    {
                        string viewpointPath = CombineArchivePath(issueFolder, viewpoint.Viewpoint);
                        if (string.IsNullOrWhiteSpace(viewpointPath) || !File.Exists(viewpointPath))
                            continue;

                        viewpoint.VisInfo = DeserializeViewpoint(viewpointPath);
                        viewpoint.SnapshotPath = CombineArchivePath(issueFolder, viewpoint.Snapshot);
                    }
                    catch (Exception ex)
                    {
                        warnings?.Add(
                            "Viewpoint " + (viewpoint.Guid ?? string.Empty) + ": " +
                            (ex.InnerException?.Message ?? ex.Message));
                    }
                }

                return;
            }

            // BCF 1.0 — один viewpoint
            issue.Viewpoints = new ObservableCollection<ViewPoint>();
            string viewpointFile = Path.Combine(issueFolder, "viewpoint.bcfv");
            if (!File.Exists(viewpointFile))
                return;

            ReportStage("Чтение viewpoint...");
            try
            {
                issue.Viewpoints.Add(new ViewPoint(true)
                {
                    VisInfo = DeserializeViewpoint(viewpointFile),
                    SnapshotPath = Path.Combine(issueFolder, "snapshot.png"),
                });

                if (issue.Comment == null)
                    return;

                foreach (var comment in issue.Comment)
                {
                    comment.Viewpoint = new CommentViewpoint { Guid = issue.Viewpoints.First().Guid };
                }
            }
            catch (Exception ex)
            {
                warnings?.Add("viewpoint.bcfv: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        /// <summary>
        /// Собирает путь внутри распакованного BCF. Пустой относительный путь
        /// (нет Snapshot/Viewpoint) не передаём в Path.Combine: в .NET это ArgumentNullException.
        /// </summary>
        private static string CombineArchivePath(string folder, string relativeName)
        {
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(relativeName))
                return null;

            try
            {
                return Path.Combine(folder, relativeName);
            }
            catch
            {
                return null;
            }
        }

        private static VisualizationInfo DeserializeViewpoint(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = CreateSafeXmlReader(stream))
                {
                    return VisualizationInfoSerializer.Value.Deserialize(reader) as VisualizationInfo;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Не удалось прочитать " + Path.GetFileName(path) + ": " +
                    (ex.InnerException?.Message ?? ex.Message),
                    ex);
            }
        }

        private static Markup DeserializeMarkup(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = CreateSafeXmlReader(stream))
                {
                    return MarkupSerializer.Value.Deserialize(reader) as Markup;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Не удалось прочитать markup.bcf: " +
                    (ex.InnerException?.Message ?? ex.Message),
                    ex);
            }
        }

        private static ProjectExtension DeserializeProject(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = CreateSafeXmlReader(stream))
                {
                    return ProjectSerializer.Value.Deserialize(reader) as ProjectExtension;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Не удалось прочитать project.bcfp: " +
                    (ex.InnerException?.Message ?? ex.Message),
                    ex);
            }
        }

        private static void LoadCustomFields(BcfFile bcffile)
        {
            try
            {
                CustomFields.CustomFieldsLoadResult loaded =
                  CustomFields.CustomFieldsXmlStore.LoadFromDocuments(bcffile.TempPath);

                foreach (string warning in loaded.Warnings)
                    bcffile.ReadWarnings.Add("Documents: " + warning);

                CustomFields.CustomFieldsXmlStore.ClearIssueFields(bcffile.Issues);

                bcffile.ReportLevelCustomFields.Clear();
                foreach (CustomFields.CustomFieldValue field in loaded.ReportLevel)
                    bcffile.ReportLevelCustomFields.Add(field);

                bcffile.DocumentSettings.CopyFrom(loaded.Document);
                bcffile.HasCustomFields = loaded.HasAny;
                bcffile.CustomFieldsVisible = false;
            }
            catch (Exception ex)
            {
                bcffile.ReadWarnings.Add("Documents: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        /// <summary>
        /// Создаёт безопасный XML reader без внешних резолверов и DTD, чтобы чтение BCF
        /// не зависало на проблемных DOCTYPE/ENTITY в сторонних файлах.
        /// </summary>
        private static XmlReader CreateSafeXmlReader(Stream stream)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                CloseInput = false
            };

            return XmlReader.Create(stream, settings);
        }
    }
}
