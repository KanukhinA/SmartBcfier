using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using Bcfier.Data.Utils;

namespace Bcfier.Bcf
{
    /// <summary>
    /// Определяет версию BCF по файлу bcf.version внутри архива.
    /// </summary>
    public static class BcfVersionReader
    {
        public const string DefaultWriteVersion = "3.0";
        public const string WriteVersionSettingKey = "BcfWriteVersion";

        /// <summary>
        /// Версия BCF для записи из настроек пользователя.
        /// </summary>
        public static string GetWriteVersion()
        {
            return NormalizeWriteVersion(UserSettings.Get(WriteVersionSettingKey));
        }

        /// <summary>
        /// Нормализует выбранную версию записи к поддерживаемым значениям.
        /// </summary>
        public static string NormalizeWriteVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return DefaultWriteVersion;

            version = version.Trim();
            switch (version)
            {
                case "1.0":
                case "2.0":
                case "2.1":
                case "3.0":
                    return version;
            }

            // Префиксный fallback для значений вроде "3", "1.0.0"
            if (version.StartsWith("1", StringComparison.Ordinal))
                return "1.0";
            if (version.StartsWith("3", StringComparison.Ordinal))
                return "3.0";

            return "2.1";
        }

        /// <summary>extensions.xml пишется только в BCF 3.0.</summary>
        public static bool WritesExtensions(string versionId) =>
            NormalizeWriteVersion(versionId) == "3.0";

        /// <summary>Topic.Index есть в 1.0–2.1, в 3.0 поле Index не используется.</summary>
        public static bool WritesTopicIndex(string versionId) =>
            NormalizeWriteVersion(versionId) != "3.0";

        public static BcfFormatVersion ReadFromExtractedFolder(string tempFolder)
        {
            string versionFile = Path.Combine(tempFolder, "bcf.version");
            // Нет bcf.version — считаем BCF 1.0
            if (!File.Exists(versionFile))
                return BcfFormatVersion.V10;

            try
            {
                using (var stream = new FileStream(versionFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
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
                    var doc = XDocument.Load(reader);
                string versionId = doc.Root?.Attribute("VersionId")?.Value
                    ?? doc.Root?.Element("VersionId")?.Value
                    ?? doc.Root?.Value;

                if (string.IsNullOrWhiteSpace(versionId))
                    return BcfFormatVersion.V20;

                versionId = versionId.Trim();
                if (versionId.StartsWith("1", StringComparison.Ordinal))
                    return BcfFormatVersion.V10;
                if (versionId.StartsWith("3", StringComparison.Ordinal))
                    return BcfFormatVersion.V30;

                return BcfFormatVersion.V20;
                }
            }
            catch
            {
                // Повреждённый version-файл трактуем как 2.x
                return BcfFormatVersion.V20;
            }
        }

        public static bool IsBcfArchive(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            string ext = Path.GetExtension(path);
            return ext.Equals(".bcfzip", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".bcf", StringComparison.OrdinalIgnoreCase);
        }
    }

    public enum BcfFormatVersion
    {
        V10,
        V20,
        V30
    }
}
