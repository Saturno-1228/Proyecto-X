using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingCompanionsValley.Services.WorkBrain.Actions
{
    public class ActionLeaveCabin : IGoapAction
    {
        private NPC _owner;
        private string _cabinName;

        public ActionLeaveCabin(NPC owner, string cabinName)
        {
            _owner = owner;
            _cabinName = cabinName;
        }

        public string Name => "LeaveCabin";

        public float CalculateUtility(IEnumerable<PerceivedEntity> sensoryCache, InternalStats stats)
        {
            // Si el NPC está dentro de la cabaña y son horas de trabajo (6:00 AM a 11:00 PM),
            // su prioridad para salir al exterior es extremadamente alta.
            if (_owner.currentLocation != null && _owner.currentLocation.NameOrUniqueName == _cabinName)
            {
                if (Game1.timeOfDay >= 600 && Game1.timeOfDay < 2300)
                {
                    return 95f; // Prioridad casi absoluta para iniciar el día
                }
            }
            return 0f;
        }

        public void Start()
        {
            WarpOutsideCabin();
        }

        private void WarpOutsideCabin()
        {
            var farm = Game1.getFarm();
            Vector2 targetTile = new Vector2(64, 15); // Fallback cerca de la casa de la granja

            // Intentar buscar la cabaña física en la granja para aparecer justo en su puerta
            var building = farm.buildings.FirstOrDefault(b => b.indoors.Value != null && b.indoors.Value.NameOrUniqueName == _cabinName);
            if (building != null)
            {
                targetTile = new Vector2(
                    building.tileX.Value + building.humanDoor.Value.X,
                    building.tileY.Value + building.humanDoor.Value.Y + 1
                );
            }

            if (_owner.currentLocation != farm)
            {
                _owner.currentLocation?.characters.Remove(_owner);
                farm.characters.Add(_owner);
                _owner.currentLocation = farm;
            }
            _owner.setTileLocation(targetTile);
            _owner.controller = null;
            _owner.Halt();

            if (_owner is WorkerNPC worker)
            {
                worker.AddLog("Saliendo de la cabaña para comenzar la jornada laboral.");
            }
        }

        public bool Update(float deltaTime)
        {
            // Acción instantánea, se termina inmediatamente para que el planificador elija la siguiente tarea
            return true;
        }

        public void End()
        {
        }

        public void Pause()
        {
        }

        public void Resume()
        {
            Start();
        }
    }
}
