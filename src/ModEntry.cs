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
using StardewLivingValley.Brain;

namespace StardewLivingValley
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        private Consciousness? _Consciousness;
        private NPC? _activeNpc;
        private NPCDialogueMenu? _activeMenu;
        private LimbicSystem? _LimbicSystem;
        private Cerebellum? _actionController;
        private PairEmotionService? _pairEmotionService;
        private ModConfig? _config;

        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            this.Monitor.Log("Stardew Living Valley (Venice AI Edition) inicializando...", LogLevel.Info);
            
            _config = helper.ReadConfig<ModConfig>();
            
            var veniceApi = new NeuralLink(_config, this.Monitor);
            var Hippocampus = new Hippocampus(helper.DirectoryPath);
            var contextBuilder = new Subconscious();
            var topicRouter = new KnowledgeCortex(helper, this.Monitor);
            _LimbicSystem = new LimbicSystem(helper, this.Monitor);
            var SensoryCortex = new SensoryCortex(this.Monitor, helper.DirectoryPath);
            _actionController = new Cerebellum(this.Monitor, helper);
            MotorCortex.SetLogger(this.Monitor);
            _actionController.SetHippocampus(Hippocampus);
            _actionController.SetSensoryCortex(SensoryCortex);
            
            _Consciousness = new Consciousness(this.Monitor, veniceApi, Hippocampus, contextBuilder, topicRouter, SensoryCortex, _actionController, _config);
            
            _pairEmotionService = new PairEmotionService(this.Monitor, helper.DirectoryPath);
            var playbackManager = new ConversationPlaybackManager(helper, this.Monitor);
            var socialInteractionManager = new SocialInteractionManager(helper, this.Monitor, veniceApi, contextBuilder, playbackManager, Hippocampus, topicRouter, SensoryCortex, _pairEmotionService, _config);
            _Consciousness.RegisterReopenCallback(ReopenDialogue);

            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Display.MenuChanged += OnMenuChanged;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.DayEnding += OnDayEnding;
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || _Consciousness == null || _config == null) return;

            if (e.Button == _config.ConfigMenuKey && Game1.activeClickableMenu == null && Context.IsPlayerFree)
            {
                Game1.activeClickableMenu = new StardewLivingValley.UI.ConfigMenu(_config, this.Helper);
                return;
            }

            if (e.Button == _config.AIChatKey && Game1.activeClickableMenu == null && Context.IsPlayerFree)
            {
                var playerTile = Game1.player.Tile;
                var closestNpc = Game1.player.currentLocation.characters
                             .Where(n => Vector2.Distance(n.Tile, playerTile) <= 3f && n.IsVillager)
                             .OrderBy(n => Vector2.Distance(n.Tile, playerTile))
                             .FirstOrDefault();

                if (closestNpc != null)
                {
                    // Forzar interacción directa sin pasar por Stardew Valley CheckAction
                    _activeNpc = closestNpc;
                    _activeNpc.Halt();
                    _activeNpc.faceGeneralDirection(Game1.player.Position);
                    this.Helper.Reflection.GetField<bool>(_activeNpc, "freezeMotion").SetValue(true);

                    _activeMenu = new StardewLivingValley.UI.NPCDialogueMenu(_activeNpc, _LimbicSystem!, OnMessageSubmitted);
                    Game1.activeClickableMenu = _activeMenu;
                    
                    _Consciousness?.StartInteraction(_activeNpc, _activeMenu, "");
                    _activeMenu.ReceiveAiResponse("Hola...");
                }
            }
        }

        private void OnMessageSubmitted(string playerMessage)
        {
            _Consciousness?.HandleChatAsync(playerMessage);
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            // Cierre de nuestro menú
            if (e.OldMenu is StardewLivingValley.UI.NPCDialogueMenu && e.NewMenu == null && _activeNpc != null)
            {
                this.Helper.Reflection.GetField<bool>(_activeNpc, "freezeMotion").SetValue(false);
                _Consciousness?.EndInteraction();
                _activeNpc = null;
                _activeMenu = null;
                return;
            }

            // Intercepción del menú Vanilla (Se activa al hacer clic derecho en el NPC)
            if (e.NewMenu is StardewValley.Menus.DialogueBox vanillaMenu && _activeNpc == null)
            {
                if (_config == null || !_config.InterceptVanillaDialogue) return;
                
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

                    if (speaker.CurrentDialogue.Count > 0)
                    {
                        speaker.CurrentDialogue.Pop();
                    }

                    _activeNpc = speaker;
                    _activeNpc.Halt();
                    _activeNpc.faceGeneralDirection(Game1.player.Position);
                    this.Helper.Reflection.GetField<bool>(_activeNpc, "freezeMotion").SetValue(true);

                    _activeMenu = new StardewLivingValley.UI.NPCDialogueMenu(_activeNpc, _LimbicSystem!, OnMessageSubmitted);
                    Game1.activeClickableMenu = _activeMenu;
                    
                    _Consciousness?.StartInteraction(_activeNpc, _activeMenu, vanillaText);
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

            _activeMenu = new StardewLivingValley.UI.NPCDialogueMenu(_activeNpc, _LimbicSystem!, OnMessageSubmitted);
            Game1.activeClickableMenu = _activeMenu;
            
            // Iniciar la interacción en la consciencia para que guarde el historial
            _Consciousness?.StartInteraction(_activeNpc, _activeMenu, initialMessage);
            
            // Inyectar directamente la respuesta pre-generada en la interfaz
            // ¡Ya no llamamos a HandleChatAsync para hacerle otra pregunta a la IA!
            _activeMenu.ReceiveAiResponse(initialMessage);
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            _pairEmotionService?.Decay();
        }

        private void OnDayEnding(object? sender, DayEndingEventArgs e)
        {
            _pairEmotionService?.Save();
        }
    }
}
