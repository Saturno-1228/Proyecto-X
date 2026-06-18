using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Objects;
using StardewValley.Buildings;

namespace StardewLivingValley.Brain
{
    public static class MotorCortex
    {
        private static IMonitor? _logger;

        public static void SetLogger(IMonitor logger)
        {
            _logger = logger;
        }

        // Definimos una clase envoltorio para el resultado de la búsqueda
        public class PathResult
        {
            public Stack<Point>? Path { get; set; }
            public bool IsPartial { get; set; }
            public Point EndPoint { get; set; }
        }

        public static PathResult FindPath(NPC npc, GameLocation location, Point startTile, Point targetTile, int maxIterations = 10000)
        {
            if (location == null || npc == null) return new PathResult { Path = null };

            int width = location.Map.Layers[0].LayerWidth;
            int height = location.Map.Layers[0].LayerHeight;

            _logger?.Log($"[MotorCortex] Buscando ruta en {location.Name} de {startTile} a {targetTile}", LogLevel.Debug);

            // Siempre asumimos que donde estamos parados es caminable
            var walkableCache = new Dictionary<Point, bool>
            {
                [startTile] = true
            };

            // 1. Verificamos si el destino es exactamente caminable. Si no lo es, encontramos el punto más cercano válido.
            Point actualTarget = targetTile;
            bool isPartial = false;

            if (!IsTileWalkable(location, targetTile, targetTile, npc, walkableCache, width, height))
            {
                actualTarget = GetClosestWalkableTile(location, targetTile, npc, walkableCache, width, height, 5);
                isPartial = true;

                if (actualTarget.X == -1)
                {
                    _logger?.Log($"[MotorCortex] Destino completamente inaccesible y sin tiles cercanos válidos.", LogLevel.Warn);
                    return new PathResult { Path = null };
                }
                _logger?.Log($"[MotorCortex] Destino bloqueado. Ajustando a punto parcial {actualTarget}", LogLevel.Debug);
            }

            if (startTile == actualTarget)
            {
                 var singleStack = new Stack<Point>();
                 singleStack.Push(actualTarget);
                 return new PathResult { Path = singleStack, IsPartial = isPartial, EndPoint = actualTarget };
            }

            // A* normal
            var openSet = new PriorityQueue<Point, long>();
            var cameFrom = new Dictionary<Point, Point>();
            var gScore = new Dictionary<Point, int>();
            var closedSet = new HashSet<Point>();

            gScore[startTile] = 0;
            openSet.Enqueue(startTile, Heuristic(startTile, actualTarget));

            int iterations = 0;
            Point[] directions = {
                new Point(0, -1), new Point(0, 1),
                new Point(-1, 0), new Point(1, 0)
            };

            // Track partial progress in case we hit the iteration limit
            Point closestReached = startTile;
            int closestH = Heuristic(startTile, actualTarget);

            while (openSet.Count > 0)
            {
                if (iterations++ > maxIterations)
                {
                    _logger?.Log($"[MotorCortex] Límite de iteraciones alcanzado. Devolviendo ruta parcial hasta {closestReached}", LogLevel.Warn);
                    return new PathResult {
                        Path = ReconstructPath(cameFrom, closestReached),
                        IsPartial = true,
                        EndPoint = closestReached
                    };
                }

                Point current = openSet.Dequeue();

                if (current == actualTarget)
                {
                    return new PathResult {
                        Path = ReconstructPath(cameFrom, current),
                        IsPartial = isPartial,
                        EndPoint = current
                    };
                }

                if (closedSet.Contains(current)) continue;
                closedSet.Add(current);

                int currentH = Heuristic(current, actualTarget);
                if (currentH < closestH)
                {
                    closestH = currentH;
                    closestReached = current;
                }

                foreach (var dir in directions)
                {
                    Point neighbor = new Point(current.X + dir.X, current.Y + dir.Y);

                    if (closedSet.Contains(neighbor))
                        continue;

                    if (!IsTileWalkable(location, neighbor, actualTarget, npc, walkableCache, width, height))
                        continue;

                    int tentative_gScore = gScore[current] + GetTileCost(location, neighbor);

                    if (!gScore.ContainsKey(neighbor) || tentative_gScore < gScore[neighbor])
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentative_gScore;
                        int h = Heuristic(neighbor, actualTarget);
                        openSet.Enqueue(neighbor, tentative_gScore + h);
                    }
                }
            }

            _logger?.Log($"[MotorCortex] No se encontró ruta. Devolviendo ruta parcial hasta {closestReached}", LogLevel.Warn);
            return new PathResult {
                Path = ReconstructPath(cameFrom, closestReached),
                IsPartial = true,
                EndPoint = closestReached
            };
        }

