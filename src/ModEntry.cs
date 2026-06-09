using System;
using System.Linq;
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
            
            _interactionManager = new InteractionManager(this.Monitor, veniceApi, memoryService, contextBuilder, topicRouter);

            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Display.MenuChanged += OnMenuChanged;
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || _interactionManager == null) return;

            if (e.Button == SButton.Tab && Game1.activeClickableMenu == null && Context.IsPlayerFree)
            {
                var playerTile = Game1.player.Tile;
                _activeNpc = Game1.player.currentLocation.characters
                             .Where(n => Vector2.Distance(n.Tile, playerTile) <= 3f && n.IsVillager)
                             .OrderBy(n => Vector2.Distance(n.Tile, playerTile))
                             .FirstOrDefault();

                if (_activeNpc != null)
                {
                    // Congelación Temporal Segura
                    if (_activeNpc.CurrentDialogue.Count == 0 && _activeNpc.movementPause <= 0)
                    {
                        _activeNpc.Halt();
                        _activeNpc.faceGeneralDirection(Game1.player.Position);
                        this.Helper.Reflection.GetField<bool>(_activeNpc, "freezeMotion").SetValue(true);
                    }

                    _activeMenu = new NPCDialogueMenu(_activeNpc, OnMessageSubmitted);
                    Game1.activeClickableMenu = _activeMenu;
                    
                    _interactionManager.StartInteraction(_activeNpc, _activeMenu);
                    
                    _activeMenu.ReceiveAiResponse("Hola... [1]");
                }
            }
        }

        private void OnMessageSubmitted(string playerMessage)
        {
            _interactionManager?.HandleChatAsync(playerMessage);
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            if (e.OldMenu is NPCDialogueMenu && e.NewMenu == null && _activeNpc != null)
            {
                this.Helper.Reflection.GetField<bool>(_activeNpc, "freezeMotion").SetValue(false);
                _interactionManager?.EndInteraction();
                _activeNpc = null;
                _activeMenu = null;
            }
        }
    }
}
