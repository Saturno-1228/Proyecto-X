using Microsoft.Xna.Framework;
using StardewValley;
using System.Collections.Generic;

namespace LivingCompanionsValley.Services.WorkBrain.Actions
{
    public class ActionSleep : IGoapAction
    {
        private NPC _owner;
        private string _cabinName;
        private bool _isSleeping = false;

        public ActionSleep(NPC owner, string cabinName)
        {
            _owner = owner;
            _cabinName = cabinName;
        }

        public string Name => "Sleep";

        public float CalculateUtility(IEnumerable<PerceivedEntity> sensoryCache, InternalStats stats)
        {
            // Si ya está durmiendo, mantener la prioridad hasta que esté completamente descansado o empiece el día (6:00 AM)
            if (_isSleeping)
            {
                // Despertar si tiene energía al 95% y es de día (entre las 6:00 AM y las 11:00 PM)
                if (stats.Energy >= 95f && Game1.timeOfDay >= 600 && Game1.timeOfDay < 2300)
                {
                    return 0f;
                }
                return 100f; // Continuar durmiendo
            }

            // Utilidad máxima si es la hora de dormir (>= 11:00 PM) o si la energía está en niveles críticos (<= 5%)
            if (Game1.timeOfDay >= 2300 || stats.Energy <= 5f)
            {
                return 100f; // Prioridad absoluta
            }
            return 0f;
        }

        public void Start()
        {
            _isSleeping = true;
            // Pathfind/Warp hacia la cabaña
            WarpInsideCabin();
        }

        private void WarpInsideCabin()
        {
            var newLoc = Game1.getLocationFromName(_cabinName);
            if (newLoc != null && _owner.currentLocation != newLoc)
            {
                _owner.currentLocation?.characters.Remove(_owner);
                newLoc.characters.Add(_owner);
                _owner.currentLocation = newLoc;
            }
            _owner.setTileLocation(new Vector2(3, 4)); // Cerca de la cama
            _owner.controller = null;
            _owner.Halt();
        }

        public bool Update(float deltaTime)
        {
            return false; // Nunca termina por sí solo, el planificador debe cambiar la acción cuando la utilidad caiga
        }

        public void End()
        {
            _isSleeping = false;
            _owner.controller = null;
        }

        public void Pause()
        {
            _owner.controller = null;
        }

        public void Resume()
        {
            _isSleeping = true;
            WarpInsideCabin();
        }
    }
}
