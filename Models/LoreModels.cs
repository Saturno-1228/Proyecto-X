using System.Collections.Generic;
using System.Xml.Serialization;

namespace LivingCompanionsValley.Models
{
    [XmlRoot("Tema")]
    public class KnowledgeTopic
    {
        [XmlAttribute("id")]
        public string Id { get; set; } = string.Empty;

        [XmlElement("Keywords")]
        public string Keywords { get; set; } = string.Empty;

        [XmlElement("Lore")]
        public string Lore { get; set; } = string.Empty;
    }
}
