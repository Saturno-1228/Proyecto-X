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
using StardewLivingValley.Configuration;

namespace StardewLivingValley.Brain
{
    public class Consciousness
    {
        private readonly IMonitor _logger;
        private readonly NeuralLink _veniceApi;
        private readonly Hippocampus _Hippocampus;
        private readonly Subconscious _contextBuilder;
        private readonly KnowledgeCortex _topicRouter;
        private readonly SensoryCortex _SensoryCortex;
        private readonly Cerebellum _actionController;
        private readonly ModConfig _config;

        private CancellationTokenSource? _cts;
        private List<VeniceMessage> _sessionMessages = new List<VeniceMessage>();
        private List<VeniceMessage> _savedSessionMessages = new List<VeniceMessage>();
        private Dictionary<string, (int Day, int Time)> _lastGiftTimeByNpc = new Dictionary<string, (int Day, int Time)>();
        private NPC? _activeNpc;
        private NPCDialogueMenu? _activeMenu;
        private Action<NPC, string>? _reopenDialogueCallback;
        private Action? _onInteractionEnded;
        private bool _isPostInspection = false;

        public void RegisterReopenCallback(Action<NPC, string> callback)
        {
            _reopenDialogueCallback = callback;
        }

        public Consciousness(IMonitor logger, NeuralLink veniceApi, Hippocampus Hippocampus, Subconscious contextBuilder, KnowledgeCortex topicRouter, SensoryCortex SensoryCortex, Cerebellum actionController, ModConfig config)
        {
            _logger = logger;
            _veniceApi = veniceApi;
            _Hippocampus = Hippocampus;
            _contextBuilder = contextBuilder;
            _topicRouter = topicRouter;
            _SensoryCortex = SensoryCortex;
            _actionController = actionController;
            _config = config;
        }

