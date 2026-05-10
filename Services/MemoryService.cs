using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using StardewModdingAPI;
using LivingCompanionsValley.Models;

namespace LivingCompanionsValley.Services
{
    // DTO para deserializar de forma segura la respuesta de GLM-5
    public class MemoryExtractionResult
    {
        public class ExtractedMemory
        {
            [System.Text.Json.Serialization.JsonPropertyName("content")]
            public string Content { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("type")]
            public string Type { get; set; } = "Episodic";
        }

        public class ProfileUpdates
        {
            [System.Text.Json.Serialization.JsonPropertyName("profession")]
            public string Profession { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("preferences")]
            public string Preferences { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("inferred_personality")]
            public string InferredPersonality { get; set; } = string.Empty;
        }

        [System.Text.Json.Serialization.JsonPropertyName("new_memories")]
        public List<ExtractedMemory> NewMemories { get; set; } = new List<ExtractedMemory>();

        [System.Text.Json.Serialization.JsonPropertyName("profile_updates")]
        public ProfileUpdates Updates { get; set; } = new ProfileUpdates();
    }

    public class MemoryService
    {
        private readonly IModHelper _helper;
        private readonly IMonitor _logger;

        public MemoryService(IModHelper helper, IMonitor logger)
        {
            _helper = helper;
            _logger = logger;
        }

        /// <summary>
        /// Obtiene la red de memoria de un NPC desde el archivo de guardado nativo de SMAPI.
        /// </summary>
        public NpcMemoryNetwork GetMemoryNetwork(string npcName)
        {
            // SMAPI exige que la key solo contenga letras, números, guiones bajos, puntos o guiones.
            var sanitizedName = System.Text.RegularExpressions.Regex.Replace(npcName, @"[^a-zA-Z0-9_\.\-]", "_");
            var saveKey = $"Memory_{sanitizedName}";
            var network = _helper.Data.ReadSaveData<NpcMemoryNetwork>(saveKey);
            
            if (network == null)
            {
                network = new NpcMemoryNetwork { NpcName = npcName };
            }
            return network;
        }

        /// <summary>
        /// Guarda la red de memoria en el archivo de guardado de SMAPI.
        /// </summary>
        public void SaveMemoryNetwork(NpcMemoryNetwork network)
        {
            var sanitizedName = System.Text.RegularExpressions.Regex.Replace(network.NpcName, @"[^a-zA-Z0-9_\.\-]", "_");
            var saveKey = $"Memory_{sanitizedName}";
            _helper.Data.WriteSaveData(saveKey, network);
        }

        /// <summary>
        /// Se ejecuta cada mañana en Stardew Valley.
        /// Aplica la curva del olvido (Decaimiento de 10 días).
        /// Envía los recuerdos muy débiles al Limbo.
        /// </summary>
        public void ProcessDailyDecay(string npcName)
        {
            // Evitar crear/procesar memorias para caballos, mascotas o NPCs no soportados
            string xmlPath = System.IO.Path.Combine(_helper.DirectoryPath, "Assets", "Lore", $"{npcName}.xml");
            if (!System.IO.File.Exists(xmlPath))
            {
                return;
            }

            var network = GetMemoryNetwork(npcName);
            var memoriesToForget = new List<NpcMemory>();

            foreach (var memory in network.ActiveMemories)
            {
                // Decaimiento basado en el tipo (La idea de los 10 días = -0.1f)
                switch (memory.Type)
                {
                    case MemoryType.Episodic:
                        memory.Strength -= 0.10f; // Tarda ~10 días en olvidarse sin refuerzo
                        break;
                    case MemoryType.LearnedFact:
                        memory.Strength -= 0.03f; // Tarda ~33 días
                        break;
                    case MemoryType.EmotionalAnchor:
                        memory.Strength -= 0.005f; // Casi permanente (Ej. Regalos amados)
                        break;
                }

                if (memory.Strength < 0.2f)
                {
                    memoriesToForget.Add(memory);
                }
            }

            // Mover al Limbo (Para poder decir "Oh, lo olvidé, lo siento")
            foreach (var forgotten in memoriesToForget)
            {
                network.ActiveMemories.Remove(forgotten);
                network.ForgottenMemories.Add(forgotten);
                _logger.Log($"[{npcName}] ha olvidado: {forgotten.Content}. Enviado al Limbo.", LogLevel.Debug);
            }

            // --- NUEVO: Decaimiento en el Limbo ---
            // Las memorias en el Limbo siguen perdiendo fuerza. 
            // Entran con ~0.2. Si restamos 0.1 por día, en 10 días llegarán a -0.8.
            var memoriesToHardDelete = new List<NpcMemory>();
            foreach (var limboMem in network.ForgottenMemories)
            {
                limboMem.Strength -= 0.10f;
                if (limboMem.Strength <= -0.8f)
                {
                    memoriesToHardDelete.Add(limboMem);
                }
            }

            foreach (var deadMem in memoriesToHardDelete)
            {
                network.ForgottenMemories.Remove(deadMem);
                _logger.Log($"[{npcName}] Memoria borrada permanentemente del Limbo (Muerte neuronal): {deadMem.Content}", LogLevel.Trace);
            }

            SaveMemoryNetwork(network);
        }
        
