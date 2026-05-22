using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;
using StardewValley.Pathfinding;
using LivingCompanionsValley.Models;

namespace LivingCompanionsValley.Services
{
    public class WorkerNPC : NPC
    {
        public WorkerState State { get; private set; }
        public List<Item> Inventory { get; private set; } = new List<Item>();
        
        // Temporizador interno para tareas
        public int TaskTicks { get; set; } = 0;
        public string CurrentTaskName { get; set; } = "Descansando";
        public WorkerRoutineState RoutineState { get; set; } = WorkerRoutineState.Sleeping;

        public WorkerNPC(WorkerState state, Vector2 tilePos, string locationName) 
            : base()
        {
            this.State = state;
            this.displayName = state.Name;
            this.Name = state.Name;
            this.Position = tilePos * 64f;
            this.currentLocation = Game1.getLocationFromName(locationName);
            this.DefaultMap = locationName;
            this.FacingDirection = 2;
            
            // Inicializar sprite nativo
            this.Sprite = new AnimatedSprite("Characters\\Farmer", 0, 16, 32);

            this.Age = 1; // Adulto
            this.SocialAnxiety = 0;
            this.Optimism = 1;
            this.Gender = StardewValley.Gender.Undefined;

            // Cargar mochila
            LoadInventory();
        }

        public void LoadInventory()
        {
            Inventory.Clear();
            foreach (var saved in State.Inventory)
            {
                try
                {
                    Item item = ItemRegistry.Create(saved.ItemId, saved.Stack);
                    if (item is Tool tool)
                    {
                        tool.UpgradeLevel = saved.ToolUpgradeLevel;
                    }
                    Inventory.Add(item);
                }
                catch (Exception ex)
                {
                    ModEntry.Logger?.Log($"Error cargando item {saved.ItemId} para {Name}: {ex.Message}", StardewModdingAPI.LogLevel.Warn);
                }
            }

            // Si está vacío, darle herramientas iniciales por defecto
            if (Inventory.Count == 0)
            {
                Inventory.Add(ItemRegistry.Create("(T)Axe"));
                Inventory.Add(ItemRegistry.Create("(T)WateringCan"));
                SaveInventory();
            }
        }

        public void SaveInventory()
        {
            State.Inventory.Clear();
            foreach (var item in Inventory)
            {
                if (item == null) continue;
                State.Inventory.Add(new SavedItem
                {
                    ItemId = item.QualifiedItemId,
                    Stack = item.Stack,
                    Name = item.DisplayName,
                    IsTool = item is Tool,
                    ToolUpgradeLevel = (item is Tool t) ? t.UpgradeLevel : 0
                });
            }
        }

        public void AddLog(string entry)
        {
            string time = Game1.getTimeOfDayString(Game1.timeOfDay);
            string formatted = $"[{time}] {entry}";
            State.DailyLog.Add(formatted);
            ModEntry.Logger?.Log($"[Worker {Name}] {formatted}", StardewModdingAPI.LogLevel.Trace);
        }

        public void ClearLog()
        {
            State.DailyLog.Clear();
        }

        /// <summary>
        /// Determina si el trabajador tiene la herramienta requerida en su inventario.
        /// </summary>
        public bool HasTool<T>(out T? foundTool) where T : Tool
        {
            foundTool = Inventory.OfType<T>().FirstOrDefault();
            return foundTool != null;
        }

        public void AddToInventory(Item item)
        {
            foreach (var existing in Inventory)
            {
                if (existing != null && existing.canStackWith(item))
                {
                    existing.Stack += item.Stack;
                    SaveInventory();
                    return;
                }
            }

            if (Inventory.Count < 12)
            {
                Inventory.Add(item);
            }
            else
            {
                AddLog($"Mochila llena. No se pudo guardar {item.DisplayName}.");
            }
            SaveInventory();
        }

        /// <summary>
        /// Consume energía o desgasta herramientas si fuera necesario.
        /// </summary>
        public void PerformWorkEnergyEffect(int amount)
        {
            // En el futuro podemos implementar fatiga/cansancio
        }

        private bool CanClear(StardewValley.Object obj)
        {
            if (obj.IsWeeds()) return true;
            if (obj.QualifiedItemId == "(O)294" || obj.QualifiedItemId == "(O)295" || obj.Name.Contains("Twig"))
            {
                return Inventory.Any(item => item is Axe);
            }
            if (obj.QualifiedItemId == "(O)343" || obj.Name.Contains("Stone"))
            {
                return Inventory.Any(item => item is Pickaxe);
            }
            return false;
        }