        public void StartInteraction(NPC npc, NPCDialogueMenu menu, string initialDialogue)
        {
            _activeNpc = npc;
            _activeMenu = menu;
            
            // Si es post-inspección, restaurar el historial de la conversación original
            if (_isPostInspection && _savedSessionMessages.Count > 0)
            {
                _sessionMessages = new List<VeniceMessage>(_savedSessionMessages);
                _isPostInspection = false;
                _savedSessionMessages.Clear();
                _logger.Log($"[Consciousness] Historial de conversación restaurado ({_sessionMessages.Count} mensajes).", LogLevel.Info);
            }
            else
            {
                _sessionMessages.Clear();
            }
            
            // Inyectar el diálogo Vanilla inicial como memoria de la IA
            if (!string.IsNullOrWhiteSpace(initialDialogue))
            {
                _sessionMessages.Add(new VeniceMessage { Role = "assistant", Content = initialDialogue });
            }

            _logger.Log($"Consciousness: Iniciando sesión con {npc.Name}. Vanilla seed: {initialDialogue}", LogLevel.Trace);
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

                // 1. Obtener Perfil (XML)
                var profile = _topicRouter.GetDynamicProfile(npcName, currentHearts);
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

                // El contexto de rumores/infidelidad cruzada fue eliminado para mantener la inmersión (Restricción de Conocimiento).
                // Los NPCs solo sabrán si el jugador sale con otras personas si lo observan físicamente o se enteran mediante un sistema de chismes futuro.

                // Evaluar cooldown de 4 horas
                bool isCooldownActive = false;
                if (_lastGiftTimeByNpc.TryGetValue(npcName, out var lastGift))
                {
                    if (Game1.Date.TotalDays == lastGift.Day && (Game1.timeOfDay - lastGift.Time) < 400)
                    {
                        isCooldownActive = true;
                    }
                }

                // Obtener datos de tiempo y agenda para el contexto
                string timeConstraintRule = "";
                string scheduleString = "";
                string currentDate = $"Día {Game1.dayOfMonth} de {Game1.currentSeason}, Año {Game1.year}";

                string FormatStardewTime(int time)
                {
                    int hours = time / 100;
                    int minutes = time % 100;
                    string amPm = hours < 12 || hours >= 24 ? "AM" : "PM";
                    int displayHours = hours % 12;
                    if (displayHours == 0) displayHours = 12;
                    return $"{displayHours}:{minutes:D2} {amPm}";
                }

                string formattedCurrentTime = FormatStardewTime(Game1.timeOfDay);

                if (_activeNpc.Schedule != null && _activeNpc.Schedule.Count > 0)
                {
                    var sbSchedule = new System.Text.StringBuilder();
                    int nextActivityTime = 2600;
                    foreach (var key in Enumerable.OrderBy(_activeNpc.Schedule.Keys, k => k))
                    {
                        var pathDesc = _activeNpc.Schedule[key];
                        string behavior = !string.IsNullOrEmpty(pathDesc.endOfRouteBehavior) ? $" (Haciendo: {pathDesc.endOfRouteBehavior})" : "";
                        string target = !string.IsNullOrEmpty(pathDesc.targetLocationName) ? pathDesc.targetLocationName : "Otro lugar";
                        sbSchedule.AppendLine($"- A las {FormatStardewTime(key)}: Ir a {target}{behavior}");
                        if (key > Game1.timeOfDay && key < nextActivityTime)
                        {
                            nextActivityTime = key;
                        }
                    }
                    scheduleString = sbSchedule.ToString();

                    if (nextActivityTime < 2600)
                    {
                        timeConstraintRule = $"Tu próxima actividad programada en tu agenda es a las {FormatStardewTime(nextActivityTime)} (la hora actual es {formattedCurrentTime}). REGLA ESTRICTA DE TIEMPO: Moverte hacia otro lugar caminando de ida y vuelta te tomará un mínimo de 40 a 60 minutos de tiempo del juego. Evalúa mentalmente si tienes el tiempo suficiente para ir a donde te pidan y volver sin faltar a tu cita de las {FormatStardewTime(nextActivityTime)} (excepto si es urgente, o si es algo vital en tu personalidad ignorarla). Si sientes que no te dará tiempo de ir y regresar (por ejemplo, si te piden ir a la Granja y tu evento es en 30 minutos), debes decirle al jugador creativamente que no puedes ir porque se te hace tarde y tienes cosas que hacer, o que podrías ir pero tendrías que apresurarte y podrías llegar tarde.";
                    }
                }

                // 3. Obtener Estado del Mundo
                var envState = new EnvironmentState
                {
                    Weather = Game1.isRaining ? "Lloviendo" : "Despejado",
                    TimeOfDay = formattedCurrentTime,
                    CurrentDate = currentDate,
                    CurrentLocation = Game1.player.currentLocation.Name,
                    FriendshipHearts = currentHearts,
                    HeldItem = Game1.player.ActiveObject?.DisplayName ?? "Ninguno",
                    RelationshipStatus = relationshipStatus,
                    RelationshipRule = relationshipRule,
                    IsGiftCooldownActive = isCooldownActive,
                    TimeConstraintRule = timeConstraintRule,
                    DailySchedule = scheduleString
                };

                // 4. Obtener Perfil del Jugador
                var userProfile = _Hippocampus.GetUserProfile(npcName);
                var activeMemories = _Hippocampus.GetActiveMemories(npcName);
                var longTermMemories = _Hippocampus.GetLongTermMemories(npcName);
                
                var allMemories = new System.Collections.Generic.List<string>(longTermMemories);
                allMemories.AddRange(activeMemories);

                string dynamicContext = _contextBuilder.BuildDynamicSystemContext(envState, userProfile, dynamicLore, allMemories.ToArray());

                // Inyectar el Cerebro Sensorial y Profesional
                string brainContext = _SensoryCortex.GetObservationContext(_activeNpc);
                if (!string.IsNullOrEmpty(brainContext))
                {
                    dynamicContext += "\n" + brainContext;
                }

                // Inyectar Directiva de Idioma y Formato
                string outputLanguage = string.IsNullOrWhiteSpace(_config.OutputLanguage) ? "Español" : _config.OutputLanguage;
                dynamicContext += $"\n\nIMPORTANT FINAL DIRECTIVE: Regardless of any previous instructions, your final response MUST be written exclusively in {outputLanguage}. If you mention any item names provided in the context, do NOT translate them, use the exact words provided in the context.";

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

                // LOG PARA DEPUBACIÓN DEL USUARIO (SMAPI CONSOLE)
                _logger.Log($"[Venice AI Response Raw] {npcName}: {cleanResponse}", LogLevel.Debug);

                string cleanedJson = cleanResponse.Trim();
                if (cleanedJson.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) cleanedJson = cleanedJson.Substring(7);
                if (cleanedJson.StartsWith("```")) cleanedJson = cleanedJson.Substring(3);
                if (cleanedJson.EndsWith("```")) cleanedJson = cleanedJson.Substring(0, cleanedJson.Length - 3);
                cleanedJson = cleanedJson.Trim();

                VeniceResponse? parsedResponse = null;
                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions { 
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };
                    parsedResponse = System.Text.Json.JsonSerializer.Deserialize<VeniceResponse>(cleanedJson, options);
                }
                catch (Exception ex)
                {
                    _logger.Log($"[Consciousness] Falló el parseo JSON. Activando Fallback Anti-Crasheo. Error: {ex.Message}", LogLevel.Warn);
                }

