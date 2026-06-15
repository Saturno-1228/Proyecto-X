using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
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
        /// <summary>
        /// Busca la ruta óptima usando A* con detección exhaustiva de obstáculos.
        /// Si el destino está bloqueado, encuentra el punto caminable más cercano.
        /// </summary>
        public static Stack<Point>? FindPath(NPC npc, GameLocation location, Point startTile, Point targetTile, int maxIterations = 15000, int toleranceRadius = 3)
        {
            if (location == null || npc == null) return null;

            int width = location.Map.Layers[0].LayerWidth;
            int height = location.Map.Layers[0].LayerHeight;

            // Pre-calcular caché de walkability para todo el área relevante
            // Esto evita recalcular colisiones costosas múltiples veces por tile
            var walkableCache = new Dictionary<Point, bool>();

            // 1. Corrección de Objetivo (Búsqueda Radial Previa)
            Point actualTarget = GetClosestWalkableTile(npc, location, targetTile, toleranceRadius, walkableCache, width, height);
            
            if (actualTarget.X == -1 && actualTarget.Y == -1)
            {
                return null; // Inalcanzable
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
                    break;

                Point current = openSet.Dequeue();
                
                if (current == actualTarget)
                {
                    return ReconstructPath(cameFrom, current);
                }

                if (closedSet[current.X, current.Y])
                    continue;
                closedSet[current.X, current.Y] = true;

                foreach (var dir in directions)
                {
                    Point neighbor = new Point(current.X + dir.X, current.Y + dir.Y);

                    // Bounds check
                    if (neighbor.X < 0 || neighbor.Y < 0 || neighbor.X >= width || neighbor.Y >= height)
                        continue;

                    if (closedSet[neighbor.X, neighbor.Y])
                        continue;

                    // Verificar walkability con caché
                    if (!IsWalkableCached(location, neighbor, npc, walkableCache, width, height))
                        continue;

                    int tentative_gScore = gScore[current] + 1;

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

            return null;
        }

        /// <summary>
        /// Calcula la prioridad con tie-breaking para preferir rutas más rectas.
        /// La prioridad principal es f = g + h. Cuando hay empate, prefiere tiles
        /// más cercanos a la línea recta entre inicio y destino.
        /// </summary>
        private static long CalculatePriority(int g, int h, Point current, Point start, Point goal)
        {
            int f = g + h;
            
            // Cross product: mide cuánto se desvía el punto actual de la línea recta start→goal
            // Un valor más bajo = más cerca de la línea recta = ruta más natural
            int dx1 = current.X - goal.X;
            int dy1 = current.Y - goal.Y;
            int dx2 = start.X - goal.X;
            int dy2 = start.Y - goal.Y;
            int cross = Math.Abs(dx1 * dy2 - dx2 * dy1);
            
            // fScore es dominante (x10000), cross solo rompe empates
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

            // totalPath: [Destino, ..., Paso1, Inicio]
            // PathFindController necesita: Top=Paso1, Bottom=Destino
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

        /// <summary>
        /// Wrapper con caché para evitar recalcular walkability del mismo tile.
        /// </summary>
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

        /// <summary>
        /// Verificación EXHAUSTIVA de si un tile es caminable por un NPC.
        /// Revisa TODAS las capas de obstrucción del juego.
        /// </summary>
        private static bool IsTileWalkable(GameLocation location, Point tile, NPC npc)
        {
            // ═══════════════════════════════════════════════════════
            // 1. CAPA BUILDINGS DIRECTA (acantilados, paredes, elevaciones)
            //    Este es el check MÁS IMPORTANTE para zonas elevadas.
            //    isTilePassable a veces falla, así que verificamos directo.
            // ═══════════════════════════════════════════════════════
            try
            {
                var buildingsLayer = location.Map.GetLayer("Buildings");
                if (buildingsLayer != null)
                {
                    var buildingTile = buildingsLayer.Tiles[tile.X, tile.Y];
                    if (buildingTile != null)
                    {
                        // Solo es pasable si tiene explícitamente "Passable" o "Shadow"
                        bool hasPassable = buildingTile.TileIndexProperties.ContainsKey("Passable") || 
                                           buildingTile.Properties.ContainsKey("Passable");
                        bool hasShadow = buildingTile.TileIndexProperties.ContainsKey("Shadow") || 
                                         buildingTile.Properties.ContainsKey("Shadow");
                        if (!hasPassable && !hasShadow)
                        {
                            return false;
                        }
                    }
                }
            }
            catch { /* Protección contra tiles fuera de rango */ }

            // ═══════════════════════════════════════════════════════
            // 2. BARRERAS ESPECÍFICAS PARA NPCs (propiedades de tile)
            //    Estas propiedades en la capa "Back" marcan zonas donde
            //    los NPCs no deben caminar (bordes de acantilados, etc.)
            // ═══════════════════════════════════════════════════════
            if (location.doesTileHaveProperty(tile.X, tile.Y, "NPCBarrier", "Back") != null)
                return false;
            if (location.doesTileHaveProperty(tile.X, tile.Y, "NoPath", "Back") != null)
                return false;

            // ═══════════════════════════════════════════════════════
            // 3. CAPA ESTÁTICA DEL MAPA (verificación redundante como respaldo)
            // ═══════════════════════════════════════════════════════
            var xLocation = new xTile.Dimensions.Location(tile.X * Game1.tileSize + Game1.tileSize / 2, tile.Y * Game1.tileSize + Game1.tileSize / 2);
            if (!location.isTilePassable(xLocation, Game1.viewport))
            {
                return false;
            }

            // ═══════════════════════════════════════════════════════
            // 4. OBJETOS DEL JUGADOR (aspersores, cofres, máquinas, vallas)
            // ═══════════════════════════════════════════════════════
            Vector2 tileVec = new Vector2(tile.X, tile.Y);
            if (location.objects.ContainsKey(tileVec))
            {
                return false;
            }

            // ═══════════════════════════════════════════════════════
            // 5. TERRAIN FEATURES (árboles, árboles frutales)
            // ═══════════════════════════════════════════════════════
            if (location.terrainFeatures.ContainsKey(tileVec))
            {
                var feature = location.terrainFeatures[tileVec];
                if (feature is Tree || feature is FruitTree)
                {
                    return false;
                }
            }

            // ═══════════════════════════════════════════════════════
            // 6. RESOURCE CLUMPS (rocas grandes, troncos, meteoritos)
            // ═══════════════════════════════════════════════════════
            foreach (var clump in location.resourceClumps)
            {
                if (clump.occupiesTile(tile.X, tile.Y))
                {
                    return false;
                }
            }

            // ═══════════════════════════════════════════════════════
            // 7. EDIFICIOS (footprints completos, excepto puerta humana)
            // ═══════════════════════════════════════════════════════
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
                        if ((tile.X == door.X && tile.Y == door.Y) || (tile.X == door.X && tile.Y == door.Y + 1))
                            continue;
                        
                        return false;
                    }
                }
            }

            // ═══════════════════════════════════════════════════════
            // 8. MUEBLES (en interiores como FarmHouse)
            // ═══════════════════════════════════════════════════════
            if (location.furniture != null && location.furniture.Count > 0)
            {
                int pixelX = tile.X * Game1.tileSize;
                int pixelY = tile.Y * Game1.tileSize;
                Rectangle tileRect = new Rectangle(pixelX + 4, pixelY + 4, Game1.tileSize - 8, Game1.tileSize - 8);
                
                foreach (var furniture in location.furniture)
                {
                    if (furniture.boundingBox.Value.Intersects(tileRect))
                    {
                        return false;
                    }
                }
            }

            // ═══════════════════════════════════════════════════════
            // 9. LARGE TERRAIN FEATURES (arbustos grandes)
            // ═══════════════════════════════════════════════════════
            if (location.largeTerrainFeatures != null)
            {
                foreach (var ltf in location.largeTerrainFeatures)
                {
                    Rectangle ltfBounds = ltf.getBoundingBox();
                    if (ltfBounds.Contains(tile.X * Game1.tileSize + 32, tile.Y * Game1.tileSize + 32))
                    {
                        return false;
                    }
                }
            }

            // ═══════════════════════════════════════════════════════
            // 10. COLISIÓN GENERAL DEL ENGINE (último recurso)
            // ═══════════════════════════════════════════════════════
            Rectangle boundingBox = new Rectangle(tile.X * Game1.tileSize + 8, tile.Y * Game1.tileSize + 8, 48, 48);
            if (location.isCollidingPosition(boundingBox, Game1.viewport, true, 0, false, npc))
            {
                return false;
            }

            return true;
        }
    }
}
