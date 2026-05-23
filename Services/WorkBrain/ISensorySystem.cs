using Microsoft.Xna.Framework;
using StardewValley;
using System.Collections.Generic;

namespace LivingCompanionsValley.Services.WorkBrain
{
    public enum EntityType
    {
        Tree,
        Crop,
        Debris,
        Character,
        Player
    }

    public class PerceivedEntity
    {
        public EntityType Type { get; set; }
        public Vector2 TileLocation { get; set; }
        public object? Reference { get; set; } // Referencia al TerrainFeature u Object nativo
        public float Distance { get; set; }
    }

    /// <summary>
    /// Escáner espacial que lee el entorno y alimenta el SensoryCache del trabajador.
    /// Evita usar el término "Memoria" para no colisionar con el sistema LLM.
    /// </summary>
    public interface ISensorySystem
    {
        /// <summary>
        /// Realiza un barrido ligero del entorno en un radio definido.
        /// Debe catalogar objetos desde terrainFeatures, objects, resourceClumps y characters.
        /// </summary>
        void ScanEnvironment(GameLocation location, Vector2 currentTile, int radius);

        /// <summary>
        /// Retorna la lista de entidades percibidas recientemente (SensoryCache).
        /// </summary>
        IEnumerable<PerceivedEntity> GetSensoryCache();

        /// <summary>
        /// Limpia las entidades que ya no son válidas o han expirado del caché espacial.
        /// </summary>
        void PruneCache();
    }
}
