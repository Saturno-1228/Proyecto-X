using StardewModdingAPI;

namespace StardewLivingValley.Configuration
{
    public class ModConfig
    {
        public string VeniceApiKey { get; set; } = "INGRESA_TU_API_KEY_AQUI";
        public string ChatModel { get; set; } = "venice-uncensored-1-2"; 
        public string ThinkingModel { get; set; } = "zai-org-glm-5";

        public SButton AIChatKey { get; set; } = SButton.Tab;
        public SButton ConfigMenuKey { get; set; } = SButton.F8;
        public bool InterceptVanillaDialogue { get; set; } = false;
        public string OutputLanguage { get; set; } = "Español";
    }
}
