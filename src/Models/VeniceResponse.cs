using System.Text.Json.Serialization;

namespace StardewLivingValley.Models
{
    public class RequestedAction
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("target")]
        public string Target { get; set; } = string.Empty;
    }

    public class VeniceResponse
    {
        [JsonPropertyName("emotion")]
        public int Emotion { get; set; } = 0;

        [JsonPropertyName("visible_text")]
        public string VisibleText { get; set; } = string.Empty;

        [JsonPropertyName("requested_action")]
        public RequestedAction? Action { get; set; }

        [JsonPropertyName("claim_level")]
        public string ClaimLevel { get; set; } = "none";

        [JsonPropertyName("evidence_source")]
        public string EvidenceSource { get; set; } = string.Empty;
    }
}
