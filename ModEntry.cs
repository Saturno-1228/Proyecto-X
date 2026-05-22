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

            // 2. Colocar el letrero físico en la parada de autobuses
            PlaceHiringBoard();

            // 3. Procesar salarios y bitácora diaria de trabajadores
            ProcessDailyWages();
        }

        private void PlaceHiringBoard()
        {
            var busStop = Game1.getLocationFromName("BusStop");
            if (busStop == null) return;

            Vector2 boardTile = new Vector2(4, 21); // Default
            
            // Buscar el cartel direccional (Action = Message ...) en la capa Buildings
            bool foundSign = false;
            for (int x = 0; x < busStop.Map.Layers[0].LayerWidth; x++)
            {
                for (int y = 0; y < busStop.Map.Layers[0].LayerHeight; y++)
                {
                    string action = busStop.doesTileHaveProperty(x, y, "Action", "Buildings");
                    if (action != null && (action.Contains("Message") || action.Contains("Sign")))
                    {
                        // Moverlo aproximadamente 8 tiles a la derecha del letrero original
                        boardTile = new Vector2(x + 7, y);
                        foundSign = true;
                        break;
                    }
                }
                if (foundSign) break;
            }

            // Buscar si ya existe el tablero gigante
            StardewValley.Objects.Furniture? existingBoard = null;
            foreach (var f in busStop.furniture)
            {
                if (f.QualifiedItemId == "(F)LivingCompanions_HiringBoard")
                {
                    existingBoard = f;
                    existingBoard.AllowLocalRemoval = false; // Hacerlo inamovible
                    break;
                }
            }

            // Si existe pero está en la posición incorrecta (por un parche de actualización), lo quitamos
            if (existingBoard != null && existingBoard.TileLocation != boardTile)
            {
                busStop.furniture.Remove(existingBoard);
                existingBoard = null;
                Logger?.Log($"Moviendo tablero de contratación a nueva posición: {boardTile}.", LogLevel.Info);
            }

            // Si no existe, lo creamos y lo agregamos
            if (existingBoard == null)
            {
                try
                {
                    var furniture = new StardewValley.Objects.Furniture("LivingCompanions_HiringBoard", boardTile);
                    furniture.AllowLocalRemoval = false; // Hacerlo inamovible
                    busStop.furniture.Add(furniture);
                    Logger?.Log($"¡Tablero de Empleos Oficial colocado en {boardTile}!", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    Logger?.Log($"Error colocando tablero de contratación: {ex.Message}", LogLevel.Warn);
                }
            }
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
                // Interceptar Shift + Click Derecho para mochila del trabajador
                bool isShift = this.Helper.Input.IsDown(SButton.LeftShift) || this.Helper.Input.IsDown(SButton.RightShift);
                if (isShift)
                {
                    foreach (var worker in _activeWorkers)
                    {
                        if (worker.currentLocation == Game1.currentLocation)
                        {
                            float distance = Vector2.Distance(Game1.player.Tile, worker.Tile);
                            if (distance <= 2.5f)
                            {
                                OpenWorkerInventory(worker);
                                this.Helper.Input.Suppress(e.Button);
                                return;
                            }
                        }
                    }
                }

                var clickedTile = e.Cursor.GrabTile;
                if (Game1.currentLocation?.Name == "BusStop")
                {
                    var busStop = Game1.currentLocation;
                    foreach (var furniture in busStop.furniture)
                    {
                        if (furniture.QualifiedItemId == "(F)LivingCompanions_HiringBoard")
                        {
                            // Verificar si el clic cae dentro del bounding box del mueble
                            if (furniture.GetBoundingBox().Contains((int)(clickedTile.X * 64), (int)(clickedTile.Y * 64)) ||
                                Vector2.Distance(clickedTile, furniture.TileLocation) <= 2f)
                            {
                                Game1.playSound("shwip");
                                Game1.activeClickableMenu = new HiringLedgerMenu();
                                this.Helper.Input.Suppress(e.Button);
                                return;
                            }
                        }
                    }
                }
            }
        }

        private void OpenWorkerInventory(WorkerNPC worker)
        {
            Game1.playSound("dwop");
            Game1.activeClickableMenu = new ItemGrabMenu(
                worker.Inventory,
                false,
                true,
                item => true,
                (item, farmer) => {
                    worker.SaveInventory();
                },
                $"{worker.Name} - Mochila",
                (item, farmer) => {
                    worker.SaveInventory();
                },
                false,
                true,
                true,
                true,
                false,
                0,
                null,
                -1,
                worker
            );
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

            // Colocar el letrero inmediatamente al cargar la partida (para no tener que esperar a dormir)
            PlaceHiringBoard();
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
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/Furniture"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, string>();
                    // Name / Type / Tilesheet Size / Bounding Box Size / Rotations / Price / Placement Restriction / Display Name / Sprite Index / Texture / Exclude from Shop / Context Tags
                    string furnitureString = "Tablero de Empleos/decor/3 2/3 1/1/1/2/Tablero de Empleos/0/LivingCompanionsValley\\HiringBoard/false/";
                    data.Data["LivingCompanions_HiringBoard"] = furnitureString;
                });
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("LivingCompanionsValley/HiringBoard"))
            {
                e.LoadFrom(() =>
                {
                    // ¡Magia! Extraemos el tablero de órdenes especiales directamente del mapa del pueblo
                    Texture2D townTiles = Game1.content.Load<Texture2D>("Maps\\spring_town");
                    
                    // El tablero está en las baldosas 2013-2015 (arriba) y 2045-2047 (abajo)
                    // En un tilesheet de 32 baldosas de ancho (512px):
                    // Tile 2013 -> X = (2013 % 32) * 16 = 29 * 16 = 464
                    //           -> Y = (2013 / 32) * 16 = 62 * 16 = 992
                    // Tamaño: 3 baldosas de ancho (48px), 2 de alto (32px)
                    Rectangle sourceRect = new Rectangle(464, 992, 48, 32);
                    Color[] pixelData = new Color[48 * 32];
                    townTiles.GetData(0, sourceRect, pixelData, 0, pixelData.Length);

                    Texture2D customBoard = new Texture2D(Game1.graphics.GraphicsDevice, 48, 32);
                    customBoard.SetData(pixelData);
                    
                    Logger?.Log("Textura del tablero de empleos generada dinámicamente desde el mapa del pueblo.", LogLevel.Trace);
                    return customBoard;
                }, AssetLoadPriority.Exclusive);
            }
        }
    }
}