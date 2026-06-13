using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;

namespace StardewLivingValley.Services
{
    public static class CrossMapPathfinder
    {
        // Breadth-First Search to find path between maps using Warps
        public static MapRoute? FindMapPath(GameLocation startLocation, GameLocation targetLocation)
        {
            if (startLocation == null || targetLocation == null) return null;
            if (startLocation.NameOrUniqueName == targetLocation.NameOrUniqueName)
            {
                return new MapRoute {
                    Nodes = new List<MapNode> {
                        new MapNode {
                            LocationName = targetLocation.NameOrUniqueName,
                            IsFinalDestination = true
                        }
                    }
                };
            }

            Queue<List<Warp>> queue = new Queue<List<Warp>>();
            HashSet<string> visited = new HashSet<string>();

            // Init queue with outgoing warps from start location
            foreach (var warp in startLocation.warps)
            {
                queue.Enqueue(new List<Warp> { warp });
            }
            visited.Add(startLocation.NameOrUniqueName);

            // Also check doors/buildings on Farm
            AddBuildingWarps(startLocation, queue, visited);

            List<Warp>? successfulPath = null;

            while (queue.Count > 0)
            {
                var path = queue.Dequeue();
                var lastWarp = path[path.Count - 1];

                if (lastWarp.TargetName == targetLocation.NameOrUniqueName)
                {
                    successfulPath = path;
                    break;
                }

                if (!visited.Contains(lastWarp.TargetName))
                {
                    visited.Add(lastWarp.TargetName);
                    var nextLoc = Game1.getLocationFromName(lastWarp.TargetName);

                    // Note: If nextLoc is null, it might be a building indoors that hasn't been loaded properly or accessed
                    // For safety, if it's null we skip it for now.
                    if (nextLoc != null)
                    {
                        foreach (var nextWarp in nextLoc.warps)
                        {
                            if (!visited.Contains(nextWarp.TargetName))
                            {
                                var newPath = new List<Warp>(path) { nextWarp };
                                queue.Enqueue(newPath);
                            }
                        }
                        AddBuildingWarps(nextLoc, queue, visited, path);
                    }
                }
            }

            if (successfulPath != null)
            {
                var route = new MapRoute();
                foreach (var warp in successfulPath)
                {
                    route.Nodes.Add(new MapNode {
                        LocationName = warp.TargetName,
                        TargetWarpTile = new Point(warp.X, warp.Y),
                        ArrivalTile = new Point(warp.TargetX, warp.TargetY),
                        IsFinalDestination = false
                    });
                }
                if (route.Nodes.Count > 0)
                {
                    route.Nodes[route.Nodes.Count - 1].IsFinalDestination = true;
                }
                return route;
            }

            return null; // No path found
        }

        private static void AddBuildingWarps(GameLocation location, Queue<List<Warp>> queue, HashSet<string> visited, List<Warp>? currentPath = null)
        {
            // Farm buildings
            var farm = location;
            if (farm == null || (!farm.IsFarm && farm.Name != "Farm"))
            {
                farm = Game1.getLocationFromName("Farm");
                if (farm != null && farm.NameOrUniqueName != location.NameOrUniqueName)
                {
                    farm = null; // Only check if we are currently AT the farm/buildable location
                }
            }

            if (farm != null && farm.buildings != null)
            {
                foreach (var building in farm.buildings)
                {
                    if (building.indoors.Value != null)
                    {
                        string targetName = building.indoors.Value.NameOrUniqueName;
                        if (!visited.Contains(targetName))
                        {
                            var bWarp = new Warp(building.tileX.Value + building.doorX.Value, building.tileY.Value + building.doorY.Value, targetName, building.indoors.Value.warps.Count > 0 ? building.indoors.Value.warps[0].X : 2, building.indoors.Value.warps.Count > 0 ? building.indoors.Value.warps[0].Y : 2, false);

                            var newPath = currentPath == null ? new List<Warp>() : new List<Warp>(currentPath);
                            newPath.Add(bWarp);
                            queue.Enqueue(newPath);
                        }
                    }
                }
            }

            // Exiting buildings
            if (location.Name.StartsWith("Cabin") || location.Name.StartsWith("Coop") || location.Name.StartsWith("Barn") || location.Name.StartsWith("Shed") || location.Name.StartsWith("SlimeHutch"))
            {
                if (location.warps.Count > 0)
                {
                    // standard warps usually cover exiting, but ensure we check building exits if not explicitly in warps list
                }
            }
        }
    }
}