                if (parsedResponse == null || string.IsNullOrWhiteSpace(parsedResponse.VisibleText))
                {
                    parsedResponse = new VeniceResponse
                    {
                        Emotion = 0,
                        VisibleText = cleanResponse,
                        ClaimLevel = "none"
                    };
                }

                cleanResponse = $"[{parsedResponse.Emotion}] {parsedResponse.VisibleText}";
                
                string actionType = parsedResponse.Action?.Type ?? "";
                string actionTarget = parsedResponse.Action?.Target ?? "";

                // Interceptar comando de regalo
                if (actionType.Equals("give_item", StringComparison.OrdinalIgnoreCase))
                {
                    string itemName = actionTarget.Trim();
                    
                    if (profile != null && profile.AllowedGifts != null && profile.AllowedGifts.TryGetValue(itemName, out string? itemId))
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

                // Interceptar comando de movimiento
                else if (actionType.Equals("go_to", StringComparison.OrdinalIgnoreCase))
                {
                    string targetLocation = actionTarget.Trim();
                    
                    NPC npcToInspect = _activeNpc;
                    
                    // Guardar historial de conversación para que Emily recuerde qué le pidieron
                    _savedSessionMessages = new List<VeniceMessage>(_sessionMessages);
                    // Añadir el mensaje actual y la respuesta al historial guardado
                    _savedSessionMessages.Add(new VeniceMessage { Role = "user", Content = playerMessage });
                    _savedSessionMessages.Add(new VeniceMessage { Role = "assistant", Content = cleanResponse });
                    
                    _onInteractionEnded = () => {
                        if (npcToInspect != null)
                        {
                            npcToInspect.CurrentDialogue.Clear();
                            npcToInspect.movementPause = 0;

                            _actionController.StartInspection(npcToInspect, targetLocation, 
                                (reportData) => {
                                    _logger.Log($"[Consciousness] Datos de inspección recibidos: {reportData}", LogLevel.Trace);
                                },
                                () => {
                                _logger.Log($"[Consciousness] Callback de inspección. NPC: {npcToInspect.Name}, Map NPC: {npcToInspect.currentLocation?.NameOrUniqueName}, Map Player: {Game1.player.currentLocation?.NameOrUniqueName}", LogLevel.Info);
                                
                                if (npcToInspect.currentLocation == null || Game1.player.currentLocation == null)
                                {
                                    _logger.Log($"[Consciousness] Error: Ubicación actual del NPC o del jugador es nula.", LogLevel.Error);
                                    return;
                                }

                                float distance = Vector2.Distance(Game1.player.Tile, npcToInspect.Tile);
                                _logger.Log($"[Consciousness] Distancia al jugador: {distance} tiles. Mismo mapa: {Game1.player.currentLocation.NameOrUniqueName == npcToInspect.currentLocation.NameOrUniqueName}", LogLevel.Info);

                                if (Game1.player.currentLocation.NameOrUniqueName == npcToInspect.currentLocation.NameOrUniqueName && distance <= 15f)
                                {
                                    _logger.Log($"[Consciousness] Reabriendo diálogo con {npcToInspect.Name}.", LogLevel.Info);
                                    _isPostInspection = true;
                                    
                                    // Construir el reporte de inspección con datos REALES
                                    string inspectionReport = _actionController.GetLastInspectionReport();
                                    string reopenMessage = !string.IsNullOrEmpty(inspectionReport) 
                                        ? inspectionReport
                                        : "*Te observa esperando a que le cuentes lo que viste*";
                                    
                                    // Guardarlo en la memoria a largo plazo
                                    _Hippocampus.SaveNpcMemory(npcToInspect.Name, $"Fui a revisar {targetLocation} y te di este reporte: {reopenMessage}");

                                    _reopenDialogueCallback?.Invoke(npcToInspect, reopenMessage);
                                }
                                else
                                {
                                    _logger.Log($"[Consciousness] No se reabre el diálogo. Distancia > 15 ({distance}) o mapas diferentes.", LogLevel.Warn);
                                }
                            });
                        }
                    };
                    
                    // No hacemos return; dejamos que el código pase al paso 6 y 7
                    // para que el jugador pueda leer la respuesta. La misión iniciará
                    // cuando el menú se cierre automáticamente.
                    
                    // Activar auto-cierre: páginas avanzan solas + cierre 2s después de terminar
                    if (_activeMenu != null)
                    {
                        _activeMenu.SetAutoClose();
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
                if (_activeMenu != null && Game1.activeClickableMenu == _activeMenu)
                    _activeMenu.ReceiveAiResponse("... (Me duele la cabeza, no puedo pensar)");
            }
        }
        
        public void EndInteraction()
        {
            _activeNpc = null;
            _activeMenu = null;
            
            _onInteractionEnded?.Invoke();
            _onInteractionEnded = null;
        }
    }
}
