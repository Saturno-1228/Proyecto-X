using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using System.Collections.Generic;
using System.Linq;

namespace LivingCompanionsValley.Services.WorkBrain
{
    public class SensorySystem : ISensorySystem
    {
        private List<PerceivedEntity> _sensoryCache = new List<PerceivedEntity>();

        public IEnumerable<PerceivedEntity> GetSensoryCache()
        {
            return _sensoryCache;
        }

        public void PruneCache()
        {
            // Limpia entidades que estén muy lejos o que ya no existan.
            // Para simplificar, simplemente limpiaremos el caché antes de cada escaneo.
        }

        public void ScanEnvironment(GameLocation location, Vector2 currentTile, int radius)
        {
            if (location == null) return;

            _sensoryCache.Clear();

            // Escanear TerrainFeatures (Árboles, Hierba, etc.)
            foreach (var pair in location.terrainFeatures.Pairs)
            {
                if (Vector2.Distance(currentTile, pair.Key) <= radius)
                {
                    if (pair.Value is Tree tree)
                    {
                        _sensoryCache.Add(new PerceivedEntity
                        {
                            Type = EntityType.Tree,
                            TileLocation = pair.Key,
                            Reference = tree,
                            Distance = Vector2.Distance(currentTile, pair.Key)
                        });
                    }
                    else if (pair.Value is HoeDirt dirt && dirt.crop != null)
                    {
                        _sensoryCache.Add(new PerceivedEntity
                        {
                            Type = EntityType.Crop,
                            TileLocation = pair.Key,
                            Reference = dirt,
                            Distance = Vector2.Distance(currentTile, pair.Key)
                        });
                    }
                }
            }

            // Escanear Objects (Piedras, Ramas, Maleza)
            foreach (var pair in location.objects.Pairs)
            {
                if (Vector2.Distance(currentTile, pair.Key) <= radius)
                {
                    var obj = pair.Value;
                    if (obj.Name.Contains("Weed") || obj.Name.Contains("Stone") || obj.Name.Contains("Twig") || obj.IsWeeds() || obj.QualifiedItemId == "(O)343")
                    {
                        _sensoryCache.Add(new PerceivedEntity
                        {
                            Type = EntityType.Debris,
                            TileLocation = pair.Key,
                            Reference = obj,
                            Distance = Vector2.Distance(currentTile, pair.Key)
                        });
                    }
                }
            }

            // Escanear ResourceClumps (Rocas grandes, troncos)
            foreach (var clump in location.resourceClumps)
            {
                if (Vector2.Distance(currentTile, clump.Tile) <= radius)
                {
                    _sensoryCache.Add(new PerceivedEntity
                    {
                        Type = EntityType.Debris,
                        TileLocation = clump.Tile,
                        Reference = clump,
                        Distance = Vector2.Distance(currentTile, clump.Tile)
                    });
                }
            }

            // Ordenar por distancia (los más cercanos primero)
            _sensoryCache = _sensoryCache.OrderBy(e => e.Distance).ToList();
        }
    }
}