        public override void update(GameTime time, GameLocation location)
        {
            base.update(time, location);

            ProcessRoutineStateMachine();

            if (location is Farm farm && RoutineState == WorkerRoutineState.Wandering)
            {
                TaskTicks++;
                
                // Simulación física interactiva si el jugador está en la Granja
                if (Game1.currentLocation is Farm)
                {
                    if (TaskTicks % 120 == 0 && this.controller == null) // Cada 2 segundos
                    {
                        WanderRandomly(farm);
                    }
                }
                else
                {
                    // Simulación matemática eficiente fuera de pantalla (cada hora de juego, aprox. 50 segundos reales)
                    if (TaskTicks % 3000 == 0) 
                    {
                        PerformOffScreenWork(farm);
                    }
                }
            }
        }

        private void ProcessRoutineStateMachine()
        {
            int timeOfDay = Game1.timeOfDay;

            // 6:00 AM - Dejar la cabaña (Teletransporte inmediato a la puerta exterior)
            if (timeOfDay >= 600 && timeOfDay < 700 && RoutineState == WorkerRoutineState.Sleeping)
            {
                RoutineState = WorkerRoutineState.LeavingCabin;
                TeleportOutsideCabin();
            }
            // 7:00 AM - Empieza a trabajar/vagar
            else if (timeOfDay >= 700 && timeOfDay < 2300 && (RoutineState == WorkerRoutineState.LeavingCabin || RoutineState == WorkerRoutineState.Sleeping))
            {
                RoutineState = WorkerRoutineState.Wandering;
                if (this.currentLocation?.Name != "Farm") TeleportOutsideCabin(); 
            }
            // 11:00 PM - Regresar a la cabaña caminando
            else if (timeOfDay >= 2300 && timeOfDay < 2400 && RoutineState == WorkerRoutineState.Wandering)
            {
                RoutineState = WorkerRoutineState.ReturningCabin;
                PathfindToCabin();
            }
            // 12:00 AM (Medianoche) - Emergencia: Teletransportar a la cabaña si no ha llegado
            else if (timeOfDay >= 2400 && RoutineState == WorkerRoutineState.ReturningCabin)
            {
                if (this.currentLocation?.Name != State.CabinName)
                {
                    RoutineState = WorkerRoutineState.Sleeping;
                    TeleportInsideCabin();
                }
            }
        }

        public void WarpTo(string locationName, Vector2 tile)
        {
            var newLoc = Game1.getLocationFromName(locationName);
            if (newLoc != null && this.currentLocation != newLoc)
            {
                this.currentLocation?.characters.Remove(this);
                newLoc.characters.Add(this);
                this.currentLocation = newLoc;
            }
            this.Position = tile * 64f;
        }

        private void TeleportOutsideCabin()
        {
            var farm = Game1.getFarm();
            var cabin = farm.buildings.FirstOrDefault(b => b.indoors.Value?.Name == State.CabinName);
            if (cabin != null)
            {
                WarpTo("Farm", new Vector2(cabin.tileX.Value + cabin.humanDoor.X, cabin.tileY.Value + cabin.humanDoor.Y + 1));
            }
            else
            {
                WarpTo("Farm", new Vector2(64, 15));
            }
            CurrentTaskName = "Yendo al trabajo";
        }

        private void PathfindToCabin()
        {
            CurrentTaskName = "Yendo a dormir";
            var farm = Game1.getFarm();
            var cabin = farm.buildings.FirstOrDefault(b => b.indoors.Value?.Name == State.CabinName);
            
            if (cabin != null && this.currentLocation == farm)
            {
                Point doorPoint = new Point(cabin.tileX.Value + cabin.humanDoor.X, cabin.tileY.Value + cabin.humanDoor.Y + 1);
                this.controller = new PathFindController(this, farm, doorPoint, -1, (c, l) =>
                {
                    RoutineState = WorkerRoutineState.Sleeping;
                    TeleportInsideCabin();
                });
            }
            else
            {
                RoutineState = WorkerRoutineState.Sleeping;
                TeleportInsideCabin();
            }
        }

        public void TeleportInsideCabin()
        {
            // Coordenadas cercanas a la cama en una cabaña típica (o la entrada si no la encontramos)
            WarpTo(State.CabinName, new Vector2(3, 4));
            this.controller = null;
            CurrentTaskName = "Durmiendo";
        }

        private void WanderRandomly(Farm farm)
        {
            Vector2? targetDebris = FindNearestDebris(farm);
            if (targetDebris == null)
            {
                CurrentTaskName = "Descansando";
                return;
            }

            Vector2 targetTile = targetDebris.Value;
            Vector2? walkTile = FindAdjacentWalkableTile(farm, targetTile);

            if (walkTile == null) return; // No hay lugar adyacente transitable

            CurrentTaskName = "Limpiando terreno";
            Point targetPoint = new Point((int)walkTile.Value.X, (int)walkTile.Value.Y);

            this.controller = new PathFindController(this, farm, targetPoint, -1, (c, l) =>
            {
                ClearDebrisAt(farm, targetTile);
            });
        }

