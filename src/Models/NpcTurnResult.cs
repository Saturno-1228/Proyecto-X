using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StardewLivingValley.Models
{
    public class NpcTurnResult
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;

        [JsonPropertyName("memories_learned")]
        public List<string> MemoriesLearned { get; set; } = new List<string>();

        [JsonPropertyName("emotion_deltas")]
        public EmotionDeltas? EmotionDeltas { get; set; }
    }
}
