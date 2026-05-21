using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Buildings;
using StardewValley.Locations;
using LivingCompanionsValley.Services;
using LivingCompanionsValley.Configuration;
using LivingCompanionsValley.Models;
using LivingCompanionsValley.Menus;

namespace LivingCompanionsValley
{
    public class ModEntry : Mod
    {
        public static ModEntry Instance { get; private set; } = null!;
        internal static IMonitor? Logger { get; private set; }
        private InteractionManager? _interactionManager;
        private MemoryService? _memoryService;
        private ModConfig? _config;

        // Lista de trabajadores activos en la sesión actual
        private readonly List<WorkerNPC> _activeWorkers = new List<WorkerNPC>();
        // Datos persistentes cargados
        private SaveData _saveData = new SaveData();

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            Logger = this.Monitor;
            _config = helper.ReadConfig<ModConfig>();

            if (string.IsNullOrWhiteSpace(_config.VeniceApiKey) || _config.VeniceApiKey == "INGRESA_TU_API_KEY_AQUI")
            {
                Logger.Log("ADVERTENCIA: No has configurado tu Venice API Key.", LogLevel.Warn);
            }

            var veniceApi = new VeniceApiService(_config.VeniceApiKey, Logger!);
            _memoryService = new MemoryService(helper, Logger!);
            var contextBuilder = new ContextBuilderService();
            var topicRouter = new TopicRouterService(helper, Logger!);
            
            _interactionManager = new InteractionManager(helper, Logger!, veniceApi, _memoryService, contextBuilder, topicRouter);

            Logger!.Log("Living Companions Valley v2.0 (Dual-Model) inicializado correctamente.", LogLevel.Info);

            if (_config.EnableBuiltInHdPortraits)
            {
                var harmony = new HarmonyLib.Harmony(this.ModManifest.UniqueID);
                HdPortraitPatcher.ApplyPatches(harmony, Logger!);
            }

            // Eventos de ciclo de juego
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.Content.AssetRequested += OnAssetRequested;
            helper.Events.Input.ButtonPressed += OnButtonPressed;

            // Guardado y Carga seguros para evitar crasheos de serialización XML
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.Saving += OnSaving;
            helper.Events.GameLoop.Saved += OnSaved;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        }

        public List<WorkerNPC> GetHiredWorkers()
        {
            return _activeWorkers;
        }

        public void RegisterAndSpawnWorker(WorkerState state)
        {
            // Spawnear en su cabaña asignada a tile (2, 2)
            var worker = new WorkerNPC(state, new Vector2(2, 2), state.CabinName);
            _activeWorkers.Add(worker);
            
            var cabin = Game1.getLocationFromName(state.CabinName);
            if (cabin != null)
            {
                cabin.characters.Add(worker);
                worker.AddLog("Contratado y asignado a esta cabaña.");
            }
            else
            {
                Logger?.Log($"No se encontró la cabaña {state.CabinName} para spawnear a {state.Name}. Spawneando en la Granja.", LogLevel.Warn);
                var farm = Game1.getFarm();
                worker.currentLocation = farm;
                worker.Position = new Vector2(64, 15) * 64f;
                farm.characters.Add(worker);
            }

            if (!_saveData.HiredWorkers.Any(w => w.Id == state.Id))
            {
                _saveData.HiredWorkers.Add(state);
            }
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            // 1. Decaimiento de memoria nativo
            foreach (var npc in Utility.getAllCharacters())
            {
                if (npc.IsVillager)
                {
                    _memoryService?.ProcessDailyDecay(npc.Name);
                }
            }
            Logger?.Log("Decaimiento diario procesado.", LogLevel.Trace);

            // 2. Colocar el letrero físico de contratación (Tablero Ledger)
            PlaceHiringBoard();

            // 3. Procesar salarios y bitácora diaria de trabajadores
            ProcessDailyWages();
        }

        private void PlaceHiringBoard()
        {
            var farm = Game1.getFarm();
            if (farm == null) return;

            Vector2 boardTile = GetHiringBoardTile();

            // Si no hay ningún objeto en esa baldosa, colocamos el letrero de madera
            if (!farm.objects.ContainsKey(boardTile))
            {
                try
                {
                    var signObj = ItemRegistry.Create<StardewValley.Object>("(O)WoodSign");
                    signObj.TileLocation = boardTile;
                    signObj.IsSpawnedObject = true;
                    farm.objects.Add(boardTile, signObj);
                    Logger?.Log($"Tablero de contratación colocado en la baldosa {boardTile}.", LogLevel.Trace);
                }
                catch (Exception ex)
                {
                    Logger?.Log($"Error colocando tablero de contratación: {ex.Message}", LogLevel.Warn);
                }
            }
        }

