using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Bcfier.Bcf.Bcf3
{
    /// <summary>
    /// extensions.xml BCF 3.0 — предопределённые списки topic-полей.
    /// </summary>
    [XmlRoot("Extensions")]
    public class Extensions
    {
        [XmlArray("TopicTypes")]
        [XmlArrayItem("TopicType")]
        public List<string> TopicTypes { get; set; } = new List<string>();

        [XmlArray("TopicStatuses")]
        [XmlArrayItem("TopicStatus")]
        public List<string> TopicStatuses { get; set; } = new List<string>();

        [XmlArray("Priorities")]
        [XmlArrayItem("Priority")]
        public List<string> Priorities { get; set; } = new List<string>();

        [XmlArray("Labels")]
        [XmlArrayItem("Label")]
        public List<string> Labels { get; set; } = new List<string>();

        [XmlArray("Users")]
        [XmlArrayItem("User")]
        public List<string> Users { get; set; } = new List<string>();
    }
}
