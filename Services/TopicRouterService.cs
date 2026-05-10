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
            if (_knowledgeCache.ContainsKey(npcName)) return;

            var topics = new List<KnowledgeTopic>();
            string npcKnowledgePath = Path.Combine(_helper.DirectoryPath, "Assets", "Knowledge", npcName);

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

            _knowledgeCache[npcName] = topics;
            _logger.Log($"Se cargaron {topics.Count} temas de conocimiento para {npcName}.", LogLevel.Trace);
        }

        public string[] GetRelevantLoreChunks(string npcName, string userMessage)
        {
            EnsureNpcKnowledgeLoaded(npcName);
            
            var msg = userMessage.ToLowerInvariant();
            var matchedLore = new List<string>();

            if (_knowledgeCache.TryGetValue(npcName, out var topics))
            {
                foreach (var topic in topics)
                {
                    var keywords = topic.Keywords.Split(',').Select(k => k.Trim().ToLowerInvariant());
                    if (keywords.Any(k => msg.Contains(k)))
                    {
                        matchedLore.Add(topic.Lore.Trim());
                        _logger.Log($"[{npcName}] Keyword detectada para el tema: {topic.Id}", LogLevel.Debug);
                    }
                }
            }

            return matchedLore.ToArray();
        }
    }
}
