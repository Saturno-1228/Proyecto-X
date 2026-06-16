using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;

namespace StardewLivingValley.Services
{
    /// <summary>
    /// Pathfinder A* optimizado que detecta TODOS los tipos de obstáculos del juego.
    /// Encuentra la ruta más corta y natural, evitando objetos del jugador, edificios,
    /// árboles, muebles y cualquier otra obstrucción.
    /// </summary>
    public static class SmartPathfinder
    {
        // Logger para diagnóstico — se inyecta desde fuera
        private static IMonitor? _logger;
        
        public static void SetLogger(IMonitor logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Busca la ruta óptima usando A* con detección exhaustiva de obstáculos.
        /// Si el destino está bloqueado, encuentra el punto caminable más cercano.
        /// </summary>
        public static Stack<Point>? FindPath(NPC npc, GameLocation location, Point startTile, Point targetTile, int maxIterations = 15000, int toleranceRadius = 3)
        {
            if (location == null || npc == null) return null;

            int width = location.Map.Layers[0].LayerWidth;
            int height = location.Map.Layers[0].LayerHeight;

            _logger?.Log($"[SmartPathfinder] === DIAGNÓSTICO INICIO ===", LogLevel.Debug);
            _logger?.Log($"[SmartPathfinder] Mapa: '{location.NameOrUniqueName}' ({width}x{height} tiles)", LogLevel.Debug);
            _logger?.Log($"[SmartPathfinder] Start: {startTile}, Target: {targetTile}", LogLevel.Debug);

            // Pre-calcular caché de walkability para todo el área relevante
            var walkableCache = new Dictionary<Point, bool>();

            // CRÍTICO: El tile donde el NPC ya está parado SIEMPRE es caminable.
            // Esto resuelve el caso donde el NPC aterriza dentro del footprint de un
            // edificio tras un warp (ej: salir de FarmHouse → tile {60,13} está dentro
            // del footprint de Farmhouse, pero el NPC ya está físicamente ahí).
            walkableCache[startTile] = true;

            // Diagnóstico: verificar tile de inicio
            string startDiag = DiagnoseTile(location, startTile, npc);
            _logger?.Log($"[SmartPathfinder] Tile INICIO {startTile}: {startDiag}", LogLevel.Debug);

            // Diagnóstico: verificar vecinos del inicio
            Point[] startNeighbors = {
                new Point(startTile.X, startTile.Y - 1),
                new Point(startTile.X, startTile.Y + 1),
                new Point(startTile.X - 1, startTile.Y),
                new Point(startTile.X + 1, startTile.Y)
            };
            foreach (var sn in startNeighbors)
            {
                if (sn.X >= 0 && sn.Y >= 0 && sn.X < width && sn.Y < height)
                {
                    string snDiag = DiagnoseTile(location, sn, npc);
                    _logger?.Log($"[SmartPathfinder] Vecino {sn}: {snDiag}", LogLevel.Debug);
                }
            }

            // Diagnóstico: verificar tile destino
            string targetDiag = DiagnoseTile(location, targetTile, npc);
            _logger?.Log($"[SmartPathfinder] Tile DESTINO {targetTile}: {targetDiag}", LogLevel.Debug);

            // 1. Corrección de Objetivo (Búsqueda Radial Previa)
            Point actualTarget = GetClosestWalkableTile(npc, location, targetTile, toleranceRadius, walkableCache, width, height);
            
            if (actualTarget.X == -1 && actualTarget.Y == -1)
            {
                _logger?.Log($"[SmartPathfinder] FALLO: No se encontró tile caminable cerca del destino {targetTile} (radio={toleranceRadius})", LogLevel.Warn);
                return null;
            }

            if (actualTarget != targetTile)
            {
                _logger?.Log($"[SmartPathfinder] Destino ajustado de {targetTile} a {actualTarget}", LogLevel.Debug);
            }

            if (startTile == actualTarget)
            {
                var stack = new Stack<Point>();
                stack.Push(actualTarget);
                return stack;
            }

            // 2. A* con tie-breaking para rutas más naturales
            var openSet = new PriorityQueue<Point, long>();
            var cameFrom = new Dictionary<Point, Point>();
            var gScore = new Dictionary<Point, int>();
            var closedSet = new bool[width, height];

            gScore[startTile] = 0;
            long startPriority = CalculatePriority(0, Heuristic(startTile, actualTarget), startTile, startTile, actualTarget);
            openSet.Enqueue(startTile, startPriority);

            int iterations = 0;
            int blockedCount = 0;
            Point[] directions = new Point[]
            {
                new Point(0, -1),  // Arriba
                new Point(0, 1),   // Abajo
                new Point(-1, 0),  // Izquierda
                new Point(1, 0)    // Derecha
            };

            while (openSet.Count > 0)
            {
                if (iterations++ > maxIterations)
                {
                    _logger?.Log($"[SmartPathfinder] FALLO: Máximo de iteraciones alcanzado ({maxIterations}). Tiles explorados: {iterations}. Bloqueados logueados: {blockedCount}", LogLevel.Warn);
                    break;
                }

                Point current = openSet.Dequeue();
                
                if (current == actualTarget)
                {
                    var result = ReconstructPath(cameFrom, current);
                    _logger?.Log($"[SmartPathfinder] ÉXITO: Ruta encontrada con {result.Count} pasos en {iterations} iteraciones.", LogLevel.Debug);
                    return result;
                }

                if (closedSet[current.X, current.Y])
                    continue;
                closedSet[current.X, current.Y] = true;

                foreach (var dir in directions)
                {
                    Point neighbor = new Point(current.X + dir.X, current.Y + dir.Y);

                    if (neighbor.X < 0 || neighbor.Y < 0 || neighbor.X >= width || neighbor.Y >= height)
                        continue;

                    if (closedSet[neighbor.X, neighbor.Y])
                        continue;

                    if (!IsWalkableCached(location, neighbor, npc, walkableCache, width, height))
                        continue;

                    int tentative_gScore = gScore[current] + GetTileCost(location, neighbor);

                    if (!gScore.ContainsKey(neighbor) || tentative_gScore < gScore[neighbor])
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentative_gScore;
                        int h = Heuristic(neighbor, actualTarget);
                        long priority = CalculatePriority(tentative_gScore, h, neighbor, startTile, actualTarget);
                        openSet.Enqueue(neighbor, priority);
                    }
                }
            }

            // Diagnóstico final: loguear tiles en la línea recta entre start y target
            _logger?.Log($"[SmartPathfinder] FALLO FINAL. Diagnosticando línea recta start→target:", LogLevel.Warn);
            int diagSteps = Math.Max(Math.Abs(actualTarget.X - startTile.X), Math.Abs(actualTarget.Y - startTile.Y));
            for (int i = 0; i <= Math.Min(diagSteps, 25); i++)
            {
                float t = diagSteps == 0 ? 0 : (float)i / diagSteps;
                int lx = startTile.X + (int)((actualTarget.X - startTile.X) * t);
                int ly = startTile.Y + (int)((actualTarget.Y - startTile.Y) * t);
                Point linePoint = new Point(lx, ly);
                if (lx >= 0 && ly >= 0 && lx < width && ly < height)
                {
                    string lineDiag = DiagnoseTile(location, linePoint, npc);
                    _logger?.Log($"[SmartPathfinder]   Línea[{i}] {linePoint}: {lineDiag}", LogLevel.Warn);
                }
            }
            _logger?.Log($"[SmartPathfinder] === DIAGNÓSTICO FIN ===", LogLevel.Warn);

            return null;
        }

