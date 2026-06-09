using System.Text;
using StardewValley;
using StardewLivingValley.Models;

namespace StardewLivingValley.Services
{
    public class EnvironmentState
    {
        public string Weather { get; set; } = "Soleado";
        public string TimeOfDay { get; set; } = "Mañana";
        public string CurrentLocation { get; set; } = "Pueblo Pelícano";
        public int FriendshipHearts { get; set; } = 0;
        public string HeldItem { get; set; } = "Ninguno";
    }

    public class ContextBuilderService
    {
        public string BuildStaticSystemPrompt(NpcKnowledgeProfile profile)
        {
            var sb = new StringBuilder(1024);

            sb.AppendLine("ERES UN ALDEANO REAL DE STARDEW VALLEY, NO UNA INTELIGENCIA ARTIFICIAL. TU IDENTIDAD ES INQUEBRANTABLE.");
            sb.AppendLine("REGLA 1: NUNCA menciones que eres un modelo de lenguaje o IA. NO rompas personaje.");
            sb.AppendLine("REGLA 2: Responde EXCLUSIVAMENTE en Español con ortografía perfecta.");
            sb.AppendLine("REGLA 3: PROHIBIDO USAR EMOJIS.");
            sb.AppendLine("REGLA DE EMOCIÓN: Debes inyectar un código de emoción entre corchetes ANTES de tu texto para cambiar tu expresión facial.");
            sb.AppendLine("Opciones: [0] Neutral, [1] Feliz, [2] Triste, [3] Sorprendido/Pensativo, [4] Enojado, [5] Sonrojado.");
            sb.AppendLine("Responde de forma concisa (1 o 2 oraciones) ya que es un juego.");

            sb.AppendLine("\n--- TU IDENTIDAD Y ROL ---");
            sb.AppendLine(profile.Role);
            sb.AppendLine(profile.Persona);
            sb.AppendLine(profile.Speech);
            
            sb.AppendLine("\n--- LÍMITES ESTRICTOS (BOUNDARIES) ---");
            sb.AppendLine(profile.Boundaries);

            if (profile.ForbiddenClaims != null && profile.ForbiddenClaims.Count > 0)
            {
                sb.AppendLine("\n--- NUNCA DEBES AFIRMAR LO SIGUIENTE ---");
                foreach (var claim in profile.ForbiddenClaims)
                {
                    sb.AppendLine($"- {claim}");
                }
            }

            return sb.ToString();
        }

        public string BuildDynamicSystemContext(
            EnvironmentState envState,
            UserProfile playerProfile, 
            string[] dynamicLoreChunks,
            string[] activeMemories)
        {
            var sb = new StringBuilder(1024);

            sb.AppendLine("--- ESTADO ACTUAL DEL MUNDO ---");
            sb.Append("Clima: ").AppendLine(envState.Weather);
            sb.Append("Hora: ").AppendLine(envState.TimeOfDay);
            sb.Append("Ubicación: ").AppendLine(envState.CurrentLocation);
            sb.Append("Amistad: ").Append(envState.FriendshipHearts).AppendLine(" corazones.");
            sb.Append("Objeto en mano del jugador: ").AppendLine(envState.HeldItem);

            sb.AppendLine("\n--- SOBRE EL JUGADOR ---");
            sb.Append("Nombre: ").AppendLine(playerProfile.PlayerName);
            sb.Append("Género: ").AppendLine(Game1.player != null && Game1.player.IsMale ? "Masculino" : "Femenino");

            sb.AppendLine("\n--- LORE DINÁMICO RELEVANTE AHORA ---");
            if (dynamicLoreChunks != null && dynamicLoreChunks.Length > 0)
            {
                foreach (var chunk in dynamicLoreChunks)
                {
                    sb.Append("- ").AppendLine(chunk);
                }
            }
            else
            {
                sb.AppendLine("- No hay conocimiento específico desencadenado en este turno.");
            }

            if (activeMemories != null && activeMemories.Length > 0)
            {
                sb.AppendLine("\n--- TUS RECUERDOS RECIENTES ---");
                foreach (var mem in activeMemories)
                {
                    sb.Append("- ").AppendLine(mem);
                }
            }

            return sb.ToString();
        }
    }
}
