using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Pathfinding;

namespace StardewLivingValley.Brain
{
    public class Cerebellum
    {
        private readonly IMonitor _logger;
        private readonly IModHelper _helper;
        
        private enum ActionState
        {
            Idle,
            WalkingToTarget,
            Inspecting,
            WalkingBack,
            ChasingPlayer,
            Finished
        }

        private ActionState _currentState = ActionState.Idle;
        private NPC? _activeNpc;
        private string _targetLocationName = "";
        private Action? _onCompleteCallback;
        private Action<string>? _onDataGatheredCallback;
        private Hippocampus? _Hippocampus;
        private SensoryCortex? _SensoryCortex;
        
        private string _originalPlayerMap = "";
        private Point _originalPlayerTile;
        private double _stateTimer = 0;
        private string _inspectionReport = "";
        private bool _originalDestroyObjects = true;

        // Sistema de estabilizaciÃƒÆ’Ã‚Â³n post-warp
        private int _postWarpTicks = 0;
        private Action? _postWarpAction = null;
        private int _routingGraceTicks = 0;

        // DetecciÃƒÆ’Ã‚Â³n de warps automÃƒÆ’Ã‚Â¡ticos del engine
        private string _npcStartMap = "";

        // Auto-recuperaciÃƒÆ’Ã‚Â³n de atascos
        private Point _lastPosition;
        private int _ticksStuck = 0;
        private int _maxRetries = 3;
        private int _currentRetries = 0;

        public Cerebellum(IMonitor logger, IModHelper helper)
        {
            _logger = logger;
            _helper = helper;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        }

        public void SetHippocampus(Hippocampus Hippocampus)
        {
            _Hippocampus = Hippocampus;
        }

        public void SetSensoryCortex(SensoryCortex SensoryCortex)
        {
            _SensoryCortex = SensoryCortex;
        }

        public string GetLastInspectionReport()
        {
            return _inspectionReport;
        }

