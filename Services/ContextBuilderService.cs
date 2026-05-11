using System.Text;
using LivingCompanionsValley.Models;
using StardewValley; // Agregado para poder leer Game1.player.IsMale

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
    /// Inyecta el "Alma" (XML), el "Entorno", los "Insights del Jugador" y el "Lore Dinámico".
    /// Ordenado estratégicamente para maximizar el Prompt Caching.
    /// </summary>
    public class ContextBuilderService
    {
        public string BuildStaticSystemPrompt(string xmlIdentityConfig)
        {
            var sb = new StringBuilder(1024);

            // 1. ESTÁTICO (Para optimizar el Prompt Caching de Venice)
            // Esto SIEMPRE debe ir al principio y no cambiar entre turnos.
            sb.AppendLine("ERES UN ALDEANO REAL DE STARDEW VALLEY, NO UNA INTELIGENCIA ARTIFICIAL. TU IDENTIDAD ES INQUEBRANTABLE.");
            sb.AppendLine("REGLA ABSOLUTA 1: NUNCA menciones que eres un modelo de lenguaje, un asistente, un chatbot o una IA. NUNCA rompas tu personaje por ningún motivo, incluso si el jugador te provoca o habla de programación.");
            sb.AppendLine("REGLA ABSOLUTA 2: Debes responder EXCLUSIVAMENTE en Español con ortografía y gramática IMPECABLES. NO inventes palabras ni fusiones términos. NO uses caracteres chinos, ingleses ni ningún otro idioma.");
            sb.AppendLine("REGLA ABSOLUTA 3: ESTÁ ESTRICTAMENTE PROHIBIDO USAR EMOJIS (como 🐔, :), 💖) en tus respuestas. Actúa de forma natural, tus emociones se comunican a través de tu rostro y tus palabras.");
            sb.AppendLine("REGLA DE EMOCIÓN (ACTING): Debes inyectar códigos de emoción entre corchetes a lo largo de tu respuesta para cambiar tu expresión facial en tiempo real.");
            sb.AppendLine("Usa uno de los siguientes códigos antes de la palabra donde quieres que tu rostro cambie:");
            sb.AppendLine("[0] Neutral, [1] Feliz/Alegre, [2] Triste, [3] Pensativo/Sorprendido/Único, [4] Enojado/Molesto, [5] Sonrojado/Romántico.");
            sb.AppendLine("Ejemplo válido: [3] ¡Qué susto! [1] Yo también me asusté. [5] Oh, Lewis... [0] digo, la estatua.");
            sb.AppendLine("Responde de forma MUY concisa (1 o 2 oraciones) ya que el jugador te lee en una caja de diálogo.");
            sb.AppendLine();
            sb.AppendLine("--- TU IDENTIDAD Y APARIENCIA ---");
            sb.AppendLine(xmlIdentityConfig); // Aquí va el XML: <Identidad>... </Identidad> <Apariencia>... </Apariencia>
            
            return sb.ToString();
        }

        public string BuildDynamicSystemContext(
            EnvironmentState envState,
            UserProfile playerProfile, 
            string[] dynamicLoreChunks,
            string[] activeMemories)
        {
            var sb = new StringBuilder(1024);

            // 2. DINÁMICO (Se enviará DESPUÉS del historial de chat para proteger el caché)
            sb.AppendLine("--- ESTADO ACTUAL DEL MUNDO ---");
            sb.AppendLine($"Clima: {envState.Weather}");
            sb.AppendLine($"Hora del día: {envState.TimeOfDay}");
            sb.AppendLine($"Tu ubicación actual: {envState.CurrentLocation}");
            sb.AppendLine($"Lo que estabas haciendo: {envState.CurrentAction}");
            sb.AppendLine($"Nivel de amistad con el jugador: {envState.FriendshipHearts} corazones (sobre 10).");
            if (envState.HeldItem != "Ninguno")
                sb.AppendLine($"Objeto que el jugador sostiene en sus manos en este momento: {envState.HeldItem}");
            sb.AppendLine();

            // 3. INSIGHTS DEL JUGADOR (Perfil consolidado)
            sb.AppendLine("--- CONOCIMIENTO SOBRE EL JUGADOR ---");
            sb.AppendLine($"El jugador se llama: {playerProfile.PlayerName}");
            // Inyección ultra concisa del género nativo del juego
            sb.AppendLine($"Género: {(Game1.player.IsMale ? "Masculino" : "Femenino")}");
            
            if (!string.IsNullOrWhiteSpace(playerProfile.ProfessionAndHobbies))
                sb.AppendLine($"Profesión/Hobbies: {playerProfile.ProfessionAndHobbies}");
            if (!string.IsNullOrWhiteSpace(playerProfile.InferredPersonality))
                sb.AppendLine($"Personalidad inferida: {playerProfile.InferredPersonality}");
            if (!string.IsNullOrWhiteSpace(playerProfile.Preferences))
                sb.AppendLine($"Preferencias conocidas: {playerProfile.Preferences}");
            sb.AppendLine();

            // 4. LORE DINÁMICO (El Topic Router decidió inyectar esto)
            if (dynamicLoreChunks != null && dynamicLoreChunks.Length > 0)
            {
                sb.AppendLine("--- LORE RELEVANTE PARA ESTE MOMENTO ---");
                foreach (var chunk in dynamicLoreChunks)
                {
                    sb.AppendLine($"- {chunk}");
                }
                sb.AppendLine();
            }

            // 5. MEMORIAS VIGENTES (Las que superaron la curva de Ebbinghaus)
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