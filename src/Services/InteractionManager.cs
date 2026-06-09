using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewLivingValley.UI;
using StardewLivingValley.Models;

namespace StardewLivingValley.Services
{
    public class InteractionManager
    {
        private readonly IMonitor _logger;
        private readonly VeniceApiService _veniceApi;
        private readonly MemoryService _memoryService;
        private readonly ContextBuilderService _contextBuilder;
        private readonly TopicRouterService _topicRouter;

        private CancellationTokenSource? _cts;
        private List<VeniceMessage> _sessionMessages = new List<VeniceMessage>();
        private NPC? _activeNpc;
        private NPCDialogueMenu? _activeMenu;

        public InteractionManager(IMonitor logger, VeniceApiService veniceApi, MemoryService memoryService, ContextBuilderService contextBuilder, TopicRouterService topicRouter)
        {
            _logger = logger;
            _veniceApi = veniceApi;
            _memoryService = memoryService;
            _contextBuilder = contextBuilder;
            _topicRouter = topicRouter;
        }

        public void StartInteraction(NPC npc, NPCDialogueMenu menu)
        {
            _activeNpc = npc;
            _activeMenu = menu;
            _sessionMessages.Clear();
            _logger.Log($"InteractionManager: Iniciando sesión con {npc.Name}", LogLevel.Trace);
        }

        public async void HandleChatAsync(string playerMessage)
        {
            if (_activeNpc == null || _activeMenu == null) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _cts.CancelAfter(TimeSpan.FromSeconds(30)); // Timeout de 30s

            try
            {
                var npcName = _activeNpc.Name;
                var currentSeason = Game1.currentSeason;
                var currentHearts = Game1.player.getFriendshipHeartLevelForNPC(npcName);

                // 1. Obtener Perfil Estático
                var profile = _topicRouter.GetStaticProfile(npcName);
                string staticPrompt = profile != null ? _contextBuilder.BuildStaticSystemPrompt(profile) : "ERES UN ALDEANO DE STARDEW VALLEY.";

                // 2. Obtener Lore Dinámico (KRS)
                string[] dynamicLore = _topicRouter.GetRelevantLoreChunks(npcName, playerMessage, currentHearts, currentSeason);

                // 3. Obtener Estado del Mundo
                var envState = new EnvironmentState
                {
                    Weather = Game1.isRaining ? "Lloviendo" : "Despejado",
                    TimeOfDay = Game1.timeOfDay.ToString(),
                    CurrentLocation = Game1.player.currentLocation.Name,
                    FriendshipHearts = currentHearts,
                    HeldItem = Game1.player.ActiveObject?.DisplayName ?? "Ninguno"
                };

                // 4. Obtener Perfil del Jugador
                var userProfile = _memoryService.GetUserProfile(npcName);
                var activeMemories = _memoryService.GetActiveMemories(npcName);

                string dynamicContext = _contextBuilder.BuildDynamicSystemContext(envState, userProfile, dynamicLore, activeMemories);

                // 5. Historial de Chat (TakeLast 10 para evitar sobrecarga)
                var chatHistory = _sessionMessages.TakeLast(10).ToList();

                // Log Debug
                _logger.Log($"Enviando petición a Venice API. Static:{staticPrompt.Length}c, Lore Chunks:{dynamicLore.Length}", LogLevel.Trace);

                string response = await _veniceApi.GenerateResponseAsync(
                    staticPrompt,
                    dynamicContext,
                    chatHistory,
                    playerMessage,
                    "kimi-k2-5", // Usamos el modelo rápido de chat
                    $"stardew_{npcName}", // Cache key para el static prompt
                    _cts.Token
                );

                string cleanResponse = string.IsNullOrWhiteSpace(response) ? "..." : response;

                // 6. Guardar en memoria de sesión
                _sessionMessages.Add(new VeniceMessage { Role = "user", Content = playerMessage });
                _sessionMessages.Add(new VeniceMessage { Role = "assistant", Content = cleanResponse });

                // 7. Enviar a UI
                if (Game1.activeClickableMenu == _activeMenu)
                {
                    _activeMenu.ReceiveAiResponse(cleanResponse);
                }
            }
            catch (TaskCanceledException)
            {
                _logger.Log("Timeout de Venice API.", LogLevel.Warn);
                if (Game1.activeClickableMenu == _activeMenu)
                    _activeMenu.ReceiveAiResponse("*Parece distraída y no responde...*");
            }
            catch (Exception ex)
            {
                _logger.Log($"Error interno de IA: {ex.Message}", LogLevel.Error);
                if (Game1.activeClickableMenu == _activeMenu)
                    _activeMenu.ReceiveAiResponse("... (Me duele la cabeza, no puedo pensar)");
            }
        }
        
        public void EndInteraction()
        {
            _activeNpc = null;
            _activeMenu = null;
        }
    }
}