        private void ClearDebrisAt(Farm farm, Vector2 target)
        {
            if (farm.objects.TryGetValue(target, out var obj) && CanClear(obj))
            {
                // Enfrentarse al objeto
                this.faceGeneralDirection(target * 64f);
                
                // Remover y cosechar
                farm.objects.Remove(target);

                string harvestId = "(O)388"; // Madera
                string name = "Madera";
                
                if (obj.IsWeeds())
                {
                    Game1.playSound("cut");
                    harvestId = "(O)771"; // Fibra
                    name = "Fibra";
                }
                else if (obj.Name.Contains("Stone") || obj.QualifiedItemId == "(O)343")
                {
                    Game1.playSound("hammer");
                    harvestId = "(O)390"; // Piedra
                    name = "Piedra";
                }
                else
                {
                    Game1.playSound("axchop");
                }

                Item harvested = ItemRegistry.Create(harvestId);
                AddToInventory(harvested);
                AddLog($"Limpié {obj.DisplayName} en la baldosa {target} y obtuve {name}.");
            }
            this.controller = null;
        }

        private void PerformOffScreenWork(Farm farm)
        {
            var debrisList = farm.objects.Pairs
                .Where(kv => CanClear(kv.Value))
                .ToList();

            if (debrisList.Count > 0)
            {
                var rand = new Random();
                var targetPair = debrisList[rand.Next(debrisList.Count)];
                Vector2 tile = targetPair.Key;
                var obj = targetPair.Value;

                farm.objects.Remove(tile);

                string harvestId = "(O)388";
                string name = "Madera";
                if (obj.IsWeeds()) { harvestId = "(O)771"; name = "Fibra"; }
                else if (obj.Name.Contains("Stone") || obj.QualifiedItemId == "(O)343") { harvestId = "(O)390"; name = "Piedra"; }

                Item harvested = ItemRegistry.Create(harvestId);
                AddToInventory(harvested);
                AddLog($"[Fuera de pantalla] Limpié {obj.DisplayName} en la baldosa {tile} y obtuve {name}.");

                // Teletransportar físicamente al NPC a esta baldosa para inmersión
                // Así cuando el jugador vuelva a la granja, el trabajador estará donde trabajó
                this.Position = tile * 64f;
            }
        }

        private Vector2? FindNearestDebris(Farm farm)
        {
            Vector2? nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var pair in farm.objects.Pairs)
            {
                var obj = pair.Value;
                if (CanClear(obj))
                {
                    float dist = Vector2.Distance(this.Tile, pair.Key);
                    if (dist < nearestDist && dist < 15f) // Rango máximo de 15 baldosas de búsqueda
                    {
                        // Validar si es alcanzable por lo menos una baldosa adyacente
                        if (FindAdjacentWalkableTile(farm, pair.Key) != null)
                        {
                            nearestDist = dist;
                            nearest = pair.Key;
                        }
                    }
                }
            }
            return nearest;
        }

        private Vector2? FindAdjacentWalkableTile(GameLocation location, Vector2 target)
        {
            Vector2[] offsets = { new Vector2(0, 1), new Vector2(0, -1), new Vector2(1, 0), new Vector2(-1, 0) };
            foreach (var offset in offsets)
            {
                Vector2 adjacent = target + offset;
                if (location.isTilePassable(adjacent))
                {
                    return adjacent;
                }
            }
            return null;
        }

        private Farmer? _dummyFarmer;

        // Evitar que el diálogo nativo rompa el flujo
        public override bool checkAction(Farmer who, GameLocation location)
        {
            // Si el jugador pulsa Shift + Click Derecho, se maneja por Harmony (abre la mochila).
            // De lo contrario, se abre el diálogo normal o el chat IA si está configurado.
            return base.checkAction(who, location);
        }

        public override void draw(Microsoft.Xna.Framework.Graphics.SpriteBatch b)
        {
            if (_dummyFarmer == null)
            {
                _dummyFarmer = WorkerTextureBaker.CreateDummyFarmer(State);
            }

            // Sincronizar estado completo
            _dummyFarmer.Position = this.Position;
            _dummyFarmer.FacingDirection = this.FacingDirection;
            _dummyFarmer.currentLocation = this.currentLocation;
            _dummyFarmer.FarmerSprite.currentFrame = this.Sprite.currentFrame;
            _dummyFarmer.Sprite.currentFrame = this.Sprite.currentFrame;

            _dummyFarmer.draw(b);
        }
    }
}
