using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using LivingCompanionsValley.UI;
using LivingCompanionsValley.Models;
using System.Text.RegularExpressions;

namespace LivingCompanionsValley.Services
{
    public class InteractionManager
    {
        private readonly IModHelper _helper;
        private readonly IMonitor _logger;
        private readonly VeniceApiService _veniceApi;
        private readonly MemoryService _memoryService;
        private readonly ContextBuilderService _contextBuilder;
        private readonly TopicRouterService _topicRouter;

        private SButton _activationKey = SButton.Tab;
        private NPC? _activeNpc;
        private AiDialogueMenu? _activeMenu;
        private CancellationTokenSource? _cts;
        private List<VeniceMessage> _sessionMessages = new List<VeniceMessage>();

        private string _currentSessionChatHistory = "";

        // Manejo de conexión y modo Offline
        private bool _isOfflineCooldownActive = false;
        private DateTime _offlineCooldownExpiration;
        private DateTime _lastInteractionTime = DateTime.MinValue;

        // Cache de optimización Zero-I/O
        private Dictionary<string, string> _identityCache = new Dictionary<string, string>();
        private HashSet<string> _supportedNpcs = new HashSet<string>();
        private bool _cacheInitialized = false;

        private void EnsureCacheLoaded()
        {
            if (_cacheInitialized) return;
            string lorePath = Path.Combine(_helper.DirectoryPath, "Assets", "Lore");
            if (Directory.Exists(lorePath))
            {
                var files = Directory.GetFiles(lorePath, "*.xml");
                foreach (var file in files)
                {
                    _supportedNpcs.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            _cacheInitialized = true;
        }

        private string GetStaticIdentity(string npcName)
        {
            var sanitizedName = System.Text.RegularExpressions.Regex.Replace(npcName, @"[^a-zA-Z0-9_\.\-]", "_");
            if (_identityCache.TryGetValue(sanitizedName, out var identity))
                return identity;
            
            string xmlPath = Path.Combine(_helper.DirectoryPath, "Assets", "Lore", $"{sanitizedName}.xml");
            identity = File.Exists(xmlPath) ? File.ReadAllText(xmlPath) : $"<Identidad><nombre>{sanitizedName}</nombre></Identidad>";
            _identityCache[sanitizedName] = identity;
            return identity;
        }

        public InteractionManager(IModHelper helper, IMonitor logger, VeniceApiService veniceApi, MemoryService memoryService, ContextBuilderService contextBuilder, TopicRouterService topicRouter)
        {
            _helper = helper;
            _logger = logger;
            _veniceApi = veniceApi;
            _memoryService = memoryService;
            _contextBuilder = contextBuilder;
            _topicRouter = topicRouter;

            // Escuchar eventos
            _helper.Events.Input.ButtonPressed += OnButtonPressed;
            _helper.Events.Display.MenuChanged += OnMenuChanged;
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (e.Button == _activationKey && Game1.activeClickableMenu == null && Context.IsPlayerFree)
            {
                EnsureCacheLoaded();
                var playerTile = Game1.player.Tile;
                _activeNpc = Game1.player.currentLocation.characters
                             .FirstOrDefault(n => Vector2.Distance(n.Tile, playerTile) <= 3f && n.IsVillager);

                if (_activeNpc != null)
                {
                    // Consulta RAM instantánea en lugar de File.Exists
                    if (_supportedNpcs.Contains(_activeNpc.Name))
                    {
                        if (_isOfflineCooldownActive && DateTime.Now < _offlineCooldownExpiration)
                        {
                            // Si estamos en cooldown por falta de internet, forzamos diálogo vanilla
                            _activeNpc.checkAction(Game1.player, Game1.player.currentLocation);
                        }
                        else
                        {
                            StartInteraction(_activeNpc);
                        }
                    }
                }
            }
        }

        private void StartInteraction(NPC npc, string? initialVanillaMessage = null)
        {
            _activeNpc = npc;
            _currentSessionChatHistory = "";
            _sessionMessages.Clear();
            _lastInteractionTime = DateTime.Now;

            _logger.Log($"Iniciando interacción con {npc.Name}", LogLevel.Debug);

            // 1. Congelación Temporal Segura (FreezeMotion via Reflection)
            if (npc.CurrentDialogue.Count == 0 && npc.movementPause <= 0)
            {
                npc.Halt();
                npc.faceGeneralDirection(Game1.player.Position);
                _helper.Reflection.GetField<bool>(npc, "freezeMotion").SetValue(true);
            }

            // 2. Instanciar la Interfaz Unificada
            _activeMenu = new AiDialogueMenu(npc, OnMessageSubmitted);
            Game1.activeClickableMenu = _activeMenu;

            // 3. Si interceptamos un diálogo nativo, inyectarlo como mensaje inicial
            if (!string.IsNullOrEmpty(initialVanillaMessage))
            {
                _currentSessionChatHistory += $"{npc.Name}: {initialVanillaMessage}\n";
                _sessionMessages.Add(new VeniceMessage { Role = "assistant", Content = initialVanillaMessage });
                _activeMenu.ReceiveAiResponse(initialVanillaMessage);
            }
        }

        private async void OnMessageSubmitted(string playerMessage)
        {
            var npc = _activeNpc;
            if (npc == null) return;

            _currentSessionChatHistory += $"Jugador: {playerMessage}\n";

            // Safeguard: Cancelar peticiones anteriores si hay spam
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _cts.CancelAfter(TimeSpan.FromSeconds(45)); // Timeout de 45 segundos para chat en vivo

            try
            {
                var network = _memoryService.GetMemoryNetwork(npc.Name);
                
                // Asegurarnos de que la IA sepa el nombre real de nuestro granjero
                if (string.IsNullOrWhiteSpace(network.PlayerProfile.PlayerName))
                {
                    network.PlayerProfile.PlayerName = Game1.player.Name;
                    // No hace falta guardarlo aquí (SaveMemoryNetwork), se guardará en la fase de consolidación
                }

                var activeMems = network.ActiveMemories.Select(m => m.Content).ToArray();
                
                // Cargar la Identidad XML Estática (Desde Caché RAM)
                string xmlConfig = GetStaticIdentity(npc.Name);

                // Detectar testigos cercanos (Consulta en RAM O(1))
                var nearbyWitnesses = Game1.player.currentLocation.characters
                    .Where(c => c.Name != npc.Name && c.IsVillager && Vector2.Distance(c.Tile, Game1.player.Tile) <= 8f && _supportedNpcs.Contains(c.Name))
                    .Select(c => c.Name)
                    .ToList();

                string witnessesStr = nearbyWitnesses.Any() ? string.Join(", ", nearbyWitnesses) : "";

                // Entorno dinámico básico y avanzado (Fase 2.5)
                var envState = new EnvironmentState { 
                    Weather = Game1.isRaining ? "Lloviendo" : "Soleado", 
                    TimeOfDay = Game1.timeOfDay.ToString(),
                    CurrentLocation = Game1.player.currentLocation.Name,
                    HeldItem = Game1.player.ActiveObject?.DisplayName ?? "Ninguno",
                    NearbyWitnesses = witnessesStr,
                    FriendshipHearts = Game1.player.getFriendshipHeartLevelForNPC(npc.Name),
                    IsFirstMeeting = !Game1.player.friendshipData.ContainsKey(npc.Name)
                };

                // Inyectar estado físico vital
                if (Game1.player.health < Game1.player.maxHealth * 0.2f)
                    envState.HealthStatus = "El jugador se ve muy malherido y a punto de colapsar.";
                if (Game1.player.Stamina < Game1.player.MaxStamina * 0.15f)
                    envState.EnergyStatus = "El jugador se ve pálido y exhausto, casi sin energía.";

                // Extraer el Lore Dinámico usando el TopicRouter (KRS)
                string[] dynamicLoreChunks = _topicRouter.GetRelevantLoreChunks(npc.Name, playerMessage);

                // Ensamblar el Prompt Estratégico Dividido (Caché friendly máximo)
                string staticSystemPrompt = _contextBuilder.BuildStaticSystemPrompt(xmlConfig);
                string dynamicSystemContext = _contextBuilder.BuildDynamicSystemContext(envState, network.PlayerProfile, dynamicLoreChunks, activeMems);

                // Pasamos la memoria de corto plazo (hilo de la conversación limitado a los últimos 10 mensajes / 5 turnos)
                // Esto evita saturar el límite de tokens de contexto del modelo rápido.
                var chatHistory = _sessionMessages.TakeLast(10).ToList();

                // --- LOG TEMPORAL PARA DEPURACIÓN ---
                _logger.Log("\n=================== [DEBUG AI START] ===================", LogLevel.Info);
                _logger.Log($"[STATIC SYSTEM PROMPT ENVIADO (100% CACHE)]:\n{staticSystemPrompt}\n", LogLevel.Info);
                _logger.Log("[HISTORIAL DE CONVERSACIÓN ENVIADO (100% CACHE)]:", LogLevel.Info);
                if (chatHistory.Count == 0) _logger.Log("  (Vacío, es el primer mensaje de la sesión)", LogLevel.Info);
                foreach (var msg in chatHistory)
                {
                    _logger.Log($"  {msg.Role.ToUpper()}: {msg.Content}", LogLevel.Info);
                }
                _logger.Log($"\n[DYNAMIC SYSTEM CONTEXT ENVIADO (FLUCTÚA)]:\n{dynamicSystemContext}\n", LogLevel.Info);
                _logger.Log($"\n[MENSAJE ACTUAL DEL JUGADOR]: {playerMessage}", LogLevel.Info);
                _logger.Log("Esperando respuesta de Venice API...", LogLevel.Info);
                // ------------------------------------

                // Llamar a Venice usando el Modelo Rápido (MiniMax)
                string response = await _veniceApi.GenerateResponseAsync(
                    staticSystemPrompt,
                    dynamicSystemContext,
                    chatHistory, 
                    playerMessage, 
                    VeniceApiService.ChatModel, 
                    $"stardew_{npc.Name}", 
                    _cts?.Token ?? CancellationToken.None);

                // --- LOG TEMPORAL DE RESPUESTA ---
                _logger.Log($"\n[RESPUESTA RECIBIDA DE {npc.Name.ToUpper()}]:\n{response}", LogLevel.Info);
                _logger.Log("=================== [DEBUG AI END] ===================\n", LogLevel.Info);
                // ---------------------------------

                // Extraer la respuesta cruda, asegurando que no esté vacía
                string cleanResponse = string.IsNullOrWhiteSpace(response) ? "..." : response;

                // --- TOOL CALLING: FRIENDSHIP DELTA ---
                var match = Regex.Match(cleanResponse, @"\{""friendship_delta"":\s*(-?\d+)\}");
                if (match.Success)
                {
                    if (int.TryParse(match.Groups[1].Value, out int delta))
                    {
                        // Limitar el delta matemáticamente entre -5 y +5
                        delta = Math.Max(-5, Math.Min(5, delta));
                        Game1.player.changeFriendship(delta, npc);
                        _logger.Log($"[{npc.Name}] IA alteró amistad en {delta} puntos.", LogLevel.Info);
                    }
                    // Remover el JSON del texto visible al usuario
                    cleanResponse = cleanResponse.Replace(match.Value, "").Trim();
                }
                // --------------------------------------

                // Si se cayó el internet o hay un error de conexión, activamos el modo offline
                if (cleanResponse.Contains("[Error de conexión con la IA]") || cleanResponse.Contains("[Error interno]") || cleanResponse.Contains("[Cancelado]"))
                {
                    _logger.Log($"[{npc.Name}] Fallo en conexión detectado. Activando Fallback a Vanilla por 5 minutos.", LogLevel.Warn);
                    _isOfflineCooldownActive = true;
                    _offlineCooldownExpiration = DateTime.Now.AddMinutes(5); // 5 minutos de cooldown
                    
                    Game1.addHUDMessage(new HUDMessage("Living Companions: Sin conexión. Diálogos clásicos activados temporalmente.", 3));
                    
                    if (Game1.activeClickableMenu is AiDialogueMenu)
                    {
                        Game1.exitActiveMenu();
                    }
                    
                    // Descongelamos al NPC y abrimos su diálogo clásico
                    _helper.Reflection.GetField<bool>(npc, "freezeMotion").SetValue(false);
                    npc.checkAction(Game1.player, Game1.player.currentLocation);
                    return;
                }

                _currentSessionChatHistory += $"{npc.Name}: {cleanResponse}\n";

                // Guardamos el intercambio en la memoria de la sesión con todo y etiquetas
                _sessionMessages.Add(new VeniceMessage { Role = "user", Content = playerMessage });
                _sessionMessages.Add(new VeniceMessage { Role = "assistant", Content = cleanResponse }); 

                // Enviar la respuesta CRUDA a la UI. El menú hará la magia del parseo.
                if (Game1.activeClickableMenu is AiDialogueMenu menu)
                {
                    menu.ReceiveAiResponse(cleanResponse);
                }
            }
            catch (TaskCanceledException)
            {
                _logger.Log("Timeout de 45s alcanzado. Venice API no respondió a tiempo.", LogLevel.Warn);
                if (Game1.activeClickableMenu is AiDialogueMenu menu)
                    menu.ReceiveAiResponse("*Parece distraída y no responde...*");
            }
            catch (Exception ex)
            {
                _logger.Log($"Error interno de IA: {ex.Message}", LogLevel.Error);
                if (Game1.activeClickableMenu is AiDialogueMenu menu)
                    menu.ReceiveAiResponse("... (Me duele la cabeza, no puedo pensar)");
            }
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            // Interceptar cajas de diálogo vanilla al hacer Clic Derecho
            if (e.NewMenu is DialogueBox dialogueBox && dialogueBox.characterDialogue != null)
            {
                // Evitar doble intercepción si ya estamos interactuando o acabamos de cerrar una UI
                if (_activeMenu != null || Game1.activeClickableMenu is AiDialogueMenu || (DateTime.Now - _lastInteractionTime).TotalMilliseconds < 800)
                {
                    return;
                }

                var npc = dialogueBox.characterDialogue.speaker;
                if (npc != null && _supportedNpcs.Contains(npc.Name))
                {
                    if (_isOfflineCooldownActive && DateTime.Now < _offlineCooldownExpiration)
                    {
                        // Estamos offline, dejar que el diálogo vanilla corra normalmente
                        return;
                    }

                    // Robar el texto vanilla
                    string vanillaMessage = dialogueBox.characterDialogue.getCurrentDialogue();
                    
                    // Iniciar la interacción de IA pasando el mensaje original
                    StartInteraction(npc, vanillaMessage);
                    return;
                }
            }

            // Detectar cuando el jugador cierra nuestra UI (con ESC o click fuera)
            if (e.OldMenu is AiDialogueMenu && e.NewMenu == null && _activeNpc != null)
            {
                EndInteraction();
            }
        }

        private void EndInteraction()
        {
            var npc = _activeNpc;
            if (npc == null) return;

            // 1. Liberar al NPC para que retome su vida (Schedule)
            _helper.Reflection.GetField<bool>(npc, "freezeMotion").SetValue(false);
            
            // 2. Disparar el Sueño / Consolidación de Memorias (Background)
            if (!string.IsNullOrWhiteSpace(_currentSessionChatHistory))
            {
                var historyToSave = _currentSessionChatHistory;
                var npcName = npc.Name;
                
                // Detectar testigos al finalizar la charla (Consulta en RAM O(1))
                var nearbyWitnesses = Game1.player.currentLocation.characters
                    .Where(c => c.Name != npcName && c.IsVillager && Vector2.Distance(c.Tile, Game1.player.Tile) <= 8f && _supportedNpcs.Contains(c.Name))
                    .Select(c => c.Name)
                    .ToList();
                
                Task.Run(async () => 
                {
                    try
                    {
                        // Consolidación del NPC principal
                        using var bgCts = new CancellationTokenSource(TimeSpan.FromSeconds(120)); 
                        await _memoryService.ConsolidateMemoriesAsync(npcName, historyToSave, _veniceApi, bgCts.Token);
                        
                        // Consolidación de testigos (Eavesdropping / Rumores)
                        foreach (var witness in nearbyWitnesses)
                        {
                            _logger.Log($"[{witness}] Escuchó la conversación de {npcName} a escondidas. Intentando extraer chismes...", LogLevel.Trace);
                            using var witnessCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                            await _memoryService.ConsolidateMemoriesAsync(witness, historyToSave, _veniceApi, witnessCts.Token, isOverhearing: true, activeNpcName: npcName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Log($"Error interno de consolidación en 2do plano: {ex.Message}", LogLevel.Error);
                    }
                });
            }

            _activeNpc = null;
            _activeMenu = null;
        }
    }
}
