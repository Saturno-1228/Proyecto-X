using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewLivingValley.UI;
using StardewLivingValley.Configuration;
using StardewLivingValley.Services;

namespace StardewLivingValley
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        private InteractionManager? _interactionManager;
        private NPC? _activeNpc;
        private NPCDialogueMenu? _activeMenu;
        private EmotionService? _emotionService;
        private NPCActionController? _actionController;

        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            this.Monitor.Log("Stardew Living Valley (Venice AI Edition) inicializando...", LogLevel.Info);
            
            var config = helper.ReadConfig<ModConfig>();
            
            var veniceApi = new VeniceApiService(config.VeniceApiKey, this.Monitor);
            var memoryService = new MemoryService();
            var contextBuilder = new ContextBuilderService();
            var topicRouter = new TopicRouterService(helper, this.Monitor);
            _emotionService = new EmotionService(helper, this.Monitor);
            var observationEngine = new ObservationEngine(this.Monitor, helper.DirectoryPath);
            _actionController = new NPCActionController(this.Monitor, helper);
            AdvancedPathfinder.SetLogger(this.Monitor);
            _actionController.SetMemoryService(memoryService);
            _actionController.SetObservationEngine(observationEngine);
            
            _interactionManager = new InteractionManager(this.Monitor, veniceApi, memoryService, contextBuilder, topicRouter, observationEngine, _actionController);
            _interactionManager.RegisterReopenCallback(ReopenDialogue);

            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Display.MenuChanged += OnMenuChanged;
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || _interactionManager == null) return;

            if (e.Button == SButton.Tab && Game1.activeClickableMenu == null && Context.IsPlayerFree)
            {
                var playerTile = Game1.player.Tile;
                var closestNpc = Game1.player.currentLocation.characters
                             .Where(n => Vector2.Distance(n.Tile, playerTile) <= 3f && n.IsVillager)
                             .OrderBy(n => Vector2.Distance(n.Tile, playerTile))
                             .FirstOrDefault();

                if (closestNpc != null)
                {
                    // Simular clic derecho original
                    closestNpc.checkAction(Game1.player, Game1.player.currentLocation);
                }
            }
        }

        private void OnMessageSubmitted(string playerMessage)
        {
            _interactionManager?.HandleChatAsync(playerMessage);
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            // Cierre de nuestro menú
            if (e.OldMenu is StardewLivingValley.UI.NPCDialogueMenu && e.NewMenu == null && _activeNpc != null)
            {
                this.Helper.Reflection.GetField<bool>(_activeNpc, "freezeMotion").SetValue(false);
                _interactionManager?.EndInteraction();
                _activeNpc = null;
                _activeMenu = null;
                return;
            }

            // Intercepción del menú Vanilla (Se activa al hacer clic derecho en el NPC)
            if (e.NewMenu is StardewValley.Menus.DialogueBox vanillaMenu && _activeNpc == null)
            {
                var charDialogue = this.Helper.Reflection.GetField<Dialogue>(vanillaMenu, "characterDialogue", false)?.GetValue();
                NPC? speaker = charDialogue?.speaker;
                
                if (speaker != null && speaker.IsVillager)
                {
                    if (_actionController != null && _actionController.IsNpcOnMission(speaker))
                    {
                        vanillaMenu.exitThisMenuNoSound();
                        Game1.dialogueUp = false;

                        string[] busyDialogues = {
                            "¡Dame un segundo, estoy yendo a revisar lo que me pediste!",
                            "¡Tengo prisa, voy para allá!",
                            "¡Ahora no puedo hablar, voy retrasado!",
                            "¡Dame un momento, estoy ocupado con lo que me pediste!"
                        };
                        string randomBusyDialogue = busyDialogues[Game1.random.Next(busyDialogues.Length)];
                        speaker.showTextAboveHead(randomBusyDialogue);
                        return;
                    }

                    string rawVanillaText = charDialogue?.getCurrentDialogue() ?? "";
                    string vanillaText = CleanVanillaDialogue(rawVanillaText);

                    vanillaMenu.exitThisMenuNoSound();
                    Game1.dialogueUp = false; // EVITA EL CRASH DE STACK EMPTY

                    _activeNpc = speaker;
                    _activeNpc.Halt();
                    _activeNpc.faceGeneralDirection(Game1.player.Position);
                    this.Helper.Reflection.GetField<bool>(_activeNpc, "freezeMotion").SetValue(true);

                    _activeMenu = new StardewLivingValley.UI.NPCDialogueMenu(_activeNpc, _emotionService!, OnMessageSubmitted);
                    Game1.activeClickableMenu = _activeMenu;
                    
                    _interactionManager?.StartInteraction(_activeNpc, _activeMenu, vanillaText);
                    _activeMenu.ReceiveAiResponse(vanillaText);
                }
            }
        }

        private string CleanVanillaDialogue(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return "";
            
            // Eliminar tokens de formato de Stardew Valley (ej. $h, $q, #$b#)
            string cleaned = Regex.Replace(rawText, @"\$[a-zA-Z][^# ]*", ""); // comandos $
            cleaned = Regex.Replace(cleaned, @"#\$b#", " "); // saltos de diálogo
            cleaned = Regex.Replace(cleaned, @"#", " ");
            cleaned = Regex.Replace(cleaned, @"%\w+", ""); // variables como %kid1
            cleaned = Regex.Replace(cleaned, @"\[\d+\]", ""); // tags embebidos
            
            // Limpiar espacios múltiples y recortes
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            
            return string.IsNullOrWhiteSpace(cleaned) ? "Hola..." : cleaned;
        }

        public void ReopenDialogue(NPC npc, string initialMessage)
        {
            if (_activeNpc != null) return;

            _activeNpc = npc;
            _activeNpc.Halt();
            _activeNpc.faceGeneralDirection(Game1.player.Position);
            this.Helper.Reflection.GetField<bool>(_activeNpc, "freezeMotion").SetValue(true);

            _activeMenu = new StardewLivingValley.UI.NPCDialogueMenu(_activeNpc, _emotionService!, OnMessageSubmitted);
            Game1.activeClickableMenu = _activeMenu;
            
            // Pasar el reporte de inspección como contexto del sistema para la IA
            _interactionManager?.StartInteraction(_activeNpc, _activeMenu, "");
            // NO mostramos "..." — el menú se queda en estado "pensando" hasta que la API responda
            // Esto evita que el usuario escriba algo que cancele la primera llamada API
            
            // Enviar el reporte como mensaje del "sistema" para que la IA responda basándose en datos reales
            string contextMessage = $"[SISTEMA: Acabas de regresar de una misión de inspección. {initialMessage} Ahora cuéntale al jugador lo que encontraste de forma natural y breve, usando tu personalidad.]";
            _interactionManager?.HandleChatAsync(contextMessage);
        }
    }
}
