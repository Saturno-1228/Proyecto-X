using System.Collections.Generic;
using StardewLivingValley.Models;

namespace StardewLivingValley.Services
{
    public class MemoryService
    {
        private Dictionary<string, UserProfile> _userProfiles = new Dictionary<string, UserProfile>();
        private Dictionary<string, List<string>> _pendingMemories = new Dictionary<string, List<string>>();

        public UserProfile GetUserProfile(string npcName)
        {
            if (!_userProfiles.ContainsKey(npcName))
                _userProfiles[npcName] = new UserProfile { PlayerName = StardewValley.Game1.player?.Name ?? "Granjero" };
            return _userProfiles[npcName];
        }

        public void SavePlayerMemory(string npcName, string memory)
        {
            if (!_pendingMemories.ContainsKey(npcName))
            {
                _pendingMemories[npcName] = new List<string>();
            }
            _pendingMemories[npcName].Add(memory);
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
    }
}