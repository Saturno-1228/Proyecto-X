using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Pathfinding;

namespace StardewLivingValley.Services
{
    public class NPCActionController
    {
        private readonly IMonitor _logger;
        
        private enum ActionState
        {
            Idle,
            CalculatingRouteToTarget,
            WalkingToWarp,
            WarpingToNextMap,
            Inspecting,
            CalculatingRouteBack,
            WalkingToPlayer,
            Finished
        }

        private ActionState _currentState = ActionState.Idle;
        private NPC? _activeNpc;
        private string _targetLocationName = "";
        private Action? _onCompleteCallback;
        private MemoryService? _memoryService;
        
        // Routing
        private MapRoute? _currentRoute;
        private int _routeIndex = 0;
        private bool _isReturning = false;

        private double _stateTimer = 0;

        public NPCActionController(IMonitor logger, IModHelper helper)
        {
            _logger = logger;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        }

        public void SetMemoryService(MemoryService memoryService)
        {
            _memoryService = memoryService;
        }

        private GameLocation? ResolveLocation(string name)
        {
            var loc = Game1.getLocationFromName(name);
            if (loc != null) return loc;

            var farm = Game1.getLocationFromName("Farm");
            if (farm != null)
            {
                foreach (var building in farm.buildings)
                {
                    if (building.indoors.Value != null)
                    {
                        if (name.Equals("Coop", StringComparison.OrdinalIgnoreCase) && 
                            building.buildingType.Value.Contains("Coop", StringComparison.OrdinalIgnoreCase))
                        {
                            return building.indoors.Value;
                        }
                        if (name.Equals("Barn", StringComparison.OrdinalIgnoreCase) && 
                            building.buildingType.Value.Contains("Barn", StringComparison.OrdinalIgnoreCase))
                        {
                            return building.indoors.Value;
                        }
                        if (building.indoors.Value.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || 
                            building.indoors.Value.NameOrUniqueName.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            return building.indoors.Value;
                        }
                    }
                }
            }

            return null;
        }

        public void StartInspection(NPC npc, string targetLocation, Action onComplete)
        {
            if (_currentState != ActionState.Idle)
            {
                _logger.Log($"[ActionController] El NPC {npc.Name} ya está ejecutando una acción.", LogLevel.Warn);
                return;
            }

            _activeNpc = npc;
            _targetLocationName = targetLocation;
            _onCompleteCallback = onComplete;
            _isReturning = false;

            _currentState = ActionState.CalculatingRouteToTarget;
            _stateTimer = 0;
            
            _logger.Log($"[ActionController] {npc.Name} inicia inspección hacia {_targetLocationName}. Origen: {npc.currentLocation.NameOrUniqueName}", LogLevel.Info);
        }

