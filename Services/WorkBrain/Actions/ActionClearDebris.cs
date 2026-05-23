using Microsoft.Xna.Framework;
using StardewValley;
using System.Collections.Generic;
using System.Linq;
using StardewValley.Tools;

namespace LivingCompanionsValley.Services.WorkBrain.Actions
{
    public class ActionClearDebris : IGoapAction
    {
        private NPC _owner;
        private PerceivedEntity? _targetDebris;
        private bool _isClearing = false;
        private float _clearTimer = 0f;

        public ActionClearDebris(NPC owner)
        {
            _owner = owner;
        }

        public string Name => "ClearDebris";

        public float CalculateUtility(IEnumerable<PerceivedEntity> sensoryCache, InternalStats stats)
        {
            // Si ya estamos limpiando o caminando hacia un escombro específico, mantener el objetivo y su utilidad
            if (_isClearing || (_owner.controller != null && _targetDebris != null))
            {
                return 50f * (stats.Energy / 100f);
            }

            // Solo nos interesan los escombros que podamos destruir con nuestras herramientas
            var debrisList = sensoryCache.Where(e => e.Type == EntityType.Debris);
            foreach (var debris in debrisList)
            {
                if (debris.Reference is StardewValley.Object obj && _owner is WorkerNPC worker)
                {
                    bool canClear = false;
                    if (obj.IsWeeds())
                        canClear = worker.HasTool<MeleeWeapon>(out var _);
                    else if (obj.Name.Contains("Stone") || obj.QualifiedItemId == "(O)343")
                        canClear = worker.HasTool<Pickaxe>(out var _);
                    else
                        canClear = worker.HasTool<Axe>(out var _);

                    if (canClear)
                    {
                        _targetDebris = debris;
                        return 50f * (stats.Energy / 100f);
                    }
                }
            }
            return 0f;
        }

        public void Start()
        {
            _isClearing = false;
        }

        public bool Update(float deltaTime)
        {
            if (_owner.currentLocation == null) return true; // Terminar si no hay location
            
            if (_isClearing)
            {
                _clearTimer -= deltaTime;
                if (_clearTimer <= 0f)
                {
                    if (_targetDebris != null && _targetDebris.Reference is StardewValley.Object obj)
                    {
                        // Enfrentarse al objeto
                        _owner.faceGeneralDirection(_targetDebris.TileLocation * 64f);
                        
                        _owner.currentLocation.objects.Remove(_targetDebris.TileLocation);
                        
                        string harvestId = "(O)388"; // Madera
                        if (obj.IsWeeds())
                        {
                            harvestId = "(O)771"; // Fibra
                        }
                        else if (obj.Name.Contains("Stone") || obj.QualifiedItemId == "(O)343")
                        {
                            harvestId = "(O)390"; // Piedra
                        }

                        Item harvested = ItemRegistry.Create(harvestId);
                        if (_owner is WorkerNPC worker)
                        {
                            worker.AddToInventory(harvested);
                            worker.AddLog($"Limpié {obj.DisplayName} en la baldosa {_targetDebris.TileLocation}.");
                        }
                    }
                    else if (_targetDebris != null && _targetDebris.Reference is StardewValley.TerrainFeatures.ResourceClump clump)
                    {
                        _owner.currentLocation.resourceClumps.Remove(clump);
                    }

                    _targetDebris = null;
                    return true; // Terminó de limpiar
                }
                return false; // Sigue limpiando
            }

            // Buscar adyacente libre
            if (_owner.controller == null && !_isClearing)
            {
                if (_targetDebris == null) return true; // Algo salió mal, abortar tarea

                Vector2? walkTile = FindAdjacentWalkableTile(_owner.currentLocation, _targetDebris.TileLocation);
                if (walkTile.HasValue)
                {
                    Point targetPoint = new Point((int)walkTile.Value.X, (int)walkTile.Value.Y);
                    _owner.controller = new StardewValley.Pathfinding.PathFindController(_owner, _owner.currentLocation, targetPoint, -1, (c, l) =>
                    {
                        // Llegó al escombro
                        _isClearing = true;
                        _clearTimer = 0.6f; // Tarda ~600ms en limpiar para coincidir con la animación

                        if (_owner is WorkerNPC worker && _targetDebris != null && _targetDebris.Reference is StardewValley.Object obj)
                        {
                            // Encarar al escombro
                            int direction = worker.getGeneralDirectionTowards(_targetDebris.TileLocation * 64f + new Vector2(32, 32));
                            worker.faceDirection(direction);

                            // Decidir la herramienta y animar
                            Tool? toolToUse = null;
                            if (obj.IsWeeds()) { worker.HasTool<MeleeWeapon>(out var t); toolToUse = t; }
                            else if (obj.Name.Contains("Stone") || obj.QualifiedItemId == "(O)343") { worker.HasTool<Pickaxe>(out var t); toolToUse = t; }
                            else { worker.HasTool<Axe>(out var t); toolToUse = t; }

                            if (toolToUse != null)
                            {
                                worker.PlayToolAnimation(toolToUse, direction);
                            }
                        }
                    });
                }
                else
                {
                    // No se puede alcanzar
                    return true;
                }
            }

            return false;
        }

        private Vector2? FindAdjacentWalkableTile(GameLocation location, Vector2 target)
        {
            Vector2[] offsets = { new Vector2(0, 1), new Vector2(0, -1), new Vector2(1, 0), new Vector2(-1, 0) };
            foreach (var offset in offsets)
            {
                Vector2 adjacent = target + offset;
                if (location.isTilePassable(adjacent))
                {
                    Microsoft.Xna.Framework.Rectangle tileRect = new Microsoft.Xna.Framework.Rectangle((int)adjacent.X * 64 + 2, (int)adjacent.Y * 64 + 2, 60, 60);
                    if (!location.isCollidingPosition(tileRect, Game1.viewport, false, 0, false, _owner))
                    {
                        return adjacent;
                    }
                }
            }
            return null;
        }

        public void End()
        {
            _owner.controller = null;
            _isClearing = false;
        }

        public void Pause()
        {
            _owner.controller = null;
        }

        public void Resume()
        {
            _isClearing = false; // Re-evaluará el pathfinding
        }

        // Método especial para inyectar el target desde el Planner (como hack rápido)
        public void SetTarget(PerceivedEntity target)
        {
            _targetDebris = target;
        }
    }
}