        /// <summary>
        /// Refuerza una memoria (Vuelve su Strength a 1.0 y la puede volver Permanente/LearnedFact)
        /// </summary>
        public void ReinforceMemory(string npcName, NpcMemory memory)
        {
            var network = GetMemoryNetwork(npcName);
            
            // Si estaba en el limbo, la revivimos
            if (network.ForgottenMemories.Any(m => m.Id == memory.Id))
            {
                network.ForgottenMemories.RemoveAll(m => m.Id == memory.Id);
                network.ActiveMemories.Add(memory);
            }

            memory.Strength = 1.0f;
            memory.RecallCount++;

            // Si se repite mucho (ej: 3 veces), se vuelve un dato duro y ya casi no decae
            if (memory.RecallCount >= 3 && memory.Type == MemoryType.Episodic)
            {
                memory.Type = MemoryType.LearnedFact;
                _logger.Log($"[{npcName}] Memoria convertida en Permanente por repetición: {memory.Content}", LogLevel.Debug);
            }

            SaveMemoryNetwork(network);
        }

        // TODO: Añadir ConsolidateMemoriesAsync (Para llamar a GLM-5.1 y crear nuevos recuerdos).
        
        /// <summary>
        /// Proceso Asíncrono de Consolidación (El Sueño del NPC).
        /// Llama al modelo GLM-5 pesado para analizar la charla y extraer memorias, 
        /// forzando el formato estricto de Thinking Protocol solicitado por el usuario.
        /// </summary>
        public async Task ConsolidateMemoriesAsync(string npcName, string rawChatHistory, VeniceApiService veniceApi, CancellationToken ct)
        {
            var systemPrompt = @"
<thinking_protocol>
INSTRUCCIÓN CRÍTICA: Eres el subconsciente cognitivo de un NPC. Tu única tarea es analizar el historial de la conversación reciente con el jugador y consolidar su memoria a largo y corto plazo.
SIEMPRE debes usar el siguiente formato de pensamiento interno antes de generar tu salida JSON:

    1. RECEPCIÓN: 
       - ¿De qué hablaron el jugador y el NPC hoy? (Resumen súper breve).

    2. EVALUACIÓN DE MEMORIAS (Episodic):
       - ¿Hay algún evento trivial o dato casual mencionado en esta charla que debamos recordar a corto plazo? (Si=Extraer a new_memories con tipo Episodic).

    3. EVALUACIÓN DE PERFIL (LearnedFact / Insights):
       - ¿El jugador reveló su profesión, gustos, aficiones o mostró una actitud particular? (Si=Extraer a profile_updates).

    4. IMPACTO EMOCIONAL (EmotionalAnchor):
       - ¿Hubo un regalo increíble, una revelación de un secreto, o un evento muy profundo? (Si=Extraer a new_memories con tipo EmotionalAnchor).

    5. VERIFICACIÓN:
       - Escaneo final: ¿Mis extracciones son estrictamente veraces según el historial crudo y no inventadas? [✓]
</thinking_protocol>

INSTRUCCIÓN FINAL: Después de concluir tu bloque de pensamiento <thinking_protocol>, tu respuesta FINAL debe ser ÚNICA y ESTRICTAMENTE un objeto JSON válido con la siguiente estructura (no añadas markdown de código al json):
{
  ""new_memories"": [
    { ""content"": ""Breve resumen del recuerdo"", ""type"": ""Episodic"" o ""EmotionalAnchor"" }
  ],
  ""profile_updates"": {
    ""profession"": ""Datos sobre su trabajo u hobbies detectados"",
    ""preferences"": ""Gustos detectados en esta charla"",
    ""inferred_personality"": ""Actitud del jugador inferida (ej: amable, impaciente)""
  }
}";
            
            _logger.Log($"[{npcName}] Iniciando consolidación asíncrona de memoria con GLM-5...", LogLevel.Trace);
            
            var stopwatch = Stopwatch.StartNew();
            
            // Hacemos la llamada al modelo pesado (ThinkingModel)
            var jsonResponse = await veniceApi.GenerateResponseAsync(systemPrompt, "", null, "Historial de la charla a procesar:\n" + rawChatHistory, VeniceApiService.ThinkingModel, null, ct);
            
            stopwatch.Stop();
            _logger.Log($"[{npcName}] Tiempo total de 'Thinking' y generación de GLM-5: {stopwatch.Elapsed.TotalSeconds:F2} segundos.", LogLevel.Info);
            
            // Limpieza del output para asegurar que solo quede el JSON (por si el modelo incluye comillas de markdown)
            var cleanJson = jsonResponse;
            if (cleanJson.Contains("```json"))
            {
                 var startIndex = cleanJson.IndexOf("```json") + 7;
                 var endIndex = cleanJson.LastIndexOf("```");
                 if (endIndex > startIndex) cleanJson = cleanJson.Substring(startIndex, endIndex - startIndex).Trim();
            }
            else if (cleanJson.Contains("{") && cleanJson.LastIndexOf("}") > cleanJson.IndexOf("{"))
            {
                 cleanJson = cleanJson.Substring(cleanJson.IndexOf("{"), cleanJson.LastIndexOf("}") - cleanJson.IndexOf("{") + 1);
            }

            try
            {
                var extraction = System.Text.Json.JsonSerializer.Deserialize<MemoryExtractionResult>(cleanJson);
                if (extraction != null)
                {
                    var network = GetMemoryNetwork(npcName);
                    
                    // Insertar las nuevas memorias detectadas
                    foreach(var mem in extraction.NewMemories)
                    {
                        var type = mem.Type == "EmotionalAnchor" ? MemoryType.EmotionalAnchor : MemoryType.Episodic;
                        network.ActiveMemories.Add(new NpcMemory { Content = mem.Content, Type = type, NpcName = npcName });
                        _logger.Log($"[{npcName}] Nueva memoria consolidada y guardada: {mem.Content} ({type})", LogLevel.Debug);
                    }

                    // Refinar el perfil si se detectaron nuevos datos
                    if (!string.IsNullOrWhiteSpace(extraction.Updates.Profession))
                        network.PlayerProfile.ProfessionAndHobbies += " | " + extraction.Updates.Profession;
                    if (!string.IsNullOrWhiteSpace(extraction.Updates.Preferences))
                        network.PlayerProfile.Preferences += " | " + extraction.Updates.Preferences;
                    if (!string.IsNullOrWhiteSpace(extraction.Updates.InferredPersonality))
                        network.PlayerProfile.InferredPersonality = extraction.Updates.InferredPersonality; // Sobreescribimos con la impresión más reciente
                    
                    SaveMemoryNetwork(network);
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"[{npcName}] Error al consolidar memoria (JSON Inválido). Respuesta cruda: {jsonResponse}. Error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
