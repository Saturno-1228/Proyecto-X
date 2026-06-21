using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using StardewValley;
using StardewLivingValley.Models;

namespace StardewLivingValley.Brain
{
    public class Hippocampus
    {
        private string _modDirectory;
        private Dictionary<string, UserProfile> _userProfiles = new Dictionary<string, UserProfile>();
        private Dictionary<string, List<string>> _pendingMemories = new Dictionary<string, List<string>>();
        private Dictionary<string, List<LongTermMemory>> _longTermMemories = new Dictionary<string, List<LongTermMemory>>();

        public Hippocampus(string modDirectory)
        {
            _modDirectory = modDirectory;
        }

        private string GetMemoryFilePath(string npcName)
        {
            return Path.Combine(_modDirectory, $"longterm_memory_{npcName}.json");
        }

        public void LoadLongTermMemories(string npcName)
        {
            if (_longTermMemories.ContainsKey(npcName)) return;

            string filePath = GetMemoryFilePath(npcName);
            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    _longTermMemories[npcName] = JsonSerializer.Deserialize<List<LongTermMemory>>(json) ?? new List<LongTermMemory>();
                }
                catch (Exception)
                {
                    _longTermMemories[npcName] = new List<LongTermMemory>();
                }
            }
            else
            {
                _longTermMemories[npcName] = new List<LongTermMemory>();
            }
        }

        private void SaveLongTermMemories(string npcName)
        {
            if (!_longTermMemories.ContainsKey(npcName)) return;

            string filePath = GetMemoryFilePath(npcName);
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_longTermMemories[npcName], options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception)
            {
                // Ignorar fallos de escritura por ahora
            }
        }

        public UserProfile GetUserProfile(string npcName)
        {
            if (!_userProfiles.ContainsKey(npcName))
                _userProfiles[npcName] = new UserProfile { PlayerName = StardewValley.Game1.player?.Name ?? "Granjero" };
            return _userProfiles[npcName];
        }

        public void SaveNpcMemory(string npcName, string memory)
        {
            // 1. Guardar como memoria pendiente (corto plazo)
            if (!_pendingMemories.ContainsKey(npcName))
            {
                _pendingMemories[npcName] = new List<string>();
            }
            _pendingMemories[npcName].Add(memory);

            // 2. Guardar como memoria a largo plazo (episódica)
            LoadLongTermMemories(npcName);
            
            string timestamp = $"[Día {Game1.dayOfMonth} de {Game1.currentSeason}, Año {Game1.year}]";
            _longTermMemories[npcName].Add(new LongTermMemory
            {
                Timestamp = timestamp,
                Content = memory,
                Category = "General"
            });

            // Limitar a las 15 más recientes
            if (_longTermMemories[npcName].Count > 15)
            {
                _longTermMemories[npcName].RemoveAt(0);
            }

            SaveLongTermMemories(npcName);
        }

        public string[] GetActiveMemories(string npcName)
        {
            var memories = new List<string>();

            if (_pendingMemories.ContainsKey(npcName))
            {
                memories.AddRange(_pendingMemories[npcName]);
                _pendingMemories[npcName].Clear(); // Consumir las memorias pendientes
            }

            return memories.ToArray();
        }

        public string[] GetLongTermMemories(string npcName)
        {
            LoadLongTermMemories(npcName);
            var results = new List<string>();
            foreach (var mem in _longTermMemories[npcName])
            {
                results.Add($"{mem.Timestamp} {mem.Content}");
            }
            return results.ToArray();
        }

        public string[] GetMemoriesAbout(string myName, string targetNpcName)
        {
            LoadLongTermMemories(myName);
            var results = new List<string>();
            foreach (var mem in _longTermMemories[myName])
            {
                if (mem.Content.Contains(targetNpcName, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add($"{mem.Timestamp} {mem.Content}");
                }
            }
            return results.ToArray();
        }
    }
}
