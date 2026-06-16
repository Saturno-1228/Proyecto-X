using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Objects;
using StardewValley.Buildings;

namespace StardewLivingValley.Services
{
    public static class AdvancedPathfinder
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

            _logger?.Log($"[AdvancedPathfinder] Buscando ruta en {location.Name} de {startTile} a {targetTile}", LogLevel.Debug);

            // Siempre asumimos que donde estamos parados es caminable
            var walkableCache = new Dictionary<Point, bool>
            {
                [startTile] = true
            };

            // 1. Verificamos si el destino es exactamente caminable. Si no lo es, encontramos el punto más cercano válido.
            Point actualTarget = targetTile;
            bool isPartial = false;

            if (!IsTileWalkable(location, targetTile, npc, walkableCache, width, height))
            {
                actualTarget = GetClosestWalkableTile(location, targetTile, npc, walkableCache, width, height, 5);
                isPartial = true;

                if (actualTarget.X == -1)
                {
                    _logger?.Log($"[AdvancedPathfinder] Destino completamente inaccesible y sin tiles cercanos válidos.", LogLevel.Warn);
                    return new PathResult { Path = null };
                }
                _logger?.Log($"[AdvancedPathfinder] Destino bloqueado. Ajustando a punto parcial {actualTarget}", LogLevel.Debug);
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
            var closedSet = new bool[width, height];

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
                    _logger?.Log($"[AdvancedPathfinder] Límite de iteraciones alcanzado. Devolviendo ruta parcial hasta {closestReached}", LogLevel.Warn);
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

                if (closedSet[current.X, current.Y]) continue;
                closedSet[current.X, current.Y] = true;

                int currentH = Heuristic(current, actualTarget);
                if (currentH < closestH)
                {
                    closestH = currentH;
                    closestReached = current;
                }

                foreach (var dir in directions)
                {
                    Point neighbor = new Point(current.X + dir.X, current.Y + dir.Y);

                    if (neighbor.X < 0 || neighbor.Y < 0 || neighbor.X >= width || neighbor.Y >= height)
                        continue;
                    if (closedSet[neighbor.X, neighbor.Y])
                        continue;

                    if (!IsTileWalkable(location, neighbor, npc, walkableCache, width, height))
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

            _logger?.Log($"[AdvancedPathfinder] No se encontró ruta. Devolviendo ruta parcial hasta {closestReached}", LogLevel.Warn);
            return new PathResult {
                Path = ReconstructPath(cameFrom, closestReached),
                IsPartial = true,
                EndPoint = closestReached
            };
        }

        private static bool IsTileWalkable(GameLocation location, Point tile, NPC npc, Dictionary<Point, bool> cache, int width, int height)
        {
            if (tile.X < 0 || tile.Y < 0 || tile.X >= width || tile.Y >= height)
                return false;

            if (cache.TryGetValue(tile, out bool cachedWalkable))
                return cachedWalkable;

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
                 // EXCEPCIÓN DE PUERTAS DE EDIFICIOS:
                 // El motor a veces marca los tiles de entrada/salida de puertas como colisivos.
                 bool isDoorArea = false;
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

            cache[tile] = true;
            return true;
        }

        private static int GetTileCost(GameLocation location, Point tile)
        {
            Vector2 tileVec = new Vector2(tile.X, tile.Y);
            if (location.terrainFeatures.TryGetValue(tileVec, out var feature))
            {
                if (feature is HoeDirt dirt)
                {
                    if (dirt.crop != null) return 200; // Cultivos altamente evitados
                    return 50; // Tierra arada
                }

                string name = feature.GetType().Name;
                if (name == "Flooring" || name == "Path")
                    return 1; // Priorizar caminos
            }
            return 5; // Costo base normal
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

                if (IsTileWalkable(location, current, npc, cache, width, height))
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
