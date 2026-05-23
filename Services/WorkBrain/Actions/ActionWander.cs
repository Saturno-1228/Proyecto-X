using Microsoft.Xna.Framework;
using StardewValley;
using System.Collections.Generic;

namespace LivingCompanionsValley.Services.WorkBrain.Actions
{
    public class ActionWander : IGoapAction
    {
        private NPC _owner;
        private float _wanderTimer = 0f;

        public ActionWander(NPC owner)
        {
            _owner = owner;
        }

        public string Name => "Wander";

        public float CalculateUtility(IEnumerable<PerceivedEntity> sensoryCache, InternalStats stats)
        {
            // Utilidad base muy baja. Siempre es una opción si no hay nada más que hacer.
            return 10f;
        }

        public void Start()
        {
            _wanderTimer = 0f;
        }

        public bool Update(float deltaTime)
        {
            _wanderTimer -= deltaTime;

            if (_wanderTimer <= 0f)
            {
                // Moverse a una posición aleatoria cercana cada 3 segundos
                _wanderTimer = 3.0f;
                Point randomPoint = new Point(
                    _owner.TilePoint.X + Game1.random.Next(-3, 4),
                    _owner.TilePoint.Y + Game1.random.Next(-3, 4)
                );

                if (_owner.currentLocation != null && _owner.currentLocation.isTilePassable(new xTile.Dimensions.Location(randomPoint.X, randomPoint.Y), Game1.viewport))
                {
                    _owner.controller = new StardewValley.Pathfinding.PathFindController(_owner, _owner.currentLocation, randomPoint, -1);
                }
            }

            return false; // Nunca termina por sí solo, el GOAP debe sobreescribirlo con otra tarea.
        }

        public void End()
        {
            _owner.controller = null;
            _owner.Halt();
        }

        public void Pause()
        {
            _owner.controller = null;
            _owner.Halt();
        }

        public void Resume()
        {
            _wanderTimer = 0f; // Forzar que camine inmediatamente
        }
    }
}
