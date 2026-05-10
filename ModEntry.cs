using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using LivingCompanionsValley.Services;
using LivingCompanionsValley.Configuration;
using StardewValley;

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
            
            // 1. Cargar la configuración. SMAPI crea config.json mágicamente si no existe.
            _config = helper.ReadConfig<ModConfig>();

            if (string.IsNullOrWhiteSpace(_config.VeniceApiKey) || _config.VeniceApiKey == "INGRESA_TU_API_KEY_AQUI")
            {
                Logger.Log("ADVERTENCIA: No has configurado tu Venice API Key. Revisa el archivo config.json en la carpeta del mod.", LogLevel.Warn);
            }

            // 2. Inicializar el enjambre de servicios de IA
            var veniceApi = new VeniceApiService(_config.VeniceApiKey, Logger!);
            _memoryService = new MemoryService(helper, Logger!);
            var contextBuilder = new ContextBuilderService();
            var topicRouter = new TopicRouterService(helper, Logger!);
            
            // 3. Arrancar el orquestador (InteractionManager automáticamente se suscribe a los botones de la UI)
            _interactionManager = new InteractionManager(helper, Logger!, veniceApi, _memoryService, contextBuilder, topicRouter);

            Logger!.Log("Living Companions Valley v2.0 (Dual-Model) inicializado correctamente.", LogLevel.Info);

            // 4. Suscribir a los eventos base que controla Entry
            helper.Events.GameLoop.DayStarted += OnDayStarted;
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            // Ejecutar el decaimiento de Ebbinghaus al inicio de cada día
            // Ejecutar el decaimiento de Ebbinghaus al inicio de cada día para todos los aldeanos
            foreach (var npc in Utility.getAllCharacters())
            {
                if (npc.IsVillager)
                {
                    _memoryService?.ProcessDailyDecay(npc.Name);
                }
            }
            Logger?.Log("Decaimiento diario de memoria (Ebbinghaus) procesado para todos los NPCs.", LogLevel.Trace);
        }
    }
}