        /// <summary>
        /// Diagnostica un tile individual, retornando qué check falla o "OK" si es caminable.
        /// </summary>
        private static string DiagnoseTile(GameLocation location, Point tile, NPC npc)
        {
            var viewport = new xTile.Dimensions.Rectangle(0, 0, location.Map.DisplayWidth, location.Map.DisplayHeight);

            // Check 1: isTilePassable
            var xLoc = new xTile.Dimensions.Location(tile.X * Game1.tileSize + Game1.tileSize / 2, tile.Y * Game1.tileSize + Game1.tileSize / 2);
            if (!location.isTilePassable(xLoc, viewport))
                return "BLOQUEADO por #1 isTilePassable (capa mapa estática)";

            // Check 2: NPCBarrier - ELIMINADO (Emily está en una misión activa, debe poder cruzar barreras de NPCs vagabundos)
            // Check 3: NoPath - ELIMINADO (Misma razón, debe usar rutas directas)

            // Check 4: Objects
            Vector2 tileVec = new Vector2(tile.X, tile.Y);
            if (location.objects.TryGetValue(tileVec, out var obj))
            {
                if (!obj.isPassable())
                    return $"BLOQUEADO por #4 Objeto no-pasable: '{obj.Name}' (ID:{obj.ItemId})";
            }

            // Check 5: Trees
            if (location.terrainFeatures.ContainsKey(tileVec))
            {
                var feature = location.terrainFeatures[tileVec];
                if (feature is Tree)
                    return "BLOQUEADO por #5 Tree";
                if (feature is FruitTree)
                    return "BLOQUEADO por #5 FruitTree";
            }

            // Check 6: Resource clumps
            foreach (var clump in location.resourceClumps)
            {
                if (clump.occupiesTile(tile.X, tile.Y))
                    return $"BLOQUEADO por #6 ResourceClump (tipo:{clump.parentSheetIndex.Value})";
            }

            // Check 7: Buildings
            if (location.buildings != null)
            {
                foreach (var building in location.buildings)
                {
                    int bx = building.tileX.Value;
                    int by = building.tileY.Value;
                    int bw = building.tilesWide.Value;
                    int bh = building.tilesHigh.Value;

                    if (tile.X >= bx && tile.X < bx + bw && tile.Y >= by && tile.Y < by + bh)
                    {
                        Point door = building.getPointForHumanDoor();
                        // Permitir: tile de la puerta, tile encima (acceso), y tile debajo (warp landing)
                        if (tile.X == door.X && (tile.Y >= door.Y - 1 && tile.Y <= door.Y + 1))
                            continue;
                        
                        return $"BLOQUEADO por #7 Building: '{building.buildingType.Value}'";
                    }
                }
            }

            // Check 8: Furniture
            if (location.furniture != null && location.furniture.Count > 0)
            {
                int pixelX = tile.X * Game1.tileSize;
                int pixelY = tile.Y * Game1.tileSize;
                Rectangle tileRect = new Rectangle(pixelX + 4, pixelY + 4, Game1.tileSize - 8, Game1.tileSize - 8);
                foreach (var furniture in location.furniture)
                {
                    if (furniture.boundingBox.Value.Intersects(tileRect))
                        return $"BLOQUEADO por #8 Furniture: '{furniture.Name}'";
                }
            }

            // Check 9: Large terrain features
            if (location.largeTerrainFeatures != null)
            {
                foreach (var ltf in location.largeTerrainFeatures)
                {
                    Rectangle ltfBounds = ltf.getBoundingBox();
                    if (ltfBounds.Contains(tile.X * Game1.tileSize + 32, tile.Y * Game1.tileSize + 32))
                        return $"BLOQUEADO por #9 LargeTerrainFeature: '{ltf.GetType().Name}'";
                }
            }

            // Check 10: isCollidingPosition — ELIMINADO
            // Era redundante con isTilePassable y causaba falsos positivos
            // alrededor de edificios (ej: Silo en {55,13} bloqueaba la ruta).
            // El pathfinder nativo findPathForNPCSchedules NO usa este check.

            return "OK (caminable, Costo: " + GetTileCost(location, tile) + ")";
        }

