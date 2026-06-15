using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
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
        private ObservationEngine? _observationEngine;
        
        private string _originalPlayerMap = "";
        private Point _originalPlayerTile;
        private double _stateTimer = 0;
        private string _inspectionReport = "";

        // Sistema de estabilización post-warp
        private int _postWarpTicks = 0;
        private Action? _postWarpAction = null;
        private int _routingGraceTicks = 0;

        // Detección de warps automáticos del engine
        private string _npcStartMap = "";

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

        public void SetObservationEngine(ObservationEngine observationEngine)
        {
            _observationEngine = observationEngine;
        }

        public string GetLastInspectionReport()
        {
            return _inspectionReport;
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
                 // Bug 4: Usar posición ACTUAL del jugador, no la guardada
                 targetMap = Game1.player.currentLocation?.NameOrUniqueName ?? _originalPlayerMap;
                 targetTile = Game1.player.TilePoint;
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
                                targetTile = building.getPointForHumanDoor();
                                targetTile.Y += 1; // Un tile debajo de la puerta para que sea caminable
                                break;
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(targetMap)) return false;

             try
             {
                  // Detectar si el NPC está en un interior de edificio y necesita salir a Farm primero
                  if (npc.currentLocation.NameOrUniqueName != targetMap && IsInsideFarmBuilding(npc.currentLocation))
                  {
                       _logger.Log($"[ActionController] NPC está dentro de un edificio '{npc.currentLocation.NameOrUniqueName}'. Buscando warp de salida...", LogLevel.Info);
                       
                       // Buscar el warp de salida del edificio (siempre va a Farm)
                       Warp? exitWarp = null;
                       foreach (var w in npc.currentLocation.warps)
                       {
                            exitWarp = w;
                            break; // El primer warp de un edificio siempre es la salida
                       }
                       
                       if (exitWarp != null)
                       {
                            var exitPath = SmartPathfinder.FindPath(npc, npc.currentLocation, npc.TilePoint, new Point(exitWarp.X, exitWarp.Y), 5000, 2);
                            if (exitPath == null || exitPath.Count == 0)
                                 exitPath = PathFindController.findPathForNPCSchedules(npc.TilePoint, new Point(exitWarp.X, exitWarp.Y), npc.currentLocation, 10000, npc);
                            
                            if (exitPath != null && exitPath.Count > 0)
                            {
                                 _logger.Log($"[ActionController] Ruta de salida encontrada: {exitPath.Count} pasos hacia warp {new Point(exitWarp.X, exitWarp.Y)}", LogLevel.Info);
                                 _npcStartMap = npc.currentLocation.NameOrUniqueName;
                                 npc.controller = new PathFindController(exitPath, npc, npc.currentLocation)
                                 {
                                      finalFacingDirection = 2,
                                      endBehaviorFunction = (c, l) => {
                                           _logger.Log($"[ActionController] NPC llegó al warp de salida del edificio. Saliendo a {exitWarp.TargetName}...", LogLevel.Info);
                                           Game1.warpCharacter(npc, exitWarp.TargetName, new Vector2(exitWarp.TargetX, exitWarp.TargetY));
                                           _postWarpTicks = 15;
                                           _postWarpAction = () => TryStartNativeRouting(npc, targetName, isReturning);
                                      }
                                 };
                                 _routingGraceTicks = 10;
                                 return true;
                            }
                       }
                       
                       // Fallback: teletransportar a la puerta del edificio en Farm
                       _logger.Log($"[ActionController] No se pudo encontrar ruta de salida del edificio. Teletransportando a Farm.", LogLevel.Warn);
                       var farm = Game1.getLocationFromName("Farm");
                       if (farm != null)
                       {
                            foreach (var building in farm.buildings)
                            {
                                 if (building.indoors.Value != null && building.indoors.Value.NameOrUniqueName == npc.currentLocation.NameOrUniqueName)
                                 {
                                      Game1.warpCharacter(npc, "Farm", new Point(building.getPointForHumanDoor().X, building.getPointForHumanDoor().Y + 1));
                                      _postWarpTicks = 15;
                                      _postWarpAction = () => TryStartNativeRouting(npc, targetName, isReturning);
                                      return true;
                                 }
                            }
                       }
                  }

                  if (npc.currentLocation.NameOrUniqueName == targetMap)
                  {
                       _logger.Log($"[ActionController] Buscando ruta local en '{targetMap}': NPC en {npc.TilePoint} hacia {targetTile}", LogLevel.Info);
                       
                       // SmartPathfinder primero (respeta colisiones con vallas y objetos del jugador)
                       var localPath = SmartPathfinder.FindPath(npc, npc.currentLocation, npc.TilePoint, targetTile, 15000, 3);
                       
                       // Fallback a pathfinder nativo
                       if (localPath == null || localPath.Count == 0)
                       {
                            _logger.Log($"[ActionController] SmartPathfinder falló. Intentando pathfinder nativo...", LogLevel.Info);
                            localPath = PathFindController.findPathForNPCSchedules(npc.TilePoint, targetTile, npc.currentLocation, 50000, npc);
                       }

                      if (localPath != null && localPath.Count > 0)
                      {
                           _logger.Log($"[ActionController] Ruta local encontrada: {localPath.Count} pasos.", LogLevel.Info);
                           _npcStartMap = npc.currentLocation.NameOrUniqueName;
                           npc.controller = new PathFindController(localPath, npc, npc.currentLocation)
                           {
                                finalFacingDirection = 2,
                                endBehaviorFunction = OnRouteFinished
                           };
                           _routingGraceTicks = 10;
                      }
                      else
                      {
                           _logger.Log($"[ActionController] SmartPathfinder también falló en {targetMap} hacia {targetTile}. Cancelando misión.", LogLevel.Warn);
                           if (isReturning)
                           {
                               StoreAbandonmentMemory();
                               FinishAction();
                           }
                           else
                           {
                               _currentState = ActionState.WalkingBack;
                               StoreAbandonmentMemoryBlocked();
                               if (!TryStartNativeRouting(npc, _originalPlayerMap, true))
                               {
                                   StoreAbandonmentMemory();
                                   FinishAction();
                               }
                           }
                      }
                      return true;
                 }
                 else
                 {
                      var pathDescription = npc.pathfindToNextScheduleLocation(
                          "temporary_mission",
                          npc.currentLocation.NameOrUniqueName,
                          npc.TilePoint.X,
                          npc.TilePoint.Y,
                          targetMap,
                          targetTile.X,
                          targetTile.Y,
                          2, // final facing direction
                          null,
                          null
                      );
                      if (pathDescription != null && pathDescription.route != null && pathDescription.route.Count > 0)
                      {
                           npc.DirectionsToNewLocation = pathDescription;
                           _npcStartMap = npc.currentLocation.NameOrUniqueName;
                           npc.controller = new PathFindController(pathDescription.route, npc, npc.currentLocation)
                           {
                                finalFacingDirection = pathDescription.facingDirection,
                                endBehaviorFunction = OnRouteFinished
                           };
                           return true;
                      }
                      
                      _logger.Log($"[ActionController] No se encontró ruta nativa hacia {targetMap} desde {npc.currentLocation.NameOrUniqueName}. Usando fallback...", LogLevel.Warn);
                      
                      // Fallback 1: Buscar warp directo
                      Warp? directWarp = null;
                      foreach (var w in npc.currentLocation.warps) {
                           if (w.TargetName == targetMap) { directWarp = w; break; }
                      }
                      
                      if (directWarp != null) 
                      {
                           var routeToWarp = PathFindController.findPathForNPCSchedules(npc.TilePoint, new Point(directWarp.X, directWarp.Y), npc.currentLocation, 30000, npc);
                           if (routeToWarp != null && routeToWarp.Count > 0)
                           {
                                _logger.Log($"[ActionController] Caminando al warp en {new Point(directWarp.X, directWarp.Y)} para cruzar a {targetMap}.", LogLevel.Info);
                                _npcStartMap = npc.currentLocation.NameOrUniqueName;
                                npc.controller = new PathFindController(routeToWarp, npc, npc.currentLocation)
                                {
                                     finalFacingDirection = 2,
                                     endBehaviorFunction = (c, l) => {
                                          _logger.Log($"[ActionController] NPC llegó al warp. Teletransportando a {targetMap}...", LogLevel.Info);
                                          Game1.warpCharacter(npc, targetMap, new Vector2(directWarp.TargetX, directWarp.TargetY));
                                          _postWarpTicks = 15;
                                          _postWarpAction = () => TryStartNativeRouting(npc, targetName, isReturning);
                                     }
                                };
                                return true;
                           }
                      }
                      
                      // Fallback 2: Teletransporte directo si están muy lejos
                      _logger.Log($"[ActionController] Fallback a teletransporte directo hacia {targetMap}.", LogLevel.Info);
                      GameLocation targetGameLoc = Game1.getLocationFromName(targetMap);
                      if (targetGameLoc != null) 
                      {
                           Point warpPoint = GetSafeTile(targetGameLoc);
                           if (targetMap == "Farm") warpPoint = new Point(64, 15);
                           else if (targetMap == "Town") warpPoint = new Point(12, 54);
                           else if (targetMap == "FarmHouse") warpPoint = (targetGameLoc as FarmHouse)?.getEntryLocation() ?? warpPoint;
                           
                           Game1.warpCharacter(npc, targetMap, warpPoint);
                           _postWarpTicks = 15;
                           _postWarpAction = () => TryStartNativeRouting(npc, targetName, isReturning);
                           return true;
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
                            Math.Abs(_activeNpc.TilePoint.X - building.getPointForHumanDoor().X) <= 1 &&
                             Math.Abs(_activeNpc.TilePoint.Y - building.getPointForHumanDoor().Y) <= 2)
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
            // Estabilización post-warp: esperar a que el motor procese el cambio de mapa
            if (_postWarpTicks > 0)
            {
                _postWarpTicks--;
                if (_postWarpTicks == 0 && _postWarpAction != null)
                {
                    var action = _postWarpAction;
                    _postWarpAction = null;
                    _logger.Log($"[ActionController] Post-warp estabilizado. NPC en '{_activeNpc?.currentLocation?.NameOrUniqueName}' tile {_activeNpc?.TilePoint}. Ejecutando ruta local...", LogLevel.Info);
                    action();
                }
                return; // No verificar estado durante estabilización
            }

            // Período de gracia: no verificar controller==null justo después de asignar uno nuevo
            if (_routingGraceTicks > 0)
            {
                _routingGraceTicks--;
                return;
            }

            if (_currentState == ActionState.Idle || _activeNpc == null) return;

            switch (_currentState)
            {
                case ActionState.WalkingToTarget:
                     if (_activeNpc.DirectionsToNewLocation == null && _activeNpc.controller == null)
                     {
                         string currentMap = _activeNpc.currentLocation?.NameOrUniqueName ?? "";
                         
                         // ¿El engine warpeó al NPC automáticamente al pisar un warp tile?
                         if (!string.IsNullOrEmpty(_npcStartMap) && currentMap != _npcStartMap && !string.IsNullOrEmpty(currentMap))
                         {
                             _logger.Log($"[ActionController] Warp automático detectado: {_npcStartMap} → {currentMap}. Re-enrutando hacia {_targetLocationName}...", LogLevel.Info);
                             _npcStartMap = currentMap;
                             _postWarpTicks = 15;
                             _postWarpAction = () => TryStartNativeRouting(_activeNpc!, _targetLocationName, false);
                         }
                         else
                         {
                             _logger.Log($"[ActionController] WalkingToTarget: controller es null. NPC en '{currentMap}' tile {_activeNpc.TilePoint}. Llamando OnRouteFinished.", LogLevel.Warn);
                             OnRouteFinished(_activeNpc, _activeNpc.currentLocation!);
                         }
                     }
                     break;

                case ActionState.WalkingBack:
                     if (_activeNpc.DirectionsToNewLocation == null && _activeNpc.controller == null)
                     {
                         string currentMapBack = _activeNpc.currentLocation?.NameOrUniqueName ?? "";
                         
                         // ¿El engine warpeó al NPC automáticamente al pisar un warp tile?
                         if (!string.IsNullOrEmpty(_npcStartMap) && currentMapBack != _npcStartMap && !string.IsNullOrEmpty(currentMapBack))
                         {
                             _logger.Log($"[ActionController] Warp automático (regreso) detectado: {_npcStartMap} → {currentMapBack}. Re-enrutando hacia {_originalPlayerMap}...", LogLevel.Info);
                             _npcStartMap = currentMapBack;
                             _postWarpTicks = 15;
                             _postWarpAction = () => TryStartNativeRouting(_activeNpc!, _originalPlayerMap, true);
                         }
                         else
                         {
                             OnRouteFinished(_activeNpc, _activeNpc.currentLocation!);
                         }
                     }
                     break;

                case ActionState.Inspecting:
                    _stateTimer -= Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
                    if (_stateTimer <= 0)
                    {
                        // Escanear el interior del edificio para reportar datos REALES
                        _inspectionReport = ScanBuildingInterior(_activeNpc.currentLocation);
                        _logger.Log($"[ActionController] Reporte de inspección: {_inspectionReport}", LogLevel.Info);

                        _logger.Log($"[ActionController] Inspección terminada. Calculando ruta de regreso.", LogLevel.Info);
                        _currentState = ActionState.WalkingBack;
                        
                        // TryStartNativeRouting ahora detecta interiores y sale caminando por el warp
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

        private void StoreAbandonmentMemoryBlocked()
        {
            if (_activeNpc != null && _memoryService != null)
            {
                _memoryService.SavePlayerMemory(_activeNpc.Name, $"Intenté ir a revisar {_targetLocationName} como me pediste, pero el camino estaba completamente bloqueado por obstáculos físicos y no pude pasar. Tuve que regresar.");
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

        private bool IsInsideFarmBuilding(GameLocation location)
        {
            if (location == null) return false;
            
            // Verificar si es un AnimalHouse (Coop, Barn) u otro interior de edificio de granja
            if (location is StardewValley.AnimalHouse) return true;
            
            // Verificar por nombre (Shed, SlimeHutch, etc.)
            string name = location.Name ?? "";
            if (name.StartsWith("Shed") || name.StartsWith("SlimeHutch")) return true;
            
            // Verificar si la ubicación pertenece a un edificio de Farm
            var farm = Game1.getLocationFromName("Farm");
            if (farm != null)
            {
                foreach (var building in farm.buildings)
                {
                    if (building.indoors.Value != null && building.indoors.Value.NameOrUniqueName == location.NameOrUniqueName)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private string ScanBuildingInterior(GameLocation location)
        {
            if (location == null) return "No pude ver nada.";

            string report = "";
            
            // Escanear animales reales del juego
            if (location is StardewValley.AnimalHouse animalHouse)
            {
                var animals = animalHouse.animals.Values.ToList();
                if (animals.Count == 0)
                {
                    report = $"Revisé el {_targetLocationName} y no había ningún animal dentro.";
                }
                else
                {
                    // Agrupar animales por tipo
                    var grouped = new Dictionary<string, int>();
                    foreach (var animal in animals)
                    {
                        string type = animal.type.Value ?? "Desconocido";
                        if (grouped.ContainsKey(type))
                            grouped[type]++;
                        else
                            grouped[type] = 1;
                    }
                    
                    var details = new List<string>();
                    foreach (var kvp in grouped)
                    {
                        details.Add($"{kvp.Value} {kvp.Key}");
                    }
                    
                    report = $"Revisé el {_targetLocationName}. Encontré {animals.Count} animal(es): {string.Join(", ", details)}.";
                    
                    // Verificar si alguno necesita ser acariciado
                    int needsPetting = animals.Count(a => !a.wasPet.Value);
                    if (needsPetting > 0)
                    {
                        report += $" {needsPetting} de ellos no han sido acariciados hoy.";
                    }
                }
            }
            else
            {
                // Para Shed u otros edificios, reportar objetos
                int objectCount = location.objects.Count();
                report = $"Revisé el {_targetLocationName}. Hay {objectCount} objetos dentro.";
            }
            
            return report;
        }
    }
}
