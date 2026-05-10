using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;

namespace LivingCompanionsValley.Services
{
    // Clases strongly-typed para el serializador JSON nativo (.NET 6)
    public class VeniceMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    public class VeniceParameters
    {
        [JsonPropertyName("include_venice_system_prompt")]
        public bool IncludeVeniceSystemPrompt { get; set; } = false;
    }

    public class ReasoningConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;
    }

    public class VeniceRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt_cache_key")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PromptCacheKey { get; set; }

        [JsonPropertyName("messages")]
        public List<VeniceMessage> Messages { get; set; } = new List<VeniceMessage>();

        [JsonPropertyName("venice_parameters")]
        public VeniceParameters VeniceParameters { get; set; } = new VeniceParameters();

        [JsonPropertyName("reasoning")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ReasoningConfig? Reasoning { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 250;

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.7;
    }

    public class VeniceApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly IMonitor _logger;
        private const string ApiUrl = "https://api.venice.ai/api/v1/chat/completions";

        // Arquitectura Dual-Model (Actualizada con modelos que soportan Prompt Caching)
        public const string ChatModel = "minimax-m25"; // Soporta Caching para 90% descuento.
        public const string ThinkingModel = "zai-org-glm-5"; // Pesado para extraer memoria y perfiles

        public VeniceApiService(string apiKey, IMonitor logger)
        {
            _apiKey = apiKey;
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        /// <summary>
        /// Envía una consulta a Venice API, respetando la estructura estricta de System Prompt y User Prompt.
        /// Soporta Prompt Caching para reducir costos.
        /// </summary>
        public async Task<string> GenerateResponseAsync(
            string staticSystemPrompt,
            string dynamicSystemContext,
            List<VeniceMessage>? chatHistory, 
            string currentUserMessage, 
            string modelName, 
            string? cacheKey,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.Log("Error: Venice API Key no está configurada.", LogLevel.Error);
                return "[Error de configuración]";
            }

            var requestPayload = new VeniceRequest
            {
                Model = modelName,
                PromptCacheKey = cacheKey,
                // El modelo de Chat usa menos tokens para ser rápido, pero el modelo Thinking necesita MUCHOS tokens para generar el bloque <think> y luego el JSON.
                MaxTokens = modelName == ChatModel ? 500 : 5000, 
                Temperature = 0.7,
                VeniceParameters = new VeniceParameters { IncludeVeniceSystemPrompt = false },
                // Desactivamos el razonamiento en el chat para obtener la máxima velocidad posible
                Reasoning = modelName == ChatModel ? new ReasoningConfig { Enabled = false } : null
            };

            // 1. El System Prompt establece el "cómo" (Reglas, Personalidad, Identidad)
            requestPayload.Messages.Add(new VeniceMessage { Role = "system", Content = staticSystemPrompt });

            // 2. Inyectamos la Memoria a Corto Plazo (Historial reciente)
            if (chatHistory != null)
            {
                requestPayload.Messages.AddRange(chatHistory);
            }

            // 3. Inyectamos el Lore Dinámico y Entorno como un segundo mensaje "system" justo antes del user.
            // Esto asegura que si el Dynamic Context fluctúa (ej. cambia la hora o el topic router añade un keyword),
            // NO rompe el Prefix Cache de todo el historial de chat anterior ni del Static Prompt.
            if (!string.IsNullOrWhiteSpace(dynamicSystemContext))
            {
                requestPayload.Messages.Add(new VeniceMessage { Role = "system", Content = dynamicSystemContext });
            }

            // 4. El User Prompt establece el "qué" (La interacción actual)
            requestPayload.Messages.Add(new VeniceMessage { Role = "user", Content = currentUserMessage });

            try
            {
                var jsonContent = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(ApiUrl, jsonContent, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.Log($"Venice API Error {response.StatusCode}: {error}", LogLevel.Error);
                    return "[Error de conexión con la IA]";
                }

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                using JsonDocument doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;
                
                // --- VERIFICACIÓN DE CACHÉ ---
                // Extraemos el objeto 'usage' para ver cuántos tokens se procesaron y cuántos fueron 'caché'
                if (root.TryGetProperty("usage", out var usage))
                {
                    _logger.Log($"\n[DEBUG CACHE MINIMAX] Uso de Tokens reportado por Venice:\n{usage.GetRawText()}\n", LogLevel.Info);
                }

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var message = choices[0].GetProperty("message");
                    return message.GetProperty("content").GetString()?.Trim() ?? "[Respuesta vacía]";
                }
                
                return "[Formato de respuesta desconocido]";
            }
            catch (TaskCanceledException)
            {
                _logger.Log("La llamada a Venice API fue cancelada (probablemente el jugador se alejó).", LogLevel.Info);
                return "[Cancelado]";
            }
            catch (Exception ex)
            {
                _logger.Log($"Excepción Venice API: {ex.Message}", LogLevel.Error);
                return "[Error interno]";
            }
        }
    }
}
