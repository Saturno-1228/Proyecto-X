using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;

namespace StardewLivingValley.Services
{
    public static class SmartPathfinder
    {
        /// <summary>
        /// Busca una ruta usando A* hiper-optimizado. Si el destino está bloqueado, encuentra el punto caminable más cercano.
        /// </summary>
        public static Stack<Point>? FindPath(NPC npc, GameLocation location, Point startTile, Point targetTile, int maxIterations = 15000, int toleranceRadius = 3)
        {
            if (location == null || npc == null) return null;

            // 1. Corrección de Objetivo (Búsqueda Radial Previa)
            Point actualTarget = GetClosestWalkableTile(npc, location, targetTile, toleranceRadius);
            
            // Si no hay ningún tile libre cerca, es inalcanzable.
            if (actualTarget.X == -1 && actualTarget.Y == -1)
            {
                return null;
            }

            // Si ya estamos allí, retornamos una ruta trivial.
            if (startTile == actualTarget)
            {
                var stack = new Stack<Point>();
                stack.Push(actualTarget);
                return stack;
            }

            // 2. A* Optimizado (Síncrono)
            int width = location.Map.Layers[0].LayerWidth;
            int height = location.Map.Layers[0].LayerHeight;

            var openSet = new PriorityQueue<Point, int>();
            var cameFrom = new Dictionary<Point, Point>();
            var gScore = new Dictionary<Point, int>();
            
            // Acceso O(1) rápido a la lista cerrada
            var closedSet = new bool[width, height];

            openSet.Enqueue(startTile, Heuristic(startTile, actualTarget));
            gScore[startTile] = 0;

            int iterations = 0;
            Point[] neighbors = new Point[]
            {
                new Point(0, -1), // Arriba
                new Point(0, 1),  // Abajo
                new Point(-1, 0), // Izquierda
                new Point(1, 0)   // Derecha
            };

            while (openSet.Count > 0)
            {
                if (iterations++ > maxIterations)
                {
                    // Límite de seguridad alcanzado (evita colgar el juego)
                    break;
                }

                Point current = openSet.Dequeue();
                
                if (current == actualTarget)
                {
                    return ReconstructPath(cameFrom, current);
                }

                closedSet[current.X, current.Y] = true;

                foreach (var dir in neighbors)
                {
                    Point neighbor = new Point(current.X + dir.X, current.Y + dir.Y);

                    // Bounds check
                    if (neighbor.X < 0 || neighbor.Y < 0 || neighbor.X >= width || neighbor.Y >= height)
                        continue;

                    if (closedSet[neighbor.X, neighbor.Y])
                        continue;

                    // Colisión
                    if (!IsTileWalkable(location, neighbor, npc))
                        continue;

                    int tentative_gScore = gScore[current] + 1;

                    if (!gScore.ContainsKey(neighbor) || tentative_gScore < gScore[neighbor])
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentative_gScore;
                        int fScore = tentative_gScore + Heuristic(neighbor, actualTarget);
                        
                        openSet.Enqueue(neighbor, fScore);
                    }
                }
            }

            return null; // Ruta no encontrada dentro del límite o bloqueada completamente
        }

        private static int Heuristic(Point a, Point b)
        {
            // Distancia Manhattan
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

            // totalPath está invertido: [Destino, PasoN, ..., Paso1, Inicio]
            // PathFindController requiere que el próximo paso (Paso1) esté arriba (Top) del Stack, y el destino al fondo (Bottom).
            var stack = new Stack<Point>();
            
            // Apilamos desde el Destino hasta el Paso1
            for (int i = 0; i < totalPath.Count - 1; i++)
            {
                stack.Push(totalPath[i]);
            }
            
            return stack;
        }

        private static Point GetClosestWalkableTile(NPC npc, GameLocation location, Point target, int radius)
        {
            if (IsTileWalkable(location, target, npc)) return target;

            // BFS corto para encontrar el tile libre más cercano en espiral
            var queue = new Queue<Point>();
            var visited = new HashSet<Point>();
            queue.Enqueue(target);
            visited.Add(target);

            while (queue.Count > 0)
            {
                Point current = queue.Dequeue();
                
                if (IsTileWalkable(location, current, npc))
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
                    if (visited.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            return new Point(-1, -1);
        }

        private static bool IsTileWalkable(GameLocation location, Point tile, NPC npc)
        {
            var xLocation = new xTile.Dimensions.Location(tile.X * Game1.tileSize + Game1.tileSize / 2, tile.Y * Game1.tileSize + Game1.tileSize / 2);
            
            // Capa estática del mapa (ej. agua, bordes del mapa)
            if (!location.isTilePassable(xLocation, Game1.viewport))
            {
                return false;
            }

            // Caja de colisión para objetos físicos (vallas, árboles, cofres, edificios)
            // Se usa un rectángulo ligeramente más pequeño (48x48) para evitar fricciones innecesarias en bordes.
            Rectangle boundingBox = new Rectangle(tile.X * Game1.tileSize + 8, tile.Y * Game1.tileSize + 8, 48, 48);

            // isCharacter = true asegura que verifique todas las colisiones relativas a personajes.
            // Ignoramos la colisión con el NPC mismo.
            if (location.isCollidingPosition(boundingBox, Game1.viewport, true, 0, false, npc))
            {
                return false;
            }

            return true;
        }
    }
}