        private static bool IsTileWalkable(GameLocation location, Point tile, Point targetTile, NPC npc, Dictionary<Point, bool> cache, int width, int height)
        {
            if (cache.TryGetValue(tile, out bool cachedWalkable))
                return cachedWalkable;

            // Si es exactamente la baldosa donde estamos ahora o el destino (ej. warps fuera del mapa), la forzamos
            if (npc.TilePoint == tile || targetTile == tile)
            {
                cache[tile] = true;
                return true;
            }

            if (tile.X < 0 || tile.Y < 0 || tile.X >= width || tile.Y >= height)
                return false;

            // 5. Evitar pisar warps y puertas de edificios accidentalmente si no son nuestro destino o inicio
            if (tile != targetTile && tile != npc.TilePoint)
            {
                foreach (var w in location.warps)
                {
                    if (tile.X == w.X && tile.Y == w.Y)
                    {
                        cache[tile] = false;
                        return false;
                    }
                }

                if (location.buildings != null)
                {
                    foreach (var b in location.buildings)
                    {
                        Point door = b.getPointForHumanDoor();
                        if (tile.X == door.X && tile.Y == door.Y)
                        {
                            cache[tile] = false;
                            return false;
                        }
                    }
                }
            }

            // 1. Capa estática de tiles (agua, montañas, etc.)
            var xLoc = new xTile.Dimensions.Location(tile.X * Game1.tileSize + Game1.tileSize / 2, tile.Y * Game1.tileSize + Game1.tileSize / 2);
            var viewport = new xTile.Dimensions.Rectangle(0, 0, location.Map.DisplayWidth, location.Map.DisplayHeight);

            if (!location.isTilePassable(xLoc, viewport))
            {
                cache[tile] = false;
                return false;
            }

            // 2. Usar el motor nativo de colisiones para detectar TODO excepto personajes
            Rectangle tileRect = new Rectangle(tile.X * Game1.tileSize, tile.Y * Game1.tileSize, Game1.tileSize, Game1.tileSize);

            // isCollidingPosition verifica: escombros, muebles, large terrain features, resource clumps y construcciones estáticas
            if (location.isCollidingPosition(tileRect, viewport, false, 0, false, null, true, false, true))
            {
                 // EXCEPCIÓN DE PUERTAS DE EDIFICIOS Y WARPS:
                 // El motor a veces marca los tiles de entrada/salida de puertas (o porches) como colisivos.
                 bool isDoorArea = false;
                 if (location.buildings != null)
                 {
                     foreach (var building in location.buildings)
                     {
                          Point door = building.getPointForHumanDoor();
                          // Permitimos pasar por el tile exacto de la puerta, y el tile de "aterrizaje" de warp justo abajo
                          if (tile.X == door.X && (tile.Y == door.Y || tile.Y == door.Y + 1))
                          {
                               isDoorArea = true;
                               break;
                          }
                     }
                 }
                 if (!isDoorArea)
                 {
                     foreach (var w in location.warps)
                     {
                          // Radio ampliado para porches: 2 tiles a los lados y 2 tiles hacia abajo del warp
                          if (tile.X >= w.X - 2 && tile.X <= w.X + 2 && (tile.Y == w.Y || tile.Y == w.Y + 1 || tile.Y == w.Y + 2))
                          {
                               isDoorArea = true;
                               break;
                          }
                     }
                 }
                 if (!isDoorArea)
                 {
                     cache[tile] = false;
                     return false;
                 }
            }

            // 3. Verificaciones de objetos extra que isCollidingPosition podría omitir en algunos contextos
            Vector2 tileVec = new Vector2(tile.X, tile.Y);
            if (location.objects.TryGetValue(tileVec, out var obj))
            {
                if (!obj.isPassable())
                {
                    cache[tile] = false;
                    return false;
                }
            }

            if (location.terrainFeatures.TryGetValue(tileVec, out var feature))
            {
                if (feature is Tree || feature is FruitTree)
                {
                     cache[tile] = false;
                     return false;
                }
            }

            // 4. Verificación perimetral extra para evitar recortar bordes peligrosos
            // Revisamos los adyacentes para detectar "medio tile" que no choque el centro pero sí al npc entero.
            Point[] adjacents = { new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1) };
            foreach (var adjDir in adjacents)
            {
                Point adjTile = new Point(tile.X + adjDir.X, tile.Y + adjDir.Y);
                if (adjTile.X < 0 || adjTile.Y < 0 || adjTile.X >= width || adjTile.Y >= height) continue;

                Rectangle adjTileRect = new Rectangle(adjTile.X * Game1.tileSize, adjTile.Y * Game1.tileSize, Game1.tileSize, Game1.tileSize);
                if (location.isCollidingPosition(adjTileRect, viewport, false, 0, false, null, true, false, true))
                {
                    // Excepción para puertas y warps
                    bool isDoorAreaAdj = false;
                    if (location.buildings != null)
                    {
                        foreach (var building in location.buildings)
                        {
                            Point door = building.getPointForHumanDoor();
                            if (adjTile.X == door.X && (adjTile.Y == door.Y || adjTile.Y == door.Y + 1))
                            {
                                isDoorAreaAdj = true;
                                break;
                            }
                        }
                    }
                    if (!isDoorAreaAdj)
                    {
                        foreach (var w in location.warps)
                        {
                            if (adjTile.X >= w.X - 2 && adjTile.X <= w.X + 2 && (adjTile.Y == w.Y || adjTile.Y == w.Y + 1 || adjTile.Y == w.Y + 2))
                            {
                                isDoorAreaAdj = true;
                                break;
                            }
                        }
                    }

                    if (!isDoorAreaAdj)
                    {
                        bool isBlockingEdge = false;

                        if (location.buildings != null)
                        {
                            foreach (var building in location.buildings)
                            {
                                if (adjTile.X >= building.tileX.Value && adjTile.X < building.tileX.Value + building.tilesWide.Value &&
                                    adjTile.Y >= building.tileY.Value && adjTile.Y < building.tileY.Value + building.tilesHigh.Value)
                                {
                                    // SOLO aplicar este borde a edificios problemáticos
                                    if (building.buildingType.Value != null && building.buildingType.Value.Contains("Shipping Bin", StringComparison.OrdinalIgnoreCase))
                                    {
                                        isBlockingEdge = true;
                                        break;
                                    }
                                }
                            }
                        }

                        // Check large craftables / machines
                        if (!isBlockingEdge)
                        {
                            Vector2 adjVec = new Vector2(adjTile.X, adjTile.Y);
                            if (location.objects.TryGetValue(adjVec, out var adjObj) && adjObj.bigCraftable.Value)
                            {
                                isBlockingEdge = true;
                            }
                        }

                        // Check Resource Clumps (giant boulders, stumps, etc)
                        if (!isBlockingEdge && location.resourceClumps != null)
                        {
                            foreach (var clump in location.resourceClumps)
                            {
                                if (clump.occupiesTile(adjTile.X, adjTile.Y))
                                {
                                    isBlockingEdge = true;
                                    break;
                                }
                            }
                        }

                        // Check Furniture
                        if (!isBlockingEdge && location.furniture != null)
                        {
                            foreach (var f in location.furniture)
                            {
                                if (f.boundingBox.Value.Intersects(adjTileRect))
                                {
                                    isBlockingEdge = true;
                                    break;
                                }
                            }
                        }

                        if (isBlockingEdge)
                        {
                            // Si choca con objeto grande en el borde, lo mejor es marcar este tile pasable como falso para evitar el recorte
                            cache[tile] = false;
                            return false;
                        }
                    }
                }
            }

