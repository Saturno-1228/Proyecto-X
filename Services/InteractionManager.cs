using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using LivingCompanionsValley.UI;
using LivingCompanionsValley.Models;

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
                var playerTile = Game1.player.Tile;
                _activeNpc = Game1.player.currentLocation.characters
                             .FirstOrDefault(n => Vector2.Distance(n.Tile, playerTile) <= 3f && n.IsVillager);

                if (_activeNpc != null)
                {
                    // Solo permitimos interactuar si el personaje tiene un archivo de Lore configurado
                    string xmlPath = Path.Combine(_helper.DirectoryPath, "Assets", "Lore", $"{_activeNpc.Name}.xml");
                    if (File.Exists(xmlPath))
                    {
                        StartInteraction(_activeNpc);
                    }
                }
            }
        }

        private void StartInteraction(NPC npc)
        {
            _activeNpc = npc;
            _currentSessionChatHistory = "";
            _sessionMessages.Clear();

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
                
                // Cargar la Identidad XML Estática
                string xmlPath = Path.Combine(_helper.DirectoryPath, "Assets", "Lore", $"{npc.Name}.xml");
                string xmlConfig = File.Exists(xmlPath) ? File.ReadAllText(xmlPath) : $"<Identidad><nombre>{npc.Name}</nombre></Identidad>";

                // Entorno dinámico básico
                var envState = new EnvironmentState { 
                    Weather = Game1.isRaining ? "Lloviendo" : "Soleado", 
                    TimeOfDay = Game1.timeOfDay.ToString(),
                    CurrentLocation = Game1.player.currentLocation.Name,
                    HeldItem = Game1.player.ActiveObject?.DisplayName ?? "Ninguno"
                };

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
                
                Task.Run(async () => 
                {
                    try
                    {
                        // Tarea pesada con GLM-5, totalmente desacoplada del hilo principal del juego
                        using var bgCts = new CancellationTokenSource(TimeSpan.FromSeconds(120)); // Damos 2 minutos para el "Thinking"
                        await _memoryService.ConsolidateMemoriesAsync(npcName, historyToSave, _veniceApi, bgCts.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger.Log($"Error interno de consolidación en 2do plano ({npcName}): {ex.Message}", LogLevel.Error);
                    }
                });
            }

            _activeNpc = null;
            _activeMenu = null;
        }
    }
}
