using System.Text;
using LivingCompanionsValley.Models;
using StardewValley; // Necesario para detectar el género del jugador

namespace LivingCompanionsValley.Services
{
    /// <summary>
    /// Estructura para pasar el estado actual del juego al momento de hablar.
    /// </summary>
    public class EnvironmentState
    {
        public string Weather { get; set; } = "Soleado";
        public string TimeOfDay { get; set; } = "Mañana";
        public string CurrentLocation { get; set; } = "Pueblo Pelícano";
        public string CurrentAction { get; set; } = "Caminando";
        public int FriendshipHearts { get; set; } = 0;
        public string HeldItem { get; set; } = "Ninguno";
        
        // Fase 2.5: Consciencia Espacial y Social
        public string HealthStatus { get; set; } = "";
        public string EnergyStatus { get; set; } = "";
        public string NearbyWitnesses { get; set; } = "";
    }

    /// <summary>
    /// Se encarga exclusivamente de ensamblar el System Prompt.
    /// Ordenado estratégicamente para maximizar el Prompt Caching.
    /// </summary>
    public class ContextBuilderService
    {
        public string BuildStaticSystemPrompt(string xmlIdentityConfig)
        {
            var sb = new StringBuilder(1024);

            // 1. ESTÁTICO (Base del Caché)
            sb.AppendLine("ERES UN ALDEANO REAL DE STARDEW VALLEY, NO UNA INTELIGENCIA ARTIFICIAL. TU IDENTIDAD ES INQUEBRANTABLE.");
            sb.AppendLine("REGLA ABSOLUTA 1: NUNCA menciones que eres un modelo de lenguaje, un asistente, un chatbot o una IA. NUNCA rompas tu personaje por ningún motivo.");
            
            // Refuerzo de ortografía para evitar errores como "parachar"
            sb.AppendLine("REGLA ABSOLUTA 2: Debes responder EXCLUSIVAMENTE en Español con ortografía y gramática IMPECABLES. NO inventes palabras ni fusiones términos. NO uses caracteres chinos, ingleses ni ningún otro idioma.");
            
            sb.AppendLine("REGLA ABSOLUTA 3: ESTÁ ESTRICTAMENTE PROHIBIDO USAR EMOJIS (como 🐔, :), 💖) en tus respuestas.");
            sb.AppendLine("REGLA DE EMOCIÓN (ACTING): Debes inyectar códigos de emoción entre corchetes a lo largo de tu respuesta para cambiar tu expresión facial en tiempo real.");
            sb.AppendLine("Usa uno de los siguientes códigos antes de la palabra donde quieres que tu rostro cambie:");
            sb.AppendLine("[0] Neutral, [1] Feliz/Alegre, [2] Triste, [3] Pensativo/Sorprendido/Único, [4] Enojado/Molesto, [5] Sonrojado/Romántico.");
            sb.AppendLine("Responde de forma MUY concisa (1 o 2 oraciones) ya que el jugador te lee en una caja de diálogo.");
            sb.AppendLine();
            sb.AppendLine("--- TU IDENTIDAD Y APARIENCIA ---");
            sb.AppendLine(xmlIdentityConfig); 
            
            return sb.ToString();
        }

        public string BuildDynamicSystemContext(
            EnvironmentState envState,
            UserProfile playerProfile, 
            string[] dynamicLoreChunks,
            string[] activeMemories)
        {
            var sb = new StringBuilder(1024);

            // 2. DINÁMICO (Estado del Mundo)
            sb.AppendLine("--- ESTADO ACTUAL DEL MUNDO ---");
            sb.Append("Clima: ").AppendLine(envState.Weather);
            sb.Append("Hora del día: ").AppendLine(envState.TimeOfDay);
            sb.Append("Tu ubicación actual: ").AppendLine(envState.CurrentLocation);
            sb.Append("Lo que estabas haciendo: ").AppendLine(envState.CurrentAction);
            sb.Append("Nivel de amistad con el jugador: ").Append(envState.FriendshipHearts).AppendLine(" corazones (sobre 10).");
            if (envState.HeldItem != "Ninguno")
                sb.Append("Objeto que el jugador sostiene en sus manos: ").AppendLine(envState.HeldItem);
                
            // Inyecciones de Fase 2.5
            if (!string.IsNullOrEmpty(envState.HealthStatus))
                sb.Append("Estado de salud del jugador: ").AppendLine(envState.HealthStatus);
            if (!string.IsNullOrEmpty(envState.EnergyStatus))
                sb.Append("Estado de energía del jugador: ").AppendLine(envState.EnergyStatus);
            if (!string.IsNullOrEmpty(envState.NearbyWitnesses))
                sb.Append("¡OJO! Hay testigos cerca escuchando esta charla: ").AppendLine(envState.NearbyWitnesses);
                
            sb.AppendLine();

            // 3. CONOCIMIENTO SOBRE EL JUGADOR (Incluye Género dinámico)
            sb.AppendLine("--- CONOCIMIENTO SOBRE EL JUGADOR ---");
            sb.Append("El jugador se llama: ").AppendLine(playerProfile.PlayerName);
            // Inyección de género para evitar errores de concordancia ("bienvenido" vs "bienvenida")
            sb.Append("Género: ").AppendLine(Game1.player.IsMale ? "Masculino" : "Femenino");
            
            if (!string.IsNullOrWhiteSpace(playerProfile.InferredPersonality))
                sb.Append("Personalidad inferida: ").AppendLine(playerProfile.InferredPersonality);
            sb.AppendLine();

            // 4. LORE DINÁMICO (Mantiene el esqueleto fijo para proteger el caché)
            sb.AppendLine("--- LORE RELEVANTE PARA ESTE MOMENTO ---");
            if (dynamicLoreChunks != null && dynamicLoreChunks.Length > 0)
            {
                foreach (var chunk in dynamicLoreChunks)
                {
                    sb.Append("- ").AppendLine(chunk);
                }
            }
            else
            {
                // Texto por defecto para que la estructura del prompt no cambie y rompa el caché
                sb.AppendLine("- No hay conocimiento específico desencadenado en este turno.");
            }
            sb.AppendLine();

            // 5. MEMORIAS VIGENTES
            if (activeMemories != null && activeMemories.Length > 0)
            {
                sb.AppendLine("--- TUS RECUERDOS RECIENTES ---");
                foreach (var mem in activeMemories)
                {
                    sb.Append("- ").AppendLine(mem);
                }
            }

            return sb.ToString();
        }
    }
}