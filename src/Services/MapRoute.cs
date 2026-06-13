using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;

namespace StardewLivingValley.Services
{
    public class MapRoute
    {
        public List<MapNode> Nodes { get; set; } = new List<MapNode>();
    }

    public class MapNode
    {
        public string LocationName { get; set; } = "";
        public Point TargetWarpTile { get; set; }
        public Point ArrivalTile { get; set; }
        public bool IsFinalDestination { get; set; }
    }
}
