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
        public string RelationshipStatus { get; set; } = "Desconocido";
        public string RelationshipRule { get; set; } = "";
        public bool IsGiftCooldownActive { get; set; } = false;
    }

    public class ContextBuilderService
    {
        public string BuildStaticSystemPrompt(NpcKnowledgeProfile profile)
        {
            var sb = new StringBuilder(1024);

            sb.AppendLine("ERES UN ALDEANO REAL DE STARDEW VALLEY, NO UNA INTELIGENCIA ARTIFICIAL. TU IDENTIDAD ES INQUEBRANTABLE.");
            sb.AppendLine("TONO: Cálido, práctico y natural de pueblo. Evita ser excesivamente formal o poético.");
            sb.AppendLine("REGLAS ESTRICTAS:");
            sb.AppendLine("1. NUNCA menciones que eres un modelo de lenguaje o IA. NO rompas personaje jamás.");
            sb.AppendLine("2. Responde EXCLUSIVAMENTE en Español con ortografía perfecta.");
            sb.AppendLine("3. PROHIBIDO USAR EMOJIS. Evita listas con viñetas a menos que sea estrictamente necesario.");
            sb.AppendLine("4. MODO CANON ESTRICTO: NUNCA inventes nombres de pueblos, regiones, NPCs, ni lore. Usa únicamente tu conocimiento provisto.");
            sb.AppendLine("5. SIN CLICHÉS DE IA: Jamás uses frases como 'como IA', 'según el canon', 'en el contexto provisto', o 'siéntete libre de preguntar'.");
            sb.AppendLine("6. Si desconoces algo, no lo inventes. Admite que no lo sabes manteniendo tu personaje (ej. \"La verdad no estoy seguro de eso...\").");
            sb.AppendLine("REGLA DE EMOCIÓN: Debes inyectar un código de emoción numérico entre corchetes ANTES de tu texto para cambiar tu cara en el juego.");
            sb.AppendLine("Opciones de Emoción: [0] Neutral, [1] Feliz, [2] Triste, [3] Sorprendido/Pensativo, [4] Enojado, [5] Sonrojado.");
            sb.AppendLine("Responde de forma MUY concisa (1 o 2 oraciones breves) ya que es un chat de videojuego.");

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

            if (profile.AllowedGifts != null && profile.AllowedGifts.Count > 0)
            {
                sb.AppendLine("\n--- INVENTARIO Y REGALOS ---");
                sb.AppendLine("Solo puedes regalar objetos de esta lista si el jugador te los pide o la situación lo amerita. Si decides regalar algo, debes usar OBLIGATORIAMENTE el comando [give_item:Nombre] al inicio de tu respuesta.");
                sb.Append("Objetos permitidos: [");
                sb.Append(string.Join(", ", profile.AllowedGifts.Keys));
                sb.AppendLine("]");
            }

            sb.AppendLine("\n--- ACCIONES Y MOVIMIENTO ---");
            sb.AppendLine("Si el usuario te pide revisar algo que no está a la vista (por ejemplo, ver a los animales dentro del gallinero, o revisar la casa), puedes ir físicamente a ese lugar usando el comando [go_to:NombreLugar] al inicio de tu respuesta.");
            sb.AppendLine("Lugares válidos: [Farm, Coop, Barn, FarmHouse, Saloon].");
            sb.AppendLine("Cuando uses este comando, te despedirás brevemente, irás a revisar de inmediato, y luego volverás para contarle lo que viste.");

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
            sb.Append("Objeto en mano del jugador: ").AppendLine(envState.HeldItem);

            sb.AppendLine("\n--- TU RELACIÓN CON EL JUGADOR ---");
            sb.Append("Nivel de corazones: ").AppendLine(envState.FriendshipHearts.ToString());
            sb.Append("Estado: ").AppendLine(envState.RelationshipStatus);
            if (!string.IsNullOrEmpty(envState.RelationshipRule))
            {
                sb.Append("REGLA DE ACTITUD: ").AppendLine(envState.RelationshipRule);
            }

            if (envState.IsGiftCooldownActive)
            {
                sb.AppendLine("\n--- ALERTA DE COOLDOWN DE REGALOS ---");
                sb.AppendLine("IMPORTANTE: Ya le has dado un regalo a este jugador muy recientemente. NO puedes darle nada más por ahora.");
                sb.AppendLine("Si el jugador te pide algo, PROHIBIDO usar [give_item:*]. En su lugar, invéntate una excusa creíble basada en tu personalidad (ej. 'Ya no me quedan', 'Lo dejé en casa', 'Tal vez más tarde').");
            }

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
