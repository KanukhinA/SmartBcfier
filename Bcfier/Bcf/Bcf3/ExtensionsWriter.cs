using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using Bcfier.Bcf.Bcf2;
using Bcfier.Bcf.Bcf3;
using Bcfier.Data;

namespace Bcfier.Bcf
{
    /// <summary>
    /// Сериализация extensions.xml для BCF 3.0.
    /// </summary>
    public static class ExtensionsWriter
    {
        public static Extensions BuildFromGlobals()
        {
            return new Extensions
            {
                TopicTypes = ToList(Globals.AvailTypes),
                TopicStatuses = ToList(Globals.AvailStatuses),
                Priorities = ToList(Globals.AvailPriorities),
                Labels = ToList(Globals.AvailLabels),
                Users = ToList(Globals.AvailAssignees)
            };
        }

        public static void Write(string tempFolder)
        {
            string path = Path.Combine(tempFolder, "extensions.xml");
            var extensions = BuildFromGlobals();
            var serializer = new XmlSerializer(typeof(Extensions));
            using (var stream = new FileStream(path, FileMode.Create))
            {
                serializer.Serialize(stream, extensions);
            }
        }

        public static void ApplyToGlobals(string tempFolder)
        {
            string path = Path.Combine(tempFolder, "extensions.xml");
            if (!File.Exists(path))
                return;

            try
            {
                var serializer = new XmlSerializer(typeof(Extensions));
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
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
                    var ext = serializer.Deserialize(reader) as Extensions;
                    if (ext == null)
                        return;

                    if (ext.TopicStatuses?.Any() == true)
                        Globals.SetStatuses(string.Join(", ", ext.TopicStatuses));
                    if (ext.TopicTypes?.Any() == true)
                        Globals.SetTypes(string.Join(", ", ext.TopicTypes));
                    if (ext.Priorities?.Any() == true)
                        Globals.SetPriorities(string.Join(", ", ext.Priorities));
                    if (ext.Labels?.Any() == true)
                        Globals.SetLabels(string.Join(", ", ext.Labels));
                    if (ext.Users?.Any() == true)
                        Globals.SetAssignees(string.Join(", ", ext.Users));
                }
            }
            catch
            {
                // неизвестные расширения игнорируем
            }
        }

        private static List<string> ToList(IEnumerable<string> source)
        {
            return source?
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }
    }
}
