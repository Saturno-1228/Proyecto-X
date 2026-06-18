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
        private ModConfig? _config;

        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            this.Monitor.Log("Stardew Living Valley (Venice AI Edition) inicializando...", LogLevel.Info);
            
            _config = helper.ReadConfig<ModConfig>();
            
            var veniceApi = new NeuralLink(_config, this.Monitor);
            var Hippocampus = new Hippocampus();
            var contextBuilder = new Subconscious();
            var topicRouter = new KnowledgeCortex(helper, this.Monitor);
            _LimbicSystem = new LimbicSystem(helper, this.Monitor);
            var SensoryCortex = new SensoryCortex(this.Monitor, helper.DirectoryPath);
            _actionController = new Cerebellum(this.Monitor, helper);
            MotorCortex.SetLogger(this.Monitor);
            _actionController.SetHippocampus(Hippocampus);
            _actionController.SetSensoryCortex(SensoryCortex);
            
            _Consciousness = new Consciousness(this.Monitor, veniceApi, Hippocampus, contextBuilder, topicRouter, SensoryCortex, _actionController, _config);
            _Consciousness.RegisterReopenCallback(ReopenDialogue);

            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Display.MenuChanged += OnMenuChanged;
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
            
            // Pasar el reporte de inspección como contexto del sistema para la IA
            _Consciousness?.StartInteraction(_activeNpc, _activeMenu, "");
            // NO mostramos "..." — el menú se queda en estado "pensando" hasta que la API responda
            // Esto evita que el usuario escriba algo que cancele la primera llamada API
            
            // Enviar el reporte como mensaje del "sistema" para que la IA responda basándose en datos reales
            string contextMessage = $"[SISTEMA: Acabas de regresar de una misión de inspección. {initialMessage} Ahora cuéntale al jugador lo que encontraste de forma natural y breve, usando tu personalidad.]";
            _Consciousness?.HandleChatAsync(contextMessage);
        }
    }
}
