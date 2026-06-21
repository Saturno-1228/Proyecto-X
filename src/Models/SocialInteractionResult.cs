using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StardewLivingValley.Models
{
    public class SocialInteractionResult
    {
        [JsonPropertyName("script")]
        public List<ConversationLine> Script { get; set; } = new List<ConversationLine>();

        [JsonPropertyName("memories_npcA")]
        public List<string> MemoriesNpcA { get; set; } = new List<string>();

        [JsonPropertyName("memories_npcB")]
        public List<string> MemoriesNpcB { get; set; } = new List<string>();

        [JsonPropertyName("emotion_deltas")]
        public EmotionDeltas? EmotionDeltas { get; set; }
    }

    public class ConversationLine
    {
        [JsonPropertyName("speaker")]
        public string Speaker { get; set; } = "";

        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }

    /// <summary>
    /// Cambios emocionales que la IA propone tras una charla.
    /// Rango esperado: -5 a +5 por eje.
    /// </summary>
    public class EmotionDeltas
    {
        [JsonPropertyName("friendship")]
        public int Friendship { get; set; } = 0;

        [JsonPropertyName("trust")]
        public int Trust { get; set; } = 0;

        [JsonPropertyName("anger")]
        public int Anger { get; set; } = 0;

        [JsonPropertyName("awkwardness")]
        public int Awkwardness { get; set; } = 0;
    }
}
