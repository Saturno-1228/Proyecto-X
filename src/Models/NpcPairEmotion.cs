using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StardewLivingValley.Models
{
    /// <summary>
    /// Representa el estado emocional entre un par de NPCs.
    /// Los ejes van de 0 a 100. Los valores por defecto representan una relación neutral entre vecinos.
    /// </summary>
    public class NpcPairEmotion
    {
        [JsonPropertyName("family_tie")]
        public string FamilyTie { get; set; } = "";

        [JsonPropertyName("friendship")]
        public int Friendship { get; set; } = 50;

        [JsonPropertyName("trust")]
        public int Trust { get; set; } = 50;

        [JsonPropertyName("anger")]
        public int Anger { get; set; } = 0;

        [JsonPropertyName("awkwardness")]
        public int Awkwardness { get; set; } = 0;

        [JsonPropertyName("familiarity")]
        public int Familiarity { get; set; } = 0;

        [JsonPropertyName("last_interaction_day")]
        public int LastInteractionDay { get; set; } = 0;
    }
}
