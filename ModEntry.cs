using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using LivingCompanionsValley.Services;
using LivingCompanionsValley.Configuration;
using StardewValley;
using Microsoft.Xna.Framework.Graphics; // ¡Faltaba esta referencia!

namespace LivingCompanionsValley
{
    public class ModEntry : Mod
    {
        internal static IMonitor? Logger { get; private set; }
        private InteractionManager? _interactionManager;
        private MemoryService? _memoryService;
        private ModConfig? _config;

        public override void Entry(IModHelper helper)
        {
            Logger = this.Monitor;
            _config = helper.ReadConfig<ModConfig>();

            if (string.IsNullOrWhiteSpace(_config.VeniceApiKey) || _config.VeniceApiKey == "INGRESA_TU_API_KEY_AQUI")
            {
                Logger.Log("ADVERTENCIA: No has configurado tu Venice API Key.", LogLevel.Warn);
            }

            var veniceApi = new VeniceApiService(_config.VeniceApiKey, Logger!);
            _memoryService = new MemoryService(helper, Logger!);
            var contextBuilder = new ContextBuilderService();
            var topicRouter = new TopicRouterService(helper, Logger!);
            
            _interactionManager = new InteractionManager(helper, Logger!, veniceApi, _memoryService, contextBuilder, topicRouter);

            Logger!.Log("Living Companions Valley v2.0 (Dual-Model) inicializado correctamente.", LogLevel.Info);

            helper.Events.GameLoop.DayStarted += OnDayStarted;
            
            // --- ESTO ES LO QUE FALTA EN GITHUB ---
            helper.Events.Content.AssetRequested += OnAssetRequested;
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            foreach (var npc in Utility.getAllCharacters())
            {
                if (npc.IsVillager)
                {
                    _memoryService?.ProcessDailyDecay(npc.Name);
                }
            }
            Logger?.Log("Decaimiento diario procesado.", LogLevel.Trace);
        }

        // --- ESTE MÉTODO TAMBIÉN FALTA EN GITHUB ---
        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.StartsWith("Portraits/"))
            {
                string npcName = e.NameWithoutLocale.Name.Split('/')[1];
                string customPortraitPath = $"assets/Portraits/{npcName}_LCV.png";

                if (this.Helper.ModContent.DoesAssetExist<Texture2D>(customPortraitPath))
                {
                    e.LoadFromModFile<Texture2D>(customPortraitPath, AssetLoadPriority.Medium);
                    Logger?.Log($"¡Retrato LCV de {npcName} inyectado con éxito!", LogLevel.Trace);
                }
            }
        }
    }
}