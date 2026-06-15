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
        private readonly IModHelper _helper;
        
        private enum ActionState
        {
            Idle,
            WalkingToTarget,
            Inspecting,
            WalkingBack,
            Finished
        }

        private ActionState _currentState = ActionState.Idle;
        private NPC? _activeNpc;
        private string _targetLocationName = "";
        private Action? _onCompleteCallback;
        private MemoryService? _memoryService;
        
        private string _originalPlayerMap = "";
        private Point _originalPlayerTile;
        private double _stateTimer = 0;

        public NPCActionController(IMonitor logger, IModHelper helper)
        {
            _logger = logger;
            _helper = helper;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        }

        public void SetMemoryService(MemoryService memoryService)
        {
            _memoryService = memoryService;
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

            _originalPlayerMap = Game1.player.currentLocation.NameOrUniqueName;
            _originalPlayerTile = Game1.player.TilePoint;

            _currentState = ActionState.WalkingToTarget;
            _stateTimer = 0;
            
            _logger.Log($"[ActionController] {npc.Name} inicia inspección hacia {_targetLocationName}. Origen: {npc.currentLocation.NameOrUniqueName}", LogLevel.Info);

            if (!TryStartNativeRouting(_activeNpc, _targetLocationName, false))
            {
                _logger.Log($"[ActionController] No se pudo crear ruta nativa hacia {_targetLocationName}. Cancelando.", LogLevel.Error);
                FinishAction();
            }
        }

        public bool IsNpcOnMission(NPC npc)
        {
            return _activeNpc == npc && _currentState != ActionState.Idle;
        }

        private bool TryStartNativeRouting(NPC npc, string targetName, bool isReturning)
        {
            Point targetTile = Point.Zero;
            string targetMap = targetName;

            if (isReturning)
            {
                targetMap = _originalPlayerMap;
                targetTile = _originalPlayerTile;
            }
            else
            {
                var loc = Game1.getLocationFromName(targetName);
                if (loc != null)
                {
                     targetTile = GetSafeTile(loc);
                }
                else
                {
                    var farm = Game1.getLocationFromName("Farm");
                    if (farm != null)
                    {
                        foreach (var building in farm.buildings)
                        {
                            if (building.indoors.Value != null &&
                               (building.indoors.Value.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase) ||
                                building.indoors.Value.NameOrUniqueName.Equals(targetName, StringComparison.OrdinalIgnoreCase) ||
                                (targetName.Equals("Coop", StringComparison.OrdinalIgnoreCase) && building.buildingType.Value.Contains("Coop", StringComparison.OrdinalIgnoreCase)) ||
                                (targetName.Equals("Barn", StringComparison.OrdinalIgnoreCase) && building.buildingType.Value.Contains("Barn", StringComparison.OrdinalIgnoreCase))))
                            {
                                targetMap = "Farm";
                                targetTile = new Point(building.tileX.Value + building.doorX.Value, building.tileY.Value + building.doorY.Value);
                                break;
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(targetMap)) return false;

            try
            {
                 if (npc.currentLocation.NameOrUniqueName == targetMap)
                 {
                      npc.controller = new PathFindController(npc, npc.currentLocation, targetTile, -1, OnRouteFinished);
                      return true;
                 }
                 else
                 {
                      var routeDictionary = PathFindController.findPathForNPCSchedules(npc.TilePoint, npc.currentLocation, targetTile, Game1.getLocationFromName(targetMap), 10000);
                      if (routeDictionary != null)
                      {
                           // Stardew Valley Schedule format: key is time, value is SchedulePathDescription
                           var tempSchedule = new Dictionary<int, SchedulePathDescription>();
                           int currentTime = Game1.timeOfDay;

                           SchedulePathDescription spd = new SchedulePathDescription(new Stack<Point>(), 2, 0, "");

                           // Utility.parseStringToIntArray for paths usually handled internally.
                           // But since routeDictionary is already a map of Location -> RouteString,
                           // We can use the constructor that parses the whole route dictionary.
                           // Actually, SchedulePathDescription doesn't have a constructor for the dictionary.
                           // NPC.parseMasterSchedule handles parsing the string into route instructions.

                           // The easiest native way to force a cross map movement via Schedule is to just set Schedule = schedule and checkSchedule.
                           // But doing this dynamically is hard since we must parse the raw route Dictionary<string, string> into the Stack<Point>.
                           // Instead of trying to reinvent Schedule parsing, we can just use the schedule format and inject it.
                           // The schedule parser takes a raw string format: "a_time targetMap x y facingDirection message"

                           string scheduleCommand = $"{targetMap} {targetTile.X} {targetTile.Y} 2";

                           // Wait, the simplest way is to manually do the route dictionary execution.
                           npc.DirectionsToNewLocation = Utility.getRouteToLocation(npc.currentLocation, npc.TilePoint, Game1.getLocationFromName(targetMap), targetTile, 10000, npc.Name);

                           if (npc.DirectionsToNewLocation != null)
                           {
                               // This gives us a proper SchedulePathDescription that crosses maps!
                               // The game engine will automatically pop it from DirectionsToNewLocation and move the NPC.
                               return true;
                           }
                      }
                 }
            }
            catch (Exception ex)
            {
                _logger.Log($"Error creando ruta: {ex.Message}", LogLevel.Error);
            }

            return false;
        }

        private Point GetSafeTile(GameLocation loc)
        {
             return new Point(loc.Map.DisplayWidth / Game1.tileSize / 2, loc.Map.DisplayHeight / Game1.tileSize / 2);
        }

        private void OnRouteFinished(Character c, GameLocation l)
        {
            if (_currentState == ActionState.WalkingToTarget)
            {
                 if (_activeNpc != null && _activeNpc.currentLocation.Name == "Farm")
                 {
                     foreach (var building in _activeNpc.currentLocation.buildings)
                     {
                         if (building.indoors.Value != null &&
                            Math.Abs(_activeNpc.TilePoint.X - (building.tileX.Value + building.doorX.Value)) <= 1 &&
                            Math.Abs(_activeNpc.TilePoint.Y - (building.tileY.Value + building.doorY.Value)) <= 1)
                         {
                              Game1.warpCharacter(_activeNpc, building.indoors.Value.NameOrUniqueName, new Point(building.indoors.Value.warps.Count > 0 ? building.indoors.Value.warps[0].X : 2, building.indoors.Value.warps.Count > 0 ? building.indoors.Value.warps[0].Y : 2));
                              break;
                         }
                     }
                 }

                 _currentState = ActionState.Inspecting;
                 _stateTimer = 4000;
            }
            else if (_currentState == ActionState.WalkingBack)
            {
                 _currentState = ActionState.Finished;
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (_currentState == ActionState.Idle || _activeNpc == null) return;

            switch (_currentState)
            {
                case ActionState.WalkingToTarget:
                     if (_activeNpc.DirectionsToNewLocation == null && _activeNpc.controller == null)
                     {
                         OnRouteFinished(_activeNpc, _activeNpc.currentLocation);
                     }
                     break;

                case ActionState.WalkingBack:
                     if (_activeNpc.DirectionsToNewLocation == null && _activeNpc.controller == null)
                     {
                         OnRouteFinished(_activeNpc, _activeNpc.currentLocation);
                     }
                     break;

                case ActionState.Inspecting:
                    _stateTimer -= Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
                    if (_stateTimer <= 0)
                    {
                        // Exit building if inside
                        if (_activeNpc.currentLocation.Name.StartsWith("Coop") || _activeNpc.currentLocation.Name.StartsWith("Barn") || _activeNpc.currentLocation.Name.StartsWith("Shed") || _activeNpc.currentLocation.Name.StartsWith("SlimeHutch"))
                        {
                            var farm = Game1.getLocationFromName("Farm");
                            foreach(var b in farm.buildings)
                            {
                                if (b.indoors.Value != null && b.indoors.Value.NameOrUniqueName == _activeNpc.currentLocation.NameOrUniqueName)
                                {
                                     Game1.warpCharacter(_activeNpc, "Farm", new Point(b.tileX.Value + b.doorX.Value, b.tileY.Value + b.doorY.Value + 1));
                                     break;
                                }
                            }
                        }

                        _logger.Log($"[ActionController] Inspección terminada. Calculando ruta de regreso a {_originalPlayerMap}.", LogLevel.Info);
                        _currentState = ActionState.WalkingBack;
                        if (!TryStartNativeRouting(_activeNpc, _originalPlayerMap, true))
                        {
                            _logger.Log($"[ActionController] No se pudo volver. Guardando memoria.", LogLevel.Warn);
                            StoreAbandonmentMemory();
                            FinishAction();
                        }
                    }
                    break;

                case ActionState.Finished:
                    if (Game1.player.currentLocation.NameOrUniqueName != _activeNpc.currentLocation.NameOrUniqueName || Vector2.Distance(Game1.player.Tile, _activeNpc.Tile) > 15f)
                    {
                        StoreAbandonmentMemory();
                    }
                    FinishAction();
                    break;
            }
        }

        private void StoreAbandonmentMemory()
        {
            if (_activeNpc != null && _memoryService != null)
            {
                _memoryService.SavePlayerMemory(_activeNpc.Name, $"Fui a revisar {_targetLocationName} como me pediste, pero cuando regresé a buscarte al lugar donde te vi por última vez, ya no estabas ahí. Tuve que seguir con mis cosas.");
            }
        }

        private void FinishAction()
        {
            _logger.Log($"[ActionController] Acción de {_activeNpc?.Name} terminada.", LogLevel.Info);
            _currentState = ActionState.Idle;
            
            _onCompleteCallback?.Invoke();
            _onCompleteCallback = null;
            
            if (_activeNpc != null)
            {
                 _activeNpc.DirectionsToNewLocation = null;
                 _activeNpc.controller = null;
                 _activeNpc.checkSchedule(Game1.timeOfDay); // Resume regular schedule
            }
            _activeNpc = null;
        }
    }
}
