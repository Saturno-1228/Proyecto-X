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
            sb.AppendLine($"Clima: {envState.Weather}");
            sb.AppendLine($"Hora del día: {envState.TimeOfDay}");
            sb.AppendLine($"Tu ubicación actual: {envState.CurrentLocation}");
            sb.AppendLine($"Lo que estabas haciendo: {envState.CurrentAction}");
            sb.AppendLine($"Nivel de amistad con el jugador: {envState.FriendshipHearts} corazones (sobre 10).");
            if (envState.HeldItem != "Ninguno")
                sb.AppendLine($"Objeto que el jugador sostiene en sus manos: {envState.HeldItem}");
            sb.AppendLine();

            // 3. CONOCIMIENTO SOBRE EL JUGADOR (Incluye Género dinámico)
            sb.AppendLine("--- CONOCIMIENTO SOBRE EL JUGADOR ---");
            sb.AppendLine($"El jugador se llama: {playerProfile.PlayerName}");
            // Inyección de género para evitar errores de concordancia ("bienvenido" vs "bienvenida")
            sb.AppendLine($"Género: {(Game1.player.IsMale ? "Masculino" : "Femenino")}");
            
            if (!string.IsNullOrWhiteSpace(playerProfile.InferredPersonality))
                sb.AppendLine($"Personalidad inferida: {playerProfile.InferredPersonality}");
            sb.AppendLine();

            // 4. LORE DINÁMICO (Mantiene el esqueleto fijo para proteger el caché)
            sb.AppendLine("--- LORE RELEVANTE PARA ESTE MOMENTO ---");
            if (dynamicLoreChunks != null && dynamicLoreChunks.Length > 0)
            {
                foreach (var chunk in dynamicLoreChunks)
                {
                    sb.AppendLine($"- {chunk}");
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
                    sb.AppendLine($"- {mem}");
                }
            }

            return sb.ToString();
        }
    }
}