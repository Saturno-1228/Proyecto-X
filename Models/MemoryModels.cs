using System;
using System.Collections.Generic;

namespace LivingCompanionsValley.Models
{
    // ──────────────────────────────────────────────
    //  Organic Memory System (Cerebro & Perfil)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Perfil psicológico y demográfico del Jugador (Insights) que el NPC construye con el tiempo.
    /// Esto es consolidado por el modelo pesado (ej. GLM-5) asíncronamente.
    /// </summary>
    public class UserProfile
    {
        public string PlayerName { get; set; } = string.Empty;
        
        /// <summary>Ej: Granjero novato, aventurero, le gusta pescar.</summary>
        public string ProfessionAndHobbies { get; set; } = string.Empty;
        
        /// <summary>Basado estrictamente en cómo interactúa el jugador (Amable, sarcástico, grosero).</summary>
        public string InferredPersonality { get; set; } = string.Empty;
        
        /// <summary>Rasgos físicos observados o mencionados en el juego (no alucinados).</summary>
        public string ObservedTraits { get; set; } = string.Empty;
        
        /// <summary>Gustos o aversiones confirmadas (Ej: Odia la mayonesa, le encanta la minería).</summary>
        public string Preferences { get; set; } = string.Empty;
    }

    public enum MemoryType
    {
        Episodic,
        LearnedFact,
        EmotionalAnchor
    }

    /// <summary>
    /// Una memoria orgánica de un NPC, inspirada en la curva de Ebbinghaus.
    /// </summary>
    public class NpcMemory
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string NpcName { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;
        public string CompressedContent { get; set; } = string.Empty;
        public string[] Tags { get; set; } = Array.Empty<string>();

        /// <summary>Fuerza del recuerdo (0.0 a 1.0). Decae diariamente, se refuerza al recordar.</summary>
        public float Strength { get; set; } = 1.0f;
        public float EmotionalWeight { get; set; } = 0.0f;

        public int CreatedDay { get; set; } = 0;
        public int LastRecalledDay { get; set; } = 0;
        public int RecallCount { get; set; } = 0;

        public MemoryType Type { get; set; } = MemoryType.Episodic;
    }

    /// <summary>
    /// La red neuronal completa de un NPC. Este es el objeto raíz que se serializará
    /// e inyectará en el archivo de guardado del jugador (SaveData) mediante SMAPI.
    /// </summary>
    public class NpcMemoryNetwork
    {
        public string NpcName { get; set; } = string.Empty;
        
        public UserProfile PlayerProfile { get; set; } = new UserProfile();
        
        /// <summary>Memorias con Strength > 0.2. Se escanean regularmente.</summary>
        public List<NpcMemory> ActiveMemories { get; set; } = new List<NpcMemory>();

        /// <summary>
        /// El Limbo: Memorias con Strength < 0.2. No se envían a Venice en charlas normales.
        /// Se consultan ÚNICAMENTE si el jugador reclama que el NPC ha olvidado algo.
        /// </summary>
        public List<NpcMemory> ForgottenMemories { get; set; } = new List<NpcMemory>();
    }
}