        public bool IsNpcOnMission(NPC npc)
        {
            return _activeNpc == npc && _currentState != ActionState.Idle;
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (_currentState == ActionState.Idle || _activeNpc == null) return;

            switch (_currentState)
            {
                case ActionState.CalculatingRouteToTarget:
                    var targetLoc = ResolveLocation(_targetLocationName);
                    if (targetLoc != null)
                    {
                        _currentRoute = CrossMapPathfinder.FindMapPath(_activeNpc.currentLocation, targetLoc);
                        if (_currentRoute != null && _currentRoute.Nodes.Count > 0)
                        {
                            _routeIndex = 0;
                            _currentState = ActionState.WalkingToWarp;
                            _logger.Log($"[ActionController] Ruta encontrada hacia {_targetLocationName}. Nodos: {_currentRoute.Nodes.Count}", LogLevel.Info);
                        }
                        else
                        {
                            _logger.Log($"[ActionController] No se encontró ruta cruzando mapas hacia {_targetLocationName}. Cancelando.", LogLevel.Error);
                            FinishAction();
                        }
                    }
                    else
                    {
                        _logger.Log($"[ActionController] La ubicación {_targetLocationName} no existe. Cancelando.", LogLevel.Error);
                        FinishAction();
                    }
                    break;

                case ActionState.WalkingToWarp:
                    if (_activeNpc.controller == null)
                    {
                        if (_currentRoute != null && _routeIndex < _currentRoute.Nodes.Count)
                        {
                            var nextNode = _currentRoute.Nodes[_routeIndex];
                            if (nextNode.IsFinalDestination)
                            {
                                // Ya estamos en el mapa final (o era el mismo mapa)
                                Point safeTile = new Point(_activeNpc.currentLocation.Map.DisplayWidth / Game1.tileSize / 2, _activeNpc.currentLocation.Map.DisplayHeight / Game1.tileSize / 2);
                                _activeNpc.controller = new PathFindController(_activeNpc, _activeNpc.currentLocation, safeTile, 0, delegate(Character c, GameLocation l) {
                                    if (!_isReturning) {
                                        _currentState = ActionState.Inspecting;
                                        _stateTimer = 4000;
                                    } else {
                                        _currentState = ActionState.WalkingToPlayer;
                                    }
                                });
                                // Fallback si no hay path
                                if (_activeNpc.controller == null || _activeNpc.controller.pathToEndPoint == null)
                                {
                                     if (!_isReturning) {
                                        _currentState = ActionState.Inspecting;
                                        _stateTimer = 4000;
                                    } else {
                                        _currentState = ActionState.WalkingToPlayer;
                                    }
                                }
                            }
                            else
                            {
                                // Caminar al warp point
                                _activeNpc.controller = new PathFindController(_activeNpc, _activeNpc.currentLocation, nextNode.TargetWarpTile, 0, delegate(Character c, GameLocation l) {
                                    _currentState = ActionState.WarpingToNextMap;
                                    _stateTimer = 200;
                                });
                                // Fallback
                                if (_activeNpc.controller == null || _activeNpc.controller.pathToEndPoint == null)
                                {
                                    _currentState = ActionState.WarpingToNextMap;
                                    _stateTimer = 200;
                                }
                            }
                        }
                    }
                    break;

                case ActionState.WarpingToNextMap:
                    _stateTimer -= Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
                    if (_stateTimer <= 0)
                    {
                        if (_currentRoute != null && _routeIndex < _currentRoute.Nodes.Count)
                        {
                            var node = _currentRoute.Nodes[_routeIndex];
                            var nextLoc = ResolveLocation(node.LocationName);
                            if (nextLoc != null)
                            {
                                Game1.warpCharacter(_activeNpc, nextLoc.NameOrUniqueName, node.ArrivalTile);
                                _routeIndex++;
                                _currentState = ActionState.WalkingToWarp;
                            }
                            else
                            {
                                FinishAction(); // error
                            }
                        }
                    }
                    break;

                case ActionState.Inspecting:
                    _stateTimer -= Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
                    if (_stateTimer <= 0)
                    {
                        _logger.Log($"[ActionController] Inspección terminada. Calculando ruta de regreso a {Game1.player.currentLocation.NameOrUniqueName}.", LogLevel.Info);
                        _isReturning = true;
                        _currentState = ActionState.CalculatingRouteBack;
                    }
                    else if (_activeNpc.controller == null)
                    {
                         Point safeTile = new Point(_activeNpc.currentLocation.Map.DisplayWidth / Game1.tileSize / 2, _activeNpc.currentLocation.Map.DisplayHeight / Game1.tileSize / 2);
                         _activeNpc.controller = new PathFindController(_activeNpc, _activeNpc.currentLocation, new Point(safeTile.X + Game1.random.Next(-3, 3), safeTile.Y + Game1.random.Next(-3, 3)), 0);
                    }
                    break;

                case ActionState.CalculatingRouteBack:
                    _currentRoute = CrossMapPathfinder.FindMapPath(_activeNpc.currentLocation, Game1.player.currentLocation);
                    if (_currentRoute != null && _currentRoute.Nodes.Count > 0)
                    {
                        _routeIndex = 0;
                        _currentState = ActionState.WalkingToWarp;
                        _logger.Log($"[ActionController] Ruta encontrada de regreso. Nodos: {_currentRoute.Nodes.Count}", LogLevel.Info);
                    }
                    else
                    {
                        _logger.Log($"[ActionController] No se encontró ruta de regreso o jugador no encontrado. Memoria de abandono.", LogLevel.Warn);
                        StoreAbandonmentMemory();
                        FinishAction();
                    }
                    break;

                case ActionState.WalkingToPlayer:
                    if (_activeNpc.currentLocation == Game1.player.currentLocation)
                    {
                        Point playerTile = Game1.player.TilePoint;
                        
                        // Si ya está cerca, terminar
                        if (Vector2.Distance(Game1.player.Tile, _activeNpc.Tile) <= 2f)
                        {
                            FinishAction();
                            return;
                        }

                        if (_activeNpc.controller == null)
                        {
                            _activeNpc.controller = new PathFindController(_activeNpc, _activeNpc.currentLocation, playerTile, 1, delegate(Character c, GameLocation l) {
                                FinishAction();
                            });

                            if (_activeNpc.controller == null || _activeNpc.controller.pathToEndPoint == null)
                            {
                                // No pudo llegar al tile exacto, terminar e intentar abrir
                                FinishAction();
                            }
                        }
                    }
                    else
                    {
                        _logger.Log($"[ActionController] El jugador se movió de nuevo. Abortando regreso.", LogLevel.Warn);
                        StoreAbandonmentMemory();
                        FinishAction();
                    }
                    break;
            }
        }

        private void StoreAbandonmentMemory()
        {
            if (_activeNpc != null && _memoryService != null)
            {
                _memoryService.SavePlayerMemory(_activeNpc.Name, $"Fui a revisar {_targetLocationName} como me pediste, pero cuando regresé ya no estabas.");
            }
        }

        private void FinishAction()
        {
            _logger.Log($"[ActionController] Acción de {_activeNpc?.Name} terminada.", LogLevel.Info);
            _currentState = ActionState.Idle;
            
            _onCompleteCallback?.Invoke();
            _onCompleteCallback = null;
            
            _activeNpc = null;
        }
    }
}