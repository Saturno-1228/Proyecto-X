using System.Collections.Generic;
using StardewLivingValley.Models;

namespace StardewLivingValley.Services
{
    public class MemoryService
    {
        private Dictionary<string, UserProfile> _userProfiles = new Dictionary<string, UserProfile>();

        public UserProfile GetUserProfile(string npcName)
        {
            if (!_userProfiles.ContainsKey(npcName))
                _userProfiles[npcName] = new UserProfile { PlayerName = StardewValley.Game1.player?.Name ?? "Granjero" };
            return _userProfiles[npcName];
        }

        public string[] GetActiveMemories(string npcName)
        {
            // Para la Fase 2, mantenemos la memoria persistente vacía.
            // Esto se llenará en la Fase 3 con el almacenamiento del juego.
            return new string[0]; 
        }
    }
}