            cache[tile] = true;
            return true;
        }

        private static int GetTileCost(GameLocation location, Point tile)
        {
            int cost = 5; // Costo base normal
            Vector2 tileVec = new Vector2(tile.X, tile.Y);
            if (location.terrainFeatures.TryGetValue(tileVec, out var feature))
            {
                if (feature is HoeDirt dirt)
                {
                    // Cultivos altamente evitados
                    if (dirt.crop != null) cost += 200;
                    else cost += 50; // Tierra arada
                }
                else
                {
                    string name = feature.GetType().Name;
                    if (name == "Flooring" || name == "Path")
                        cost = 1; // Priorizar caminos
                }
            }

            // Radio de zona incómoda: buscar obstáculos alrededor
            Point[] checks = {
                new Point(tile.X + 1, tile.Y), new Point(tile.X - 1, tile.Y),
                new Point(tile.X, tile.Y + 1), new Point(tile.X, tile.Y - 1)
            };

            bool foundObstacle = false;
            foreach(var p in checks)
            {
                var rect = new Rectangle(p.X * Game1.tileSize, p.Y * Game1.tileSize, Game1.tileSize, Game1.tileSize);
                var vp = new xTile.Dimensions.Rectangle(0, 0, location.Map.DisplayWidth, location.Map.DisplayHeight);
                if (location.isCollidingPosition(rect, vp, false, 0, false, null, true, false, true))
                {
                    foundObstacle = true;
                    break;
                }

                Vector2 pv = new Vector2(p.X, p.Y);
                if (location.objects.TryGetValue(pv, out var o) && !o.isPassable())
                {
                    foundObstacle = true;
                    break;
                }
            }
            if (foundObstacle)
            {
                cost += 15; // Añadir un costo extra a estar adyacente a obstáculos
            }

            return cost;
        }

        private static Point GetClosestWalkableTile(GameLocation location, Point center, NPC npc, Dictionary<Point, bool> cache, int width, int height, int maxRadius)
        {
            var queue = new Queue<Point>();
            var visited = new HashSet<Point>();

            queue.Enqueue(center);
            visited.Add(center);

            Point[] neighbors = { new Point(0, -1), new Point(0, 1), new Point(-1, 0), new Point(1, 0) };

            while (queue.Count > 0)
            {
                Point current = queue.Dequeue();

                if (IsTileWalkable(location, current, center, npc, cache, width, height))
                    return current;

                foreach (var dir in neighbors)
                {
                    Point n = new Point(current.X + dir.X, current.Y + dir.Y);

                    if (n.X >= 0 && n.Y >= 0 && n.X < width && n.Y < height && Math.Abs(n.X - center.X) + Math.Abs(n.Y - center.Y) <= maxRadius)
                    {
                        if (visited.Add(n))
                            queue.Enqueue(n);
                    }
                }
            }
            return new Point(-1, -1);
        }

        private static int Heuristic(Point a, Point b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }

        private static Stack<Point> ReconstructPath(Dictionary<Point, Point> cameFrom, Point current)
        {
            var path = new List<Point>();
            while (cameFrom.ContainsKey(current))
            {
                path.Add(current);
                current = cameFrom[current];
            }
            path.Reverse(); // Del origen al destino

            var stack = new Stack<Point>();
            // Apilamos al revés para que Pop() devuelva el primer paso
            for (int i = path.Count - 1; i >= 0; i--)
            {
                stack.Push(path[i]);
            }
            return stack;
        }

        // --- SISTEMA DE IDENTIFICACIÓN DE OBSTÁCULOS ---

        public static string IdentifyObstacle(GameLocation location, Point tile)
        {
            if (location == null) return "algo";

            Vector2 tileVec = new Vector2(tile.X, tile.Y);
            Rectangle tileRect = new Rectangle(tile.X * Game1.tileSize, tile.Y * Game1.tileSize, Game1.tileSize, Game1.tileSize);

            // 1. Objetos colocados por el jugador (Máquinas, Vallas, Espantapájaros)
            if (location.objects.TryGetValue(tileVec, out var obj))
            {
                if (obj is Fence fence) return fence.isGate.Value ? "una puerta cerrada" : "una valla";
                if (obj.bigCraftable.Value) return "una máquina pesada (" + obj.DisplayName + ")";
                return "un objeto (" + obj.DisplayName + ")";
            }

            // 2. Terreno grande y árboles
            if (location.terrainFeatures.TryGetValue(tileVec, out var feature))
            {
                if (feature is Tree) return "un árbol grande";
                if (feature is FruitTree) return "un árbol frutal";
                if (feature is Bush bush) return bush.townBush.Value ? "un arbusto de la ciudad" : "un arbusto denso";
            }

            // 3. Resource Clumps (Troncos grandes, meteoritos, piedras grandes)
            foreach (var clump in location.resourceClumps)
            {
                if (clump.occupiesTile(tile.X, tile.Y))
                {
                    int parentSheet = clump.parentSheetIndex.Value;
                    if (parentSheet == 600) return "un tocón de madera enorme";
                    if (parentSheet == 602) return "un tronco caído macizo";
                    if (parentSheet == 622) return "un meteorito gigante";
                    if (parentSheet == 672 || parentSheet == 752) return "una roca inmensa";
                    return "un obstáculo natural grande";
                }
            }

            // 4. Muebles (decoración del jugador)
            if (location.furniture != null)
            {
                foreach (var f in location.furniture)
                {
                    if (f.boundingBox.Value.Intersects(tileRect))
                        return "un mueble (" + f.DisplayName + ")";
                }
            }

            // 5. Large Terrain Features
            if (location.largeTerrainFeatures != null)
            {
                foreach (var ltf in location.largeTerrainFeatures)
                {
                    if (ltf.getBoundingBox().Intersects(tileRect))
                        return "un obstáculo natural gigante";
                }
            }

            // 6. Edificios de granja (El jugador movió un edificio)
            if (location.buildings != null)
            {
                foreach (var b in location.buildings)
                {
                    if (tile.X >= b.tileX.Value && tile.X < b.tileX.Value + b.tilesWide.Value &&
                        tile.Y >= b.tileY.Value && tile.Y < b.tileY.Value + b.tilesHigh.Value)
                    {
                        return "un edificio (" + b.buildingType.Value + ")";
                    }
                }
            }

            // Fallback (agua, pared estática del mapa, etc)
            var xLoc = new xTile.Dimensions.Location(tile.X * Game1.tileSize + Game1.tileSize / 2, tile.Y * Game1.tileSize + Game1.tileSize / 2);
            var viewport = new xTile.Dimensions.Rectangle(0, 0, location.Map.DisplayWidth, location.Map.DisplayHeight);
            if (!location.isTilePassable(xLoc, viewport))
            {
                return "una pared, barranco o el agua";
            }

            return "un obstáculo extraño";
        }
    }
}
