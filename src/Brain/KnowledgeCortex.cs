using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using StardewModdingAPI;

namespace StardewLivingValley.Brain
{
    public class TriggerCluster
    {
        public string Intent { get; set; } = string.Empty;
        public List<string> Patterns { get; set; } = new List<string>();
        public int Weight { get; set; } = 1;
    }

    public class LoreConditions
    {
        public int MinHearts { get; set; } = 0;
        public int MaxHearts { get; set; } = 14;
        public string? Season { get; set; } = null;
    }

    public class LoreChunk
    {
        public string Id { get; set; } = string.Empty;
        public string Lore { get; set; } = string.Empty;
        public LoreConditions Conditions { get; set; } = new LoreConditions();
        public List<TriggerCluster> Triggers { get; set; } = new List<TriggerCluster>();
    }

    public class NpcKnowledgeProfile
    {
        public string Role { get; set; } = string.Empty;
        public string Persona { get; set; } = string.Empty;
        public string Speech { get; set; } = string.Empty;
        public string Ties { get; set; } = string.Empty;
        public string Boundaries { get; set; } = string.Empty;
        public List<string> ForbiddenClaims { get; set; } = new List<string>();
        public Dictionary<string, string> AllowedGifts { get; set; } = new Dictionary<string, string>();
        
        public List<LoreChunk> DynamicLore { get; set; } = new List<LoreChunk>();
    }

    public class KnowledgeCortex
    {
        private readonly IModHelper _helper;
        private readonly IMonitor _logger;

        private Dictionary<string, NpcKnowledgeProfile> _knowledgeCache = new Dictionary<string, NpcKnowledgeProfile>();
        private readonly XmlBrainParser _xmlParser;

        public KnowledgeCortex(IModHelper helper, IMonitor logger)
        {
            _helper = helper;
            _logger = logger;
            _xmlParser = new XmlBrainParser(logger);
            LoadAllKnowledge();
        }

        public void LoadAllKnowledge()
        {
            string knowledgePath = Path.Combine(_helper.DirectoryPath, "Assets", "Brains");
            if (!Directory.Exists(knowledgePath))
            {
                Directory.CreateDirectory(knowledgePath);
                CreatePlaceholderProfile(knowledgePath, "Robin"); // Creamos un par de ejemplos
                CreatePlaceholderProfile(knowledgePath, "Abigail");
                CreatePlaceholderProfile(knowledgePath, "Lewis");
            }

            var directories = Directory.GetDirectories(knowledgePath);
            foreach (var dir in directories)
            {
                string npcName = Path.GetFileName(dir);
                string profilePath = Path.Combine(dir, "personality.json");
                
                if (File.Exists(profilePath))
                {
                    try
                    {
                        string json = File.ReadAllText(profilePath);
                        var profile = JsonSerializer.Deserialize<NpcKnowledgeProfile>(json);
                        if (profile != null)
                        {
                            _knowledgeCache[npcName.ToLowerInvariant()] = profile;
                            _logger.Log($"[{npcName}] Conocimiento cargado exitosamente.", LogLevel.Trace);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Log($"Error cargando conocimiento para {npcName}: {ex.Message}", LogLevel.Error);
                    }
                }
            }
        }

        private void CreatePlaceholderProfile(string basePath, string npcName)
        {
            string dir = Path.Combine(basePath, npcName);
            Directory.CreateDirectory(dir);
            string profilePath = Path.Combine(dir, "personality.json");

            var profile = new NpcKnowledgeProfile
            {
                Role = $"Eres {npcName}, un aldeano de Stardew Valley.",
                Persona = "Amable, trabajador y con una personalidad única que los jugadores de Stardew Valley reconocen.",
                Speech = "Conversacional, natural, usando un tono apropiado para tu edad y oficio.",
                Boundaries = "Nunca reveles que eres un personaje de un juego. Nunca rompas tu personaje.",
                ForbiddenClaims = new List<string> { "Soy una Inteligencia Artificial", "Veo el futuro", "Soy el creador del juego" },
                AllowedGifts = new Dictionary<string, string>
                {
                    { "Madera", "(O)388" },
                    { "Piedra", "(O)390" },
                    { "Café", "(O)395" }
                },
                DynamicLore = new List<LoreChunk>
                {
                    new LoreChunk
                    {
                        Id = "clima_lluvia",
                        Lore = "Me encanta cuando llueve, es bueno para las cosechas y da un ambiente relajante.",
                        Conditions = new LoreConditions { MinHearts = 0 },
                        Triggers = new List<TriggerCluster>
                        {
                            new TriggerCluster { Intent = "Lluvia", Patterns = new List<string> { "lluvia", "llover", "tormenta", "mojado" }, Weight = 5 }
                        }
                    }
                }
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(profilePath, JsonSerializer.Serialize(profile, options));
        }

        public NpcKnowledgeProfile? GetDynamicProfile(string npcName, int currentHearts)
        {
            if (_knowledgeCache.TryGetValue(npcName.ToLowerInvariant(), out var profile))
            {
                // Create a clone of the profile to avoid modifying the cached version permanently
                var dynamicProfile = new NpcKnowledgeProfile
                {
                    Role = _xmlParser.ProcessBrainXml(profile.Role, npcName, currentHearts),
                    Persona = profile.Persona,
                    Speech = profile.Speech,
                    Ties = profile.Ties,
                    Boundaries = profile.Boundaries,
                    ForbiddenClaims = new List<string>(profile.ForbiddenClaims),
                    AllowedGifts = new Dictionary<string, string>(profile.AllowedGifts),
                    DynamicLore = profile.DynamicLore // Kept as reference, it's evaluated separately
                };
                return dynamicProfile;
            }
            
            // Fallback genérico si no existe
            return new NpcKnowledgeProfile 
            { 
                Role = $"Eres {npcName}, un residente de Pelican Town.",
                Persona = "Hablas de forma natural.",
                Speech = "Breve y directo."
            };
        }

        public string[] GetRelevantLoreChunks(string npcName, string userMessage, int currentHearts, string currentSeason)
        {
            if (!_knowledgeCache.TryGetValue(npcName.ToLowerInvariant(), out var profile))
                return Array.Empty<string>();

            var msg = userMessage.ToLowerInvariant();
            var scoredLore = new List<(LoreChunk chunk, int score)>();

            foreach (var chunk in profile.DynamicLore)
            {
                if (currentHearts < chunk.Conditions.MinHearts || currentHearts > chunk.Conditions.MaxHearts)
                    continue;
                
                if (!string.IsNullOrEmpty(chunk.Conditions.Season) && chunk.Conditions.Season.ToLowerInvariant() != currentSeason.ToLowerInvariant())
                    continue;

                int score = 0;
                foreach (var trigger in chunk.Triggers)
                {
                    foreach (var pattern in trigger.Patterns)
                    {
                        if (Regex.IsMatch(msg, $@"\b{pattern}\b", RegexOptions.IgnoreCase))
                        {
                            score += trigger.Weight;
                            break;
                        }
                    }
                }

                if (score > 0)
                {
                    scoredLore.Add((chunk, score));
                }
            }

            return scoredLore.OrderByDescending(x => x.score)
                             .Take(2)
                             .Select(x => x.chunk.Lore)
                             .ToArray();
        }
    }
}