        private static int GetTileCost(GameLocation location, Point tile)
        {
            int cost = 5; // Costo base normal

            Vector2 tileVec = new Vector2(tile.X, tile.Y);
            if (location.terrainFeatures.TryGetValue(tileVec, out var feature))
            {
                if (feature is HoeDirt dirt)
                {
                    if (dirt.crop != null)
                        return 200; // Penalización altísima por pisar cultivos
                    return 50;  // Penalización alta por pisar tierra arada
                }
                
                string featureName = feature.GetType().Name;
                if (featureName == "Flooring" || featureName == "Path")
                    return 1; // Camino preferido
            }

            return cost;
        }

        private static long CalculatePriority(int g, int h, Point current, Point start, Point goal)
        {
            int f = g + h;
            int dx1 = current.X - goal.X;
            int dy1 = current.Y - goal.Y;
            int dx2 = start.X - goal.X;
            int dy2 = start.Y - goal.Y;
            int cross = Math.Abs(dx1 * dy2 - dx2 * dy1);
            return (long)f * 10000L + (long)cross;
        }

        private static int Heuristic(Point a, Point b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }

        private static Stack<Point> ReconstructPath(Dictionary<Point, Point> cameFrom, Point current)
        {
            var totalPath = new List<Point> { current };
            while (cameFrom.ContainsKey(current))
            {
                current = cameFrom[current];
                totalPath.Add(current);
            }

            var stack = new Stack<Point>();
            for (int i = 0; i < totalPath.Count - 1; i++)
            {
                stack.Push(totalPath[i]);
            }
            
            return stack;
        }

