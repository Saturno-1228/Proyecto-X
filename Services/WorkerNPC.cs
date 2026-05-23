using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;
using StardewValley.Pathfinding;
using LivingCompanionsValley.Models;
using LivingCompanionsValley.Services.WorkBrain;
using LivingCompanionsValley.Services.WorkBrain.Actions;

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

        private ILivingBrain _brain;

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
            
            // Inicializar sprite nativo con textura válida para evitar ContentLoadException
            this.Sprite = new AnimatedSprite("Characters\\Abigail", 0, 16, 32);
            
            // Instanciar al dummy farmer desde el inicio para poder animarlo de forma nativa
            _dummyFarmer = WorkerTextureBaker.CreateDummyFarmer(state);

            this.Age = 1; // Adulto
            this.SocialAnxiety = 0;
            this.Optimism = 1;
            this.Gender = StardewValley.Gender.Undefined;

            // Cargar mochila
            LoadInventory();

            // Implantar el Cerebro GOAP
            var sensory = new SensorySystem();
            var internalState = new InternalState();
            var reaction = new ReactionSystem();
            var planner = new GoapPlanner();
            
            planner.RegisterAction(new ActionWander(this));
            planner.RegisterAction(new ActionClearDebris(this));
            planner.RegisterAction(new ActionSleep(this, State.CabinName));
            planner.RegisterAction(new ActionLeaveCabin(this, State.CabinName));

            _brain = new LivingBrain(sensory, internalState, reaction, planner);
            _brain.Initialize(this);
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
                Inventory.Add(ItemRegistry.Create("(T)Pickaxe"));
                Inventory.Add(ItemRegistry.Create("(T)Hoe"));
                Inventory.Add(ItemRegistry.Create("(T)WateringCan"));
                Inventory.Add(ItemRegistry.Create("(W)47")); // Scythe (arma)
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

        public void PlayToolAnimation(Tool tool, int direction)
        {
            if (_dummyFarmer != null)
            {
                this.FacingDirection = direction;
                _dummyFarmer.SwingTool(tool, direction);
            }
        }

        public override void update(GameTime time, GameLocation location)
        {
            base.update(time, location);

            if (_dummyFarmer != null)
            {
                _dummyFarmer.TickVisuals(time, this.isMoving(), this.FacingDirection, this.Position, this.currentLocation);
            }

            // Ejecutar el nuevo "Cerebro Vivo" basado en GOAP
            _brain?.UpdateTicked(time, location);
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
            this.setTileLocation(tile);
        }

        private WorkerFakeFarmer? _dummyFarmer;

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

            _dummyFarmer.Position = this.Position;
            _dummyFarmer.faceDirection(this.FacingDirection);
            _dummyFarmer.currentLocation = this.currentLocation;

            _dummyFarmer.draw(b);
        }
    }
}
