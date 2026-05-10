using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LivingCompanionsValley.Services
{
    /// <summary>
    /// Clasifica el mensaje del jugador (Paso A) y extrae el Lore/Memorias relevantes (Paso B).
    /// </summary>
    public class TopicRouterService
    {
        // En un futuro, esto puede usar un LLM muy ligero local o reglas regex avanzadas.
        // Por ahora, usaremos un mock simple para probar la arquitectura.
        
        /// <summary>
        /// Dado un mensaje de entrada, devuelve una lista de tags o temas detectados.
        /// </summary>
        public List<string> DetectTopics(string userMessage)
        {
            var topics = new List<string>();
            var msg = userMessage.ToLowerInvariant();

            if (msg.Contains("mina") || msg.Contains("mineral") || msg.Contains("cobre"))
                topics.Add("mining");
            
            if (msg.Contains("vaca") || msg.Contains("gallina") || msg.Contains("animal"))
                topics.Add("animals");

            if (msg.Contains("regalo") || msg.Contains("toma esto"))
                topics.Add("gifts");

            return topics;
        }

        /// <summary>
        /// Recupera el lore estático usando los temas detectados.
        /// </summary>
        public string[] GetRelevantLoreChunks(string npcName, List<string> topics)
        {
            // TODO: Buscar en el JSON cargado en memoria los fragmentos que hagan match con 'topics'.
            // Simulación:
            if (topics.Contains("animals") && npcName == "Marnie")
            {
                return new[] { "Marnie ama a sus vacas más que a nada. Vende animales en su rancho al sur de la granja del jugador." };
            }

            return Array.Empty<string>();
        }
    }
}