        public void StartInspection(NPC npc, string targetLocation, Action<string> onDataGathered, Action onComplete)
        {
            if (_currentState != ActionState.Idle)
            {
                _logger.Log($"[ActionController] El NPC {npc.Name} ya está ejecutando una acción.", LogLevel.Warn);
                return;
            }

            // Guarda defensiva: si otro mod ya controla este NPC, no interferir
            if (npc.controller != null)
            {
                _logger.Log($"[ActionController] {npc.Name} ya tiene un PathFindController activo (posiblemente de otro mod). Abortando para evitar conflictos.", LogLevel.Warn);
                return;
            }

            _activeNpc = npc;
            _targetLocationName = targetLocation;
            _onDataGatheredCallback = onDataGathered;
            _onCompleteCallback = onComplete;

            _originalPlayerMap = Game1.player.currentLocation.NameOrUniqueName;
            _originalPlayerTile = Game1.player.TilePoint;

            _currentState = ActionState.WalkingToTarget;
            _stateTimer = 0;
            _ticksStuck = 0;
            _currentRetries = 0;
            _lastPosition = npc.TilePoint;

            // Proteger objetos del jugador (aspersores, máquinas, etc.) durante la misión
            _originalDestroyObjects = npc.willDestroyObjectsUnderfoot;
            npc.willDestroyObjectsUnderfoot = false;
            
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
                 // Reversión: el usuario pidió explícitamente que el NPC vuelva a la posición guardada
                 // en lugar de rastrear mágicamente el movimiento actual del jugador.
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
                            var exitResult = MotorCortex.FindPath(npc, npc.currentLocation, npc.TilePoint, new Point(exitWarp.X, exitWarp.Y), 5000);
                            var exitPath = exitResult.Path;
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
                       
                       // MotorCortex reemplaza la lógica anticuada de colisiones por físicas nativas
                       var pathResult = MotorCortex.FindPath(npc, npc.currentLocation, npc.TilePoint, targetTile, 10000);
                       var localPath = pathResult.Path;

                       if (localPath != null && localPath.Count > 0)
                       {
                           if (pathResult.IsPartial)
                           {
                                _logger.Log($"[ActionController] Ruta local PARCIAL encontrada. Destino bloqueado.", LogLevel.Warn);
                                _npcStartMap = npc.currentLocation.NameOrUniqueName;
                                npc.controller = new PathFindController(localPath, npc, npc.currentLocation)
                                {
                                     finalFacingDirection = 2,
                                     endBehaviorFunction = (c, l) => {
                                         // Al llegar al punto parcial, escaneamos qué lo bloquea mirando hacia el objetivo original
                                         string obstacle = MotorCortex.IdentifyObstacle(npc.currentLocation, targetTile);
                                         _logger.Log($"[ActionController] Llegó al tope parcial. Obstáculo detectado: {obstacle}", LogLevel.Warn);

                                         _currentState = ActionState.WalkingBack;
                                         StoreAbandonmentMemoryBlocked(obstacle);

                                         if (!TryStartNativeRouting(npc, _originalPlayerMap, true))
                                         {
                                             StoreAbandonmentMemory();
                                             FinishAction();
                                         }
                                     }
                                };
                                _routingGraceTicks = 10;
                           }
                           else
                           {
                                _logger.Log($"[ActionController] Ruta local COMPLETA encontrada: {localPath.Count} pasos.", LogLevel.Info);
                                _npcStartMap = npc.currentLocation.NameOrUniqueName;
                                npc.controller = new PathFindController(localPath, npc, npc.currentLocation)
                                {
                                     finalFacingDirection = 2,
                                     endBehaviorFunction = OnRouteFinished
                                };
                                _routingGraceTicks = 10;
                           }
                      }
                      else
                      {
                           if (isReturning && targetMap == Game1.player.currentLocation?.NameOrUniqueName)
                           {
                               _logger.Log($"[ActionController] MotorCortex falló hacia el tile original en {targetMap}, pero estamos en el mismo mapa que el jugador. Pasando a persecución directa (ChasingPlayer).", LogLevel.Warn);
                               _currentState = ActionState.ChasingPlayer;
                           }
                           else
                           {
                               _logger.Log($"[ActionController] MotorCortex falló completamente en {targetMap} hacia {targetTile}. Cancelando misión.", LogLevel.Warn);
                               if (isReturning)
                               {
                                   StoreAbandonmentMemory();
                                   FinishAction();
                               }
                               else
                               {
                                   _currentState = ActionState.WalkingBack;
                                   StoreAbandonmentMemoryBlocked("un misterioso bloqueo inexplicable");
                                   if (!TryStartNativeRouting(npc, _originalPlayerMap, true))
                                   {
                                       StoreAbandonmentMemory();
                                       FinishAction();
                                   }
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
                       
                       // Caso especial: Farm -> FarmHouse (la puerta no es un warp regular, es una BuildingDoor)
                       if (targetMap == "FarmHouse" && npc.currentLocation.Name == "Farm")
                       {
                            _logger.Log($"[ActionController] Caso especial Farm->FarmHouse. Buscando puerta del edificio...", LogLevel.Info);
                            var farmHouseLoc = Game1.getLocationFromName("FarmHouse");
                            if (farmHouseLoc != null)
                            {
                                 // Buscar el warp DE SALIDA del FarmHouse (apunta a Farm) para saber dónde está la puerta en Farm
                                 Point farmDoorOnFarm = new Point(64, 15); // Posición por defecto
                                 foreach (var w in farmHouseLoc.warps)
                                 {
                                      if (w.TargetName == "Farm")
                                      {
                                           farmDoorOnFarm = new Point(w.TargetX, w.TargetY);
                                           _logger.Log($"[ActionController] Puerta de FarmHouse en Farm encontrada en {farmDoorOnFarm}", LogLevel.Info);
                                           break;
                                      }
                                 }
                                 
                                 var resultToDoor = MotorCortex.FindPath(npc, npc.currentLocation, npc.TilePoint, farmDoorOnFarm, 30000);
                                 var pathToDoor = resultToDoor.Path;
                                 if (pathToDoor == null || pathToDoor.Count == 0)
                                      pathToDoor = PathFindController.findPathForNPCSchedules(npc.TilePoint, farmDoorOnFarm, npc.currentLocation, 50000, npc);
                                 
                                 if (pathToDoor != null && pathToDoor.Count > 0)
                                 {
                                      _logger.Log($"[ActionController] Ruta a puerta de FarmHouse: {pathToDoor.Count} pasos.", LogLevel.Info);
                                      _npcStartMap = npc.currentLocation.NameOrUniqueName;
                                      npc.controller = new PathFindController(pathToDoor, npc, npc.currentLocation)
                                      {
                                           finalFacingDirection = 0,
                                           endBehaviorFunction = (c, l) => {
                                                _logger.Log($"[ActionController] NPC llegó a la puerta de FarmHouse. Entrando...", LogLevel.Info);
                                                var fh = Game1.getLocationFromName("FarmHouse") as FarmHouse;
                                                Point entry = fh?.getEntryLocation() ?? new Point(27, 30);
                                                Game1.warpCharacter(npc, "FarmHouse", entry);
                                                _postWarpTicks = 15;
                                                _postWarpAction = () => TryStartNativeRouting(npc, targetName, isReturning);
                                           }
                                      };
                                      return true;
                                 }
                                 else
                                 {
                                      _logger.Log($"[ActionController] No se encontró ruta a la puerta de FarmHouse en {farmDoorOnFarm}.", LogLevel.Warn);
                                 }
                            }
                       }
                      
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
            _currentRetries = 0; // Reiniciar retries al finalizar la ruta

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
                 if (_activeNpc != null && Game1.player.currentLocation != null && _activeNpc.currentLocation.NameOrUniqueName == Game1.player.currentLocation.NameOrUniqueName)
                 {
                     _currentState = ActionState.ChasingPlayer;
                 }
                 else
                 {
                     _currentState = ActionState.Finished;
                 }
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
                case ActionState.WalkingBack:
                     // Auto-recuperación de atascos
                     if (_activeNpc.controller != null || _activeNpc.DirectionsToNewLocation != null)
                     {
                         if (_activeNpc.TilePoint == _lastPosition)
                         {
                             _ticksStuck++;
                         }
                         else
                         {
                             _lastPosition = _activeNpc.TilePoint;
                             _ticksStuck = 0;
                         }

                         // Si está atascado por ~3 segundos (180 ticks, o digamos 60 para 1 segundo, elegimos 180 para dar tiempo a moverse un poco y no ser muy agresivos)
                         if (_ticksStuck > 180)
                         {
                             _logger.Log($"[ActionController] {_activeNpc.Name} parece atascado en {_activeNpc.TilePoint} durante 3 segundos. Intentando recuperar...", LogLevel.Warn);

                             _ticksStuck = 0;
                             _activeNpc.Halt();
                             _activeNpc.controller = null;
                             _activeNpc.DirectionsToNewLocation = null;

                             if (_currentRetries >= _maxRetries)
                             {
                                 _logger.Log($"[ActionController] Se superó el máximo de intentos de recuperación ({_maxRetries}). Abortando misión.", LogLevel.Error);
                                 if (_currentState == ActionState.WalkingToTarget)
                                 {
                                     StoreAbandonmentMemoryBlocked("un obstáculo inamovible");
                                 }
                                 else
                                 {
                                     StoreAbandonmentMemory();
                                 }
                                 FinishAction();
                                 return;
                             }

                             _currentRetries++;
                             bool isReturning = _currentState == ActionState.WalkingBack;
                             string target = isReturning ? _originalPlayerMap : _targetLocationName;

                             _logger.Log($"[ActionController] Recalculando ruta hacia {target} (Intento {_currentRetries}/{_maxRetries})...", LogLevel.Info);
                             if (!TryStartNativeRouting(_activeNpc, target, isReturning))
                             {
                                 _logger.Log($"[ActionController] Fallo fatal al recalcular tras atasco. Cancelando.", LogLevel.Error);
                                 if (isReturning) StoreAbandonmentMemory();
                                 else StoreAbandonmentMemoryBlocked("un camino cerrado");
                                 FinishAction();
                             }
                             return;
                         }
                     }

                     if (_activeNpc.DirectionsToNewLocation == null && _activeNpc.controller == null)
                     {
                         string currentMap = _activeNpc.currentLocation?.NameOrUniqueName ?? "";
                         
                         // ¿El engine warpeó al NPC automáticamente al pisar un warp tile?
                         if (!string.IsNullOrEmpty(_npcStartMap) && currentMap != _npcStartMap && !string.IsNullOrEmpty(currentMap))
                         {
                             string targetMap = _currentState == ActionState.WalkingBack ? _originalPlayerMap : _targetLocationName;
                             _logger.Log($"[ActionController] Warp automático detectado: {_npcStartMap} -> {currentMap}. Re-enrutando hacia {targetMap}...", LogLevel.Info);
                             _npcStartMap = currentMap;
                             _postWarpTicks = 15;
                             _postWarpAction = () => TryStartNativeRouting(_activeNpc!, targetMap, _currentState == ActionState.WalkingBack);
                         }
                         else
                         {
                             if (_currentState == ActionState.WalkingToTarget)
                                 _logger.Log($"[ActionController] WalkingToTarget: controller es null. NPC en '{currentMap}' tile {_activeNpc.TilePoint}. Llamando OnRouteFinished.", LogLevel.Warn);

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
                        _onDataGatheredCallback?.Invoke(_inspectionReport);
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

                case ActionState.ChasingPlayer:
                    if (_activeNpc == null || Game1.player.currentLocation == null) return;
                    if (_activeNpc.currentLocation.NameOrUniqueName != Game1.player.currentLocation.NameOrUniqueName)
                    {
                        StoreAbandonmentMemory();
                        FinishAction();
                        break;
                    }
                    float distance = Vector2.Distance(_activeNpc.Tile, Game1.player.Tile);
                    if (distance <= 2f)
                    {
                        _activeNpc.Halt();
                        _activeNpc.faceGeneralDirection(Game1.player.Position, 0, false, false);
                        _currentState = ActionState.Finished;
                        FinishAction();
                    }
                    else
                    {
                        _stateTimer -= Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
                        if (_stateTimer <= 0)
                        {
                            _stateTimer = 1000;
                            _activeNpc.controller = new PathFindController(_activeNpc, _activeNpc.currentLocation, Game1.player.TilePoint, -1, (c, l) => { });
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
            if (_activeNpc != null && _Hippocampus != null)
            {
                string baseMsg = $"Fui a revisar {_targetLocationName} como me pediste. ";
                if (!string.IsNullOrEmpty(_inspectionReport))
                {
                    baseMsg += $"{_inspectionReport} Sin embargo, cuando regresé a buscarte al lugar donde iniciamos la charla para darte el reporte, ya te habías ido, así que tuve que seguir con mis rutinas.";
                }
                else
                {
                    baseMsg += "Pero cuando regresé a buscarte al lugar donde te vi por última vez, ya no estabas ahí. Tuve que seguir con mis cosas.";
                }
                _Hippocampus.SaveNpcMemory(_activeNpc.Name, baseMsg);
            }
        }

        private void StoreAbandonmentMemoryBlocked(string obstacleName)
        {
            if (_activeNpc != null && _Hippocampus != null)
            {
                _Hippocampus.SaveNpcMemory(_activeNpc.Name, $"Intenté ir a revisar {_targetLocationName} como me pediste, pero el camino estaba completamente bloqueado por {obstacleName} y no pude pasar. Tuve que regresar de inmediato.");
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
                try
                {
                    // Restaurar la propiedad de destrucción de objetos
                    _activeNpc.willDestroyObjectsUnderfoot = _originalDestroyObjects;
                    _activeNpc.DirectionsToNewLocation = null;
                    if (_activeNpc.controller != null) _activeNpc.controller = null;
                    _activeNpc.checkSchedule(Game1.timeOfDay); // Resume regular schedule
                }
                catch (Exception ex)
                {
                    _logger.Log($"[ActionController] Error al restaurar estado de {_activeNpc.Name}: {ex.Message}. El NPC debería recuperarse en el próximo cambio de hora.", LogLevel.Warn);
                }
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
