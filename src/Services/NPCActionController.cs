using System;
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
            WarpingToTarget,
            Inspecting,
            WarpingBack,
            WalkingToPlayer,
            Finished
        }

        private ActionState _currentState = ActionState.Idle;
        private NPC? _activeNpc;
        private string _targetLocationName = "";
        private Action? _onCompleteCallback;
        
        // Memoria de la posición original
        private string _originalLocationName = "";
        private Point _originalTile;

        private double _stateTimer = 0;

        public NPCActionController(IMonitor logger, IModHelper helper)
        {
            _logger = logger;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
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

            _originalLocationName = npc.currentLocation.NameOrUniqueName;
            _originalTile = npc.TilePoint;

            _currentState = ActionState.WarpingToTarget;
            _stateTimer = 500; // Medio segundo antes de warpear
            
            _logger.Log($"[ActionController] {npc.Name} inicia inspección hacia {_targetLocationName}. Origen: {_originalLocationName}", LogLevel.Info);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (_currentState == ActionState.Idle || _activeNpc == null) return;

            switch (_currentState)
            {
                case ActionState.WarpingToTarget:
                    _stateTimer -= Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
                    if (_stateTimer <= 0)
                    {
                        var targetLoc = ResolveLocation(_targetLocationName);
                        if (targetLoc != null)
                        {
                            // Encontrar un tile seguro genérico cerca del centro (como ejemplo)
                            Point safeTile = new Point(targetLoc.Map.DisplayWidth / Game1.tileSize / 2, targetLoc.Map.DisplayHeight / Game1.tileSize / 2);
                            Game1.warpCharacter(_activeNpc, targetLoc.NameOrUniqueName, safeTile);
                            
                            _logger.Log($"[ActionController] {_activeNpc.Name} llegó a {targetLoc.NameOrUniqueName}. Iniciando inspección.", LogLevel.Info);
                            _currentState = ActionState.Inspecting;
                            _stateTimer = 4000; // 4 segundos de inspección
                            
                            // Hacer que camine aleatoriamente
                            _activeNpc.controller = new PathFindController(_activeNpc, targetLoc, new Point(safeTile.X + Game1.random.Next(-3, 3), safeTile.Y + Game1.random.Next(-3, 3)), 0);
                        }
                        else
                        {
                            _logger.Log($"[ActionController] La ubicación {_targetLocationName} no existe. Cancelando.", LogLevel.Error);
                            FinishAction();
                        }
                    }
                    break;

                case ActionState.Inspecting:
                    _stateTimer -= Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
                    if (_stateTimer <= 0)
                    {
                        _logger.Log($"[ActionController] Inspección terminada. Regresando a {_originalLocationName}.", LogLevel.Info);
                        _currentState = ActionState.WarpingBack;
                        _stateTimer = 500;
                    }
                    break;

                case ActionState.WarpingBack:
                    _stateTimer -= Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
                    if (_stateTimer <= 0)
                    {
                        var origLoc = ResolveLocation(_originalLocationName);
                        if (origLoc != null)
                        {
                            Game1.warpCharacter(_activeNpc, origLoc.NameOrUniqueName, _originalTile);
                            
                            _currentState = ActionState.WalkingToPlayer;
                            _logger.Log($"[ActionController] {_activeNpc.Name} regresó. Caminando hacia el jugador.", LogLevel.Info);
                        }
                        else
                        {
                            FinishAction();
                        }
                    }
                    break;

                case ActionState.WalkingToPlayer:
                    // Nos aseguramos de que camine hacia donde está el jugador si están en el mismo mapa
                    if (_activeNpc.currentLocation == Game1.player.currentLocation)
                    {
                        Point playerTile = Game1.player.TilePoint;
                        
                        // Caminar hasta quedar a 1 tile del jugador
                        _activeNpc.controller = new PathFindController(_activeNpc, _activeNpc.currentLocation, playerTile, 1, delegate(Character c, GameLocation l) {
                            FinishAction();
                        });
                        
                        // Si no pudo generar ruta, forzamos término para no trabar el ciclo
                        if (_activeNpc.controller == null || _activeNpc.controller.pathToEndPoint == null)
                        {
                            FinishAction();
                        }
                        else
                        {
                            // Dejar que el PathFindController termine y llame al delegate
                            _currentState = ActionState.Finished;
                        }
                    }
                    else
                    {
                        FinishAction();
                    }
                    break;
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
