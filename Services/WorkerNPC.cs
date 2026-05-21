using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;
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

        /// <summary>
        /// Consume energía o desgasta herramientas si fuera necesario.
        /// </summary>
        public void PerformWorkEnergyEffect(int amount)
        {
            // En el futuro podemos implementar fatiga/cansancio
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