        private static Point GetClosestWalkableTile(NPC npc, GameLocation location, Point target, int radius, Dictionary<Point, bool> cache, int mapW, int mapH)
        {
            if (IsWalkableCached(location, target, npc, cache, mapW, mapH)) return target;

            var queue = new Queue<Point>();
            var visited = new HashSet<Point>();
            queue.Enqueue(target);
            visited.Add(target);

            while (queue.Count > 0)
            {
                Point current = queue.Dequeue();
                
                if (IsWalkableCached(location, current, npc, cache, mapW, mapH))
                {
                    return current;
                }

                if (Heuristic(current, target) > radius)
                    continue;

                Point[] neighbors = {
                    new Point(current.X, current.Y - 1),
                    new Point(current.X, current.Y + 1),
                    new Point(current.X - 1, current.Y),
                    new Point(current.X + 1, current.Y)
                };

                foreach (var n in neighbors)
                {
                    if (n.X >= 0 && n.Y >= 0 && n.X < mapW && n.Y < mapH && visited.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            return new Point(-1, -1);
        }

        private static bool IsWalkableCached(GameLocation location, Point tile, NPC npc, Dictionary<Point, bool> cache, int mapW, int mapH)
        {
            if (tile.X < 0 || tile.Y < 0 || tile.X >= mapW || tile.Y >= mapH)
                return false;

            if (cache.TryGetValue(tile, out bool cached))
                return cached;

            bool walkable = IsTileWalkable(location, tile, npc);
            cache[tile] = walkable;
            return walkable;
        }

        private static bool IsTileWalkable(GameLocation location, Point tile, NPC npc)
        {
            var viewport = new xTile.Dimensions.Rectangle(0, 0, location.Map.DisplayWidth, location.Map.DisplayHeight);

            // 1. CAPA ESTÁTICA DEL MAPA
            var xLocation = new xTile.Dimensions.Location(tile.X * Game1.tileSize + Game1.tileSize / 2, tile.Y * Game1.tileSize + Game1.tileSize / 2);
            if (!location.isTilePassable(xLocation, viewport))
                return false;

            // 2. NPCBarrier - ELIMINADO
            // 3. NoPath - ELIMINADO

            // 4. Objetos no-pasables
            Vector2 tileVec = new Vector2(tile.X, tile.Y);
            if (location.objects.TryGetValue(tileVec, out var obj))
            {
                if (!obj.isPassable())
                    return false;
            }

            // 5. Árboles
            if (location.terrainFeatures.ContainsKey(tileVec))
            {
                var feature = location.terrainFeatures[tileVec];
                if (feature is Tree || feature is FruitTree)
                    return false;
            }

            // 6. Resource clumps
            foreach (var clump in location.resourceClumps)
            {
                if (clump.occupiesTile(tile.X, tile.Y))
                    return false;
            }

            // 7. Edificios
            if (location.buildings != null)
            {
                foreach (var building in location.buildings)
                {
                    int bx = building.tileX.Value;
                    int by = building.tileY.Value;
                    int bw = building.tilesWide.Value;
                    int bh = building.tilesHigh.Value;

                    if (tile.X >= bx && tile.X < bx + bw && tile.Y >= by && tile.Y < by + bh)
                    {
                        Point door = building.getPointForHumanDoor();
                        // Permitir: tile de la puerta, tile encima (acceso), y tile debajo (warp landing)
                        if (tile.X == door.X && (tile.Y >= door.Y - 1 && tile.Y <= door.Y + 1))
                            continue;
                        
                        return false;
                    }
                }
            }

            // 8. Muebles
            if (location.furniture != null && location.furniture.Count > 0)
            {
                int pixelX = tile.X * Game1.tileSize;
                int pixelY = tile.Y * Game1.tileSize;
                Rectangle tileRect = new Rectangle(pixelX + 4, pixelY + 4, Game1.tileSize - 8, Game1.tileSize - 8);
                foreach (var furniture in location.furniture)
                {
                    if (furniture.boundingBox.Value.Intersects(tileRect))
                        return false;
                }
            }

            // 9. Large terrain features
            if (location.largeTerrainFeatures != null)
            {
                foreach (var ltf in location.largeTerrainFeatures)
                {
                    Rectangle ltfBounds = ltf.getBoundingBox();
                    if (ltfBounds.Contains(tile.X * Game1.tileSize + 32, tile.Y * Game1.tileSize + 32))
                        return false;
                }
            }

            // 10. isCollidingPosition — ELIMINADO (redundante, causaba falsos positivos en edificios)

            return true;
        }
    }
}
