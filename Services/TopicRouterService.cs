using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using StardewModdingAPI;
using LivingCompanionsValley.Models;

namespace LivingCompanionsValley.Services
{
    public class TopicRouterService
    {
        private readonly IModHelper _helper;
        private readonly IMonitor _logger;

        // Cache de conocimientos por NPC. Dict<NpcName, List<KnowledgeTopic>>
        private Dictionary<string, List<KnowledgeTopic>> _knowledgeCache = new Dictionary<string, List<KnowledgeTopic>>();

        public TopicRouterService(IModHelper helper, IMonitor logger)
        {
            _helper = helper;
            _logger = logger;
        }

        private void EnsureNpcKnowledgeLoaded(string npcName)
        {
            var sanitizedName = System.Text.RegularExpressions.Regex.Replace(npcName, @"[^a-zA-Z0-9_\.\-]", "_");
            if (_knowledgeCache.ContainsKey(sanitizedName)) return;

            var topics = new List<KnowledgeTopic>();
            string npcKnowledgePath = Path.Combine(_helper.DirectoryPath, "Assets", "Knowledge", sanitizedName);

            if (Directory.Exists(npcKnowledgePath))
            {
                var xmlFiles = Directory.GetFiles(npcKnowledgePath, "*.xml", SearchOption.AllDirectories);
                var serializer = new XmlSerializer(typeof(KnowledgeTopic));

                foreach (var file in xmlFiles)
                {
                    try
                    {
                        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read))
                        {
                            if (serializer.Deserialize(stream) is KnowledgeTopic topic)
                            {
                                if (!string.IsNullOrEmpty(topic.Keywords))
                                {
                                    topic.ParsedKeywords = new HashSet<string>(
                                        topic.Keywords.Split(',')
                                            .Select(k => k.Trim().ToLowerInvariant())
                                            .Where(k => !string.IsNullOrEmpty(k))
                                    );
                                }
                                topics.Add(topic);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Log($"Error cargando conocimiento desde {file}: {ex.Message}", LogLevel.Error);
                    }
                }
            }

            _knowledgeCache[sanitizedName] = topics;
            _logger.Log($"Se cargaron {topics.Count} temas de conocimiento para {sanitizedName}.", LogLevel.Trace);
        }

        public string[] GetRelevantLoreChunks(string npcName, string userMessage)
        {
            var sanitizedName = System.Text.RegularExpressions.Regex.Replace(npcName, @"[^a-zA-Z0-9_\.\-]", "_");
            EnsureNpcKnowledgeLoaded(sanitizedName);
            
            var msg = userMessage.ToLowerInvariant();
            var matchedLore = new List<string>();

            if (_knowledgeCache.TryGetValue(sanitizedName, out var topics))
            {
                foreach (var topic in topics)
                {
                    if (topic.ParsedKeywords != null && topic.ParsedKeywords.Any(k => msg.Contains(k)))
                    {
                        matchedLore.Add(topic.Lore.Trim());
                        _logger.Log($"[{sanitizedName}] Keyword detectada para el tema: {topic.Id}", LogLevel.Debug);
                    }
                }
            }

            return matchedLore.ToArray();
        }
    }
}