        private Vector2 GetHiringBoardTile()
        {
            var farm = Game1.getFarm();
            if (farm == null) return new Vector2(62, 15);

            var farmhouse = farm.buildings.FirstOrDefault(b => b.buildingType.Value == "Farmhouse");
            if (farmhouse != null)
            {
                int doorX = farmhouse.tileX.Value + farmhouse.humanDoor.X;
                int doorY = farmhouse.tileY.Value + farmhouse.humanDoor.Y;
                return new Vector2(doorX - 2, doorY);
            }
            return new Vector2(62, 15); // Fallback
        }

        private void ProcessDailyWages()
        {
            foreach (var worker in _activeWorkers)
            {
                worker.ClearLog(); // Limpiar bitácora del día anterior
                
                int wage = worker.State.Wage;
                if (Game1.player.Money >= wage)
                {
                    Game1.player.Money -= wage;
                    worker.AddLog($"Salario diario de {wage}g cobrado exitosamente.");
                }
                else
                {
                    // No hay suficiente dinero, trabaja bajo advertencia
                    worker.AddLog($"¡ALERTA! No se pudo cobrar el salario de {wage}g por falta de fondos.");
                }
            }
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            if (e.Button.IsActionButton())
            {
                var clickedTile = e.Cursor.GrabTile;
                if (Game1.currentLocation is Farm)
                {
                    Vector2 boardTile = GetHiringBoardTile();
                    if (clickedTile == boardTile)
                    {
                        // Abrir Ledger de Contratación
                        Game1.playSound("shwip");
                        Game1.activeClickableMenu = new HiringLedgerMenu();
                        this.Helper.Input.Suppress(e.Button); // Evitar cartel nativo
                    }
                }
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            _activeWorkers.Clear();
            _saveData = this.Helper.Data.ReadSaveData<SaveData>("hired-workers") ?? new SaveData();

            foreach (var state in _saveData.HiredWorkers)
            {
                RegisterAndSpawnWorker(state);
            }
            Logger?.Log($"Cargados {_activeWorkers.Count} trabajadores de la partida guardada.", LogLevel.Info);
        }

        private void OnSaving(object? sender, SavingEventArgs e)
        {
            // Sincronizar e inventariar antes de remover para evitar perder items
            _saveData.HiredWorkers.Clear();
            foreach (var worker in _activeWorkers)
            {
                worker.SaveInventory();
                _saveData.HiredWorkers.Add(worker.State);
            }

            // Guardar en el archivo de save de SMAPI
            this.Helper.Data.WriteSaveData("hired-workers", _saveData);

            // ¡CRÍTICO! Remover temporalmente a los NPCs de las ubicaciones del juego
            // de lo contrario, el serializador XML de Stardew Valley crasheará al guardar.
            foreach (var worker in _activeWorkers)
            {
                worker.currentLocation?.characters.Remove(worker);
            }
            Logger?.Log("Trabajadores removidos temporalmente para guardado seguro.", LogLevel.Trace);
        }

        private void OnSaved(object? sender, SavedEventArgs e)
        {
            // ¡Re-spawnear inmediatamente después de completar el guardado!
            foreach (var worker in _activeWorkers)
            {
                var cabin = Game1.getLocationFromName(worker.State.CabinName);
                if (cabin != null)
                {
                    cabin.characters.Add(worker);
                }
                else
                {
                    Game1.getFarm().characters.Add(worker);
                }
            }
            Logger?.Log("Trabajadores restablecidos en sus cabañas después del guardado.", LogLevel.Trace);
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            _activeWorkers.Clear();
            _saveData = new SaveData();
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.StartsWith("Portraits/"))
            {
                string npcName = e.NameWithoutLocale.Name.Split('/')[1];
                string customPortraitPath = $"assets/Portraits/{npcName}_LCV.png";

                if (this.Helper.ModContent.DoesAssetExist<Texture2D>(customPortraitPath))
                {
                    e.LoadFromModFile<Texture2D>(customPortraitPath, AssetLoadPriority.Medium);
                    Logger?.Log($"¡Retrato LCV de {npcName} inyectado con éxito!", LogLevel.Trace);
                }
            }
        }
    }
}