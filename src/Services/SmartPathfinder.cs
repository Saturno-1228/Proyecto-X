using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;

namespace StardewLivingValley.Services
{
    /// <summary>
    /// Un enrutador A* (A-Star) personalizado, síncrono y altamente optimizado.
    /// Diseñado para ser usado como fallback cuando el pathfinder nativo de Stardew Valley falla,
    /// especialmente en mapas grandes y dinámicos como la Granja.
    /// </summary>
    public static class SmartPathfinder
    {
        // Movimientos permitidos (Arriba, Abajo, Izquierda, Derecha)
        private static readonly Point[] Directions = new Point[]
        {
            new Point(0, -1),
            new Point(0, 1),
            new Point(-1, 0),
            new Point(1, 0)
        };

        /// <summary>
        /// Intenta encontrar una ruta desde un punto inicial hasta un objetivo.
        /// Si el objetivo exacto es inalcanzable (por ejemplo, porque es una pared o está rodeado de vallas),
        /// intenta buscar el tile libre más cercano dentro del radio de tolerancia.
        /// </summary>
        /// <param name="npc">El NPC que se está moviendo (se usa su bounding box para calcular colisiones).</param>
        /// <param name="location">El mapa donde se realiza la búsqueda.</param>
        /// <param name="startTile">El tile de inicio.</param>
        /// <param name="targetTile">El tile de destino original.</param>
        /// <param name="maxIterations">Límite de iteraciones del A* para evitar cuelgues (por defecto 15,000).</param>
        /// <param name="toleranceRadius">Radio de búsqueda radial si el objetivo está bloqueado (por defecto 3).</param>
        /// <returns>Un Stack con la ruta de tiles (el tope es el siguiente paso), o null si es imposible encontrar ruta.</returns>
        public static Stack<Point> FindPath(NPC npc, GameLocation location, Point startTile, Point targetTile, int maxIterations = 15000, int toleranceRadius = 3)
        {
            // 1. Corrección del Destino (Target Correction)
            // Si el objetivo original no es caminable, buscamos el tile libre más cercano.
            Point actualTarget = GetWalkableTarget(npc, location, targetTile, toleranceRadius);

            // Si ni siquiera dentro del radio encontramos un lugar libre, fallamos.
            if (!IsTileWalkable(npc, location, actualTarget))
            {
                return null;
            }

            // 2. Ejecutar Algoritmo A* Optimizado
            return RunAStar(npc, location, startTile, actualTarget, maxIterations);
        }

        /// <summary>
        /// Realiza el algoritmo A* desde el inicio hasta el objetivo.
        /// Usa PriorityQueue y un array 2D para máximo rendimiento.
        /// </summary>
        private static Stack<Point> RunAStar(NPC npc, GameLocation location, Point startTile, Point targetTile, int maxIterations)
        {
            // Si ya estamos en el destino, no hay ruta que calcular.
            if (startTile == targetTile) return new Stack<Point>();

            int width = location.Map.Layers[0].LayerWidth;
            int height = location.Map.Layers[0].LayerHeight;

            // Arrays 2D para O(1) lookups en vez de hashsets/diccionarios pesados.
            bool[,] closedSet = new bool[width, height];
            Point?[,] cameFrom = new Point?[width, height];

            // Priority Queue (Disponible en .NET 6+)
            PriorityQueue<Point, int> openSet = new PriorityQueue<Point, int>();

            // Distancias conocidas (G-Cost). Inicializar solo lo necesario usando un Diccionario para no iterar todo el grid.
            // Aunque un array 2D para el G-Cost sería O(1), inicializar un int[,] de 200x200 con int.MaxValue toma tiempo extra en cada llamada.
            Dictionary<Point, int> gScore = new Dictionary<Point, int>();

            openSet.Enqueue(startTile, 0);
            gScore[startTile] = 0;

            int iterations = 0;

            while (openSet.Count > 0)
            {
                iterations++;
                if (iterations >= maxIterations)
                {
                    // Límite de seguridad superado. Devolvemos null en vez de congelar el juego.
                    return null;
                }

                Point current = openSet.Dequeue();

                // Si llegamos al destino, reconstruimos el path
                if (current == targetTile)
                {
                    return ReconstructPath(cameFrom, current);
                }

                closedSet[current.X, current.Y] = true;

                int currentGScore = gScore[current];

                foreach (Point dir in Directions)
                {
                    Point neighbor = new Point(current.X + dir.X, current.Y + dir.Y);

                    // Límites del mapa
                    if (neighbor.X < 0 || neighbor.Y < 0 || neighbor.X >= width || neighbor.Y >= height)
                        continue;

                    // Si ya lo evaluamos, ignorar
                    if (closedSet[neighbor.X, neighbor.Y])
                        continue;

                    // Verificar colisiones
                    if (!IsTileWalkable(npc, location, neighbor))
                        continue;

                    int tentativeGScore = currentGScore + 1; // El costo de moverse a un tile adyacente es 1

                    // Si no tiene score previo, o encontramos un camino más corto
                    if (!gScore.TryGetValue(neighbor, out int neighborGScore) || tentativeGScore < neighborGScore)
                    {
                        cameFrom[neighbor.X, neighbor.Y] = current;
                        gScore[neighbor] = tentativeGScore;

                        // Heurística de Manhattan
                        int fScore = tentativeGScore + Math.Abs(neighbor.X - targetTile.X) + Math.Abs(neighbor.Y - targetTile.Y);

                        openSet.Enqueue(neighbor, fScore);
                    }
                }
            }

            // No se encontró camino tras explorar todo el espacio disponible
            return null;
        }

        /// <summary>
        /// Reconstruye el path desde el nodo final yendo hacia atrás.
        /// Devuelve un Stack donde el tope es el SIGUIENTE paso a dar desde el origen.
        /// </summary>
        private static Stack<Point> ReconstructPath(Point?[,] cameFrom, Point current)
        {
            Stack<Point> path = new Stack<Point>();
            path.Push(current);

            Point? next = cameFrom[current.X, current.Y];
            while (next.HasValue)
            {
                // No metemos el tile de inicio en el stack final de movimientos
                if (cameFrom[next.Value.X, next.Value.Y] == null)
                {
                    break;
                }
                path.Push(next.Value);
                next = cameFrom[next.Value.X, next.Value.Y];
            }

            return path;
        }

        /// <summary>
        /// Búsqueda radial previa (Espiral/BFS): Si el destino está bloqueado, busca el tile caminable más cercano.
        /// </summary>
        private static Point GetWalkableTarget(NPC npc, GameLocation location, Point originalTarget, int radius)
        {
            if (IsTileWalkable(npc, location, originalTarget))
            {
                return originalTarget; // Está libre, usamos este.
            }

            // Si está bloqueado, hacemos un pequeño BFS radial para buscar el tile libre más cercano.
            Queue<Point> queue = new Queue<Point>();
            HashSet<Point> visited = new HashSet<Point>();

            queue.Enqueue(originalTarget);
            visited.Add(originalTarget);

            while (queue.Count > 0)
            {
                Point current = queue.Dequeue();

                // Si encontramos uno libre, lo devolvems (al ser BFS, garantizamos que es el más cercano espacialmente)
                if (IsTileWalkable(npc, location, current))
                {
                    return current;
                }

                foreach (Point dir in Directions)
                {
                    Point neighbor = new Point(current.X + dir.X, current.Y + dir.Y);

                    // Comprobamos el radio de tolerancia usando distancia de Manhattan
                    int dist = Math.Abs(neighbor.X - originalTarget.X) + Math.Abs(neighbor.Y - originalTarget.Y);
                    if (dist > radius) continue;

                    if (visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // Si no encontramos nada dentro del radio, devolvemos el original (el A* fallará posteriormente)
            return originalTarget;
        }

        /// <summary>
        /// Utiliza la lógica nativa del juego para determinar si un tile específico es caminable para este NPC.
        /// </summary>
        private static bool IsTileWalkable(NPC npc, GameLocation location, Point tile)
        {
            // Convertimos la coordenada del tile a un rectángulo de coordenadas en pixeles (Bounding Box).
            // Stardew Valley usa tiles de 64x64 pixeles (Game1.tileSize)
            Rectangle tileRect = new Rectangle(tile.X * 64, tile.Y * 64, 64, 64);

            // Verificamos colisión con las propiedades del mapa, edificios, muebles, vallas, etc.
            // Es la misma lógica que frena a un granjero o NPC.
            return !location.isCollidingPosition(tileRect, Game1.viewport, false, 0, false, npc);
        }

        /*
         * =========================================================================
         * INSTRUCCIONES DE INTEGRACIÓN
         * =========================================================================
         *
         * Para utilizar este pathfinder y asignarlo a un NPC, usa este código en tu
         * controlador o donde estés seteando el schedule/destino:
         *
         * // 1. Intentar obtener la ruta
         * Stack<Point> path = SmartPathfinder.FindPath(npc, npc.currentLocation, npc.TilePoint, targetTile);
         *
         * if (path != null && path.Count > 0)
         * {
         *     // 2. Crear un PathFindController nativo
         *     PathFindController controller = new PathFindController(npc, npc.currentLocation, path, targetTile);
         *
         *     // 3. Asignar el controlador al NPC para que empiece a caminar
         *     npc.controller = controller;
         * }
         * else
         * {
         *     // No se encontró ruta (el NPC está atrapado o el destino es absolutamente inalcanzable)
         *     // Manejar el fallback (ej: dejar al NPC donde está, hacer que espere, o teletransportar si es crítico).
         * }
         *
         * =========================================================================
         */
    }
}
