namespace LivingCompanionsValley.Configuration
{
    public class ModConfig
    {
        public string VeniceApiKey { get; set; } = "INGRESA_TU_API_KEY_AQUI";
        public StardewModdingAPI.SButton ActivationKey { get; set; } = StardewModdingAPI.SButton.Tab;
        
        /// <summary>
        /// Activa el parche interno de Harmony para escalar retratos HD en el juego base.
        /// Apágalo si usas un mod externo de retratos HD como Portraiture.
        /// </summary>
        public bool EnableBuiltInHdPortraits { get; set; } = true;
    }
}
