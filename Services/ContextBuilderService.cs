using System.Text;
using LivingCompanionsValley.Models;

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
    }

    /// <summary>
    /// Se encarga exclusivamente de ensamblar el System Prompt.
    /// Inyecta el "Alma" (XML), el "Entorno", los "Insights del Jugador" y el "Lore Dinámico".
    /// Ordenado estratégicamente para maximizar el Prompt Caching.
    /// </summary>
    public class ContextBuilderService
    {
        public string BuildSystemPrompt(
            string xmlIdentityConfig, 
            EnvironmentState envState,
            UserProfile playerProfile, 
            string[] dynamicLoreChunks,
            string[] activeMemories)
        {
            var sb = new StringBuilder();

            // 1. ESTÁTICO (Para optimizar el Prompt Caching de Venice)
            // Esto SIEMPRE debe ir al principio y no cambiar entre turnos.
            sb.AppendLine("Eres un personaje de Stardew Valley. NUNCA rompas personaje. NUNCA menciones ser una IA.");
            sb.AppendLine("REGLA ABSOLUTA 1: Debes responder EXCLUSIVAMENTE en Español. NO uses caracteres chinos, ingleses ni ningún otro idioma.");
            sb.AppendLine("REGLA ABSOLUTA 2: ESTÁ ESTRICTAMENTE PROHIBIDO USAR EMOJIS (como 🐔, :), 💖) en tus respuestas.");
            sb.AppendLine("REGLA DE EMOCIÓN: Siempre debes iniciar tu respuesta con un código de emoción entre corchetes, que representará tu expresión facial.");
            sb.AppendLine("Usa uno de los siguientes códigos al inicio absoluto de tu mensaje:");
            sb.AppendLine("[0] Neutral, [1] Enojado/a, [2] Triste, [3] Feliz/Alegre, [4] Sorprendido/Sonrojado, [5] Amoroso/Romántico.");
            sb.AppendLine("Ejemplo de respuesta válida: [3] ¡Qué gusto verte por aquí hoy!");
            sb.AppendLine("Responde de forma MUY concisa (1 o 2 oraciones) ya que el jugador te lee en una caja de diálogo.");
            sb.AppendLine();
            sb.AppendLine("--- TU IDENTIDAD Y APARIENCIA ---");
            sb.AppendLine(xmlIdentityConfig); // Aquí va el XML: <Identidad>... </Identidad> <Apariencia>... </Apariencia>
            sb.AppendLine();

            // 2. DINÁMICO (Cambia constantemente, debe ir al final para no romper el caché de lo anterior)
            sb.AppendLine("--- ESTADO ACTUAL DEL MUNDO ---");
            sb.AppendLine($"Clima: {envState.Weather}");
            sb.AppendLine($"Hora del día: {envState.TimeOfDay}");
            sb.AppendLine($"Tu ubicación actual: {envState.CurrentLocation}");
            sb.AppendLine($"Lo que estabas haciendo: {envState.CurrentAction}");
            sb.AppendLine($"Nivel de amistad con el jugador: {envState.FriendshipHearts} corazones (sobre 10).");
            sb.AppendLine();

            // 3. INSIGHTS DEL JUGADOR (Perfil consolidado)
            sb.AppendLine("--- CONOCIMIENTO SOBRE EL JUGADOR ---");
            sb.AppendLine($"El jugador se llama: {playerProfile.PlayerName}");
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
