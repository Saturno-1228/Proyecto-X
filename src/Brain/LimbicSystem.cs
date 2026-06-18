using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using StardewModdingAPI;

namespace StardewLivingValley.Brain
{
    public class PortraitProfileRoot
    {
        [JsonPropertyName("Npcs")]
        public Dictionary<string, NpcPortraitProfile> Npcs { get; set; } = new Dictionary<string, NpcPortraitProfile>();
    }

    public class NpcPortraitProfile
    {
        [JsonPropertyName("DefaultFrames")]
        public Dictionary<string, int> DefaultFrames { get; set; } = new Dictionary<string, int>();
    }

    public class LimbicSystem
    {
        private readonly IModHelper _helper;
        private readonly IMonitor _logger;
        private Dictionary<string, NpcPortraitProfile> _profiles = new Dictionary<string, NpcPortraitProfile>(StringComparer.OrdinalIgnoreCase);

        // Mapeo fijo del número que la IA elige a la clave en DefaultFrames
        private readonly Dictionary<int, string> _aiToKeyMap = new Dictionary<int, string>
        {
            { 0, "Neutral" },
            { 1, "Happy" },
            { 2, "Sad" },
            { 3, "Surprised" }, // Sorprendido/Pensativo
            { 4, "Angry" },     // Enojado
            { 5, "Blush" }      // Sonrojado
        };

        public LimbicSystem(IModHelper helper, IMonitor logger)
        {
            _helper = helper;
            _logger = logger;
            LoadProfiles();
        }

        private void LoadProfiles()
        {
            string path = Path.Combine(_helper.DirectoryPath, "Assets", "portrait-emotion-profiles.json");
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var root = JsonSerializer.Deserialize<PortraitProfileRoot>(json);
                    if (root != null && root.Npcs != null)
                    {
                        _profiles = new Dictionary<string, NpcPortraitProfile>(root.Npcs, StringComparer.OrdinalIgnoreCase);
                        _logger.Log($"[LimbicSystem] Cargados perfiles de emoción para {_profiles.Count} NPCs.", LogLevel.Trace);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"[LimbicSystem] Error leyendo portrait-emotion-profiles.json: {ex.Message}", LogLevel.Error);
                }
            }
            else
            {
                _logger.Log("[LimbicSystem] No se encontró Assets/portrait-emotion-profiles.json.", LogLevel.Warn);
            }
        }

        public int GetFrameForEmotion(string npcName, int aiEmotionCode)
        {
            // Si el NPC tiene un perfil y conocemos la emoción que la IA intenta expresar
            if (_profiles.TryGetValue(npcName, out var profile) && _aiToKeyMap.TryGetValue(aiEmotionCode, out var emotionKey))
            {
                if (profile.DefaultFrames != null && profile.DefaultFrames.TryGetValue(emotionKey, out int specificFrame))
                {
                    return specificFrame;
                }
            }
            
            // Si no hay perfil, o la IA mandó un número extraño, caemos al raw code
            return aiEmotionCode;
        }
    }
}
