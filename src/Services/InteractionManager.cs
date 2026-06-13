using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
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
        private readonly ObservationEngine _observationEngine;

        private CancellationTokenSource? _cts;
        private List<VeniceMessage> _sessionMessages = new List<VeniceMessage>();
        private Dictionary<string, (int Day, int Time)> _lastGiftTimeByNpc = new Dictionary<string, (int Day, int Time)>();
        private NPC? _activeNpc;
        private NPCDialogueMenu? _activeMenu;

        public InteractionManager(IMonitor logger, VeniceApiService veniceApi, MemoryService memoryService, ContextBuilderService contextBuilder, TopicRouterService topicRouter, ObservationEngine observationEngine)
        {
            _logger = logger;
            _veniceApi = veniceApi;
            _memoryService = memoryService;
            _contextBuilder = contextBuilder;
            _topicRouter = topicRouter;
            _observationEngine = observationEngine;
        }

        public void StartInteraction(NPC npc, NPCDialogueMenu menu, string initialDialogue)
        {
            _activeNpc = npc;
            _activeMenu = menu;
            _sessionMessages.Clear();
            
            // Inyectar el diálogo Vanilla inicial como memoria de la IA
            if (!string.IsNullOrWhiteSpace(initialDialogue))
            {
                _sessionMessages.Add(new VeniceMessage { Role = "assistant", Content = initialDialogue });
            }

            _logger.Log($"InteractionManager: Iniciando sesión con {npc.Name}. Vanilla seed: {initialDialogue}", LogLevel.Trace);
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

                string relationshipStatus = "Desconocido";
                string relationshipRule = "";

                if (Game1.player.friendshipData.TryGetValue(npcName, out var friendship))
                {
                    if (friendship.IsMarried())
                    {
                        relationshipStatus = "Esposo(a)";
                        relationshipRule = "Estás casado(a) con el jugador. Trátalo(a) con muchísimo amor, devoción y calidez. ¡Son esposos!";
                    }
                    else if (friendship.IsDating())
                    {
                        relationshipStatus = "Novio(a)";
                        relationshipRule = "Están saliendo románticamente. Usa un tono coqueto, dulce y cariñoso.";
                    }
                    else if (currentHearts >= 8)
                    {
                        relationshipStatus = "Mejor Amigo(a)";
                        relationshipRule = "Eres extremadamente cercano a esta persona. Confías en ella y eres muy abierto y cálido.";
                    }
                    else if (currentHearts >= 5)
                    {
                        relationshipStatus = "Buen Amigo(a)";
                        relationshipRule = "Le tienes cariño y confianza. Trátalo como a un buen amigo.";
                    }
                    else if (currentHearts >= 2)
                    {
                        relationshipStatus = "Conocido(a)";
                        relationshipRule = "Lo conoces, pero mantén un trato casual.";
                    }
                    else
                    {
                        relationshipStatus = "Desconocido / Recién llegado";
                        relationshipRule = "Apenas lo conoces. Mantén distancia, sé muy breve y evita cualquier lenguaje afectuoso o confianzudo.";
                    }
                }
                else
                {
                    relationshipRule = "Apenas lo conoces. Mantén distancia, sé muy breve y evita cualquier lenguaje afectuoso o confianzudo.";
                }

                // Inyectar contexto global de relaciones (poligamia/infidelidad)
                string spouse = Game1.player.spouse;
                List<string> datingOthers = new List<string>();
                
                foreach (var kvp in Game1.player.friendshipData.Pairs)
                {
                    if (kvp.Key != npcName && kvp.Value.IsDating())
                    {
                        datingOthers.Add(kvp.Key);
                    }
                }

                string dramaContext = "";
                if (!string.IsNullOrEmpty(spouse) && spouse != npcName)
                {
                    dramaContext += $" OJO: El jugador ESTÁ CASADO con {spouse} actualmente.";
                }
                if (datingOthers.Count > 0)
                {
                    dramaContext += $" OJO: El jugador también es novio(a) de: {string.Join(", ", datingOthers)}.";
                }

                if (!string.IsNullOrEmpty(dramaContext))
                {
                    relationshipRule += " " + dramaContext.Trim() + " Ten esto en cuenta y actúa según tu personalidad (celos, secreto, indiferencia, etc).";
                }

                // Evaluar cooldown de 4 horas
                bool isCooldownActive = false;
                if (_lastGiftTimeByNpc.TryGetValue(npcName, out var lastGift))
                {
                    if (Game1.Date.TotalDays == lastGift.Day && (Game1.timeOfDay - lastGift.Time) < 400)
                    {
                        isCooldownActive = true;
                    }
                }

                // 3. Obtener Estado del Mundo
                var envState = new EnvironmentState
                {
                    Weather = Game1.isRaining ? "Lloviendo" : "Despejado",
                    TimeOfDay = Game1.timeOfDay.ToString(),
                    CurrentLocation = Game1.player.currentLocation.Name,
                    FriendshipHearts = currentHearts,
                    HeldItem = Game1.player.ActiveObject?.DisplayName ?? "Ninguno",
                    RelationshipStatus = relationshipStatus,
                    RelationshipRule = relationshipRule,
                    IsGiftCooldownActive = isCooldownActive
                };

                // 4. Obtener Perfil del Jugador
                var userProfile = _memoryService.GetUserProfile(npcName);
                var activeMemories = _memoryService.GetActiveMemories(npcName);

                string dynamicContext = _contextBuilder.BuildDynamicSystemContext(envState, userProfile, dynamicLore, activeMemories);

                // Inyectar el Cerebro Sensorial y Profesional
                string brainContext = _observationEngine.GetObservationContext(_activeNpc);
                if (!string.IsNullOrEmpty(brainContext))
                {
                    dynamicContext += "\n" + brainContext;
                }

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

                // Interceptar comando de regalo
                var giveItemMatch = Regex.Match(cleanResponse, @"\[give_item:(.*?)\]", RegexOptions.IgnoreCase);
                if (giveItemMatch.Success)
                {
                    string itemName = giveItemMatch.Groups[1].Value.Trim();
                    cleanResponse = cleanResponse.Replace(giveItemMatch.Value, "").Trim();
                    
                    if (profile != null && profile.AllowedGifts != null && profile.AllowedGifts.TryGetValue(itemName, out string itemId))
                    {
                        if (!isCooldownActive)
                        {
                            try 
                            {
                                Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create(itemId));
                                _lastGiftTimeByNpc[npcName] = (Game1.Date.TotalDays, Game1.timeOfDay);
                                _logger.Log($"[{npcName}] regaló {itemName} ({itemId}) al jugador.", LogLevel.Info);
                            }
                            catch (Exception e)
                            {
                                _logger.Log($"Error entregando objeto {itemId}: {e.Message}", LogLevel.Error);
                            }
                        }
                        else
                        {
                            _logger.Log($"[{npcName}] intentó regalar {itemName} pero está en cooldown de 4 horas.", LogLevel.Warn);
                        }
                    }
                    else
                    {
                        _logger.Log($"[{npcName}] intentó regalar '{itemName}' pero no está en su AllowedGifts.", LogLevel.Warn);
                    }
                }

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
