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
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Enabled { get; set; }

        [JsonPropertyName("effort")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Effort { get; set; }
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

        // Arquitectura Dual-Model
        public const string ChatModel = "kimi-k2-5"; // Nuevo modelo rápido con caché y razonamiento
        public const string ThinkingModel = "zai-org-glm-5"; // Modelo para consolidación

        public VeniceApiService(string apiKey, IMonitor logger)
        {
            _apiKey = apiKey;
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        /// <summary>
        /// Genera respuesta usando el sistema de sándwich para optimizar el caché y aplica razonamiento medio en chat.
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
                MaxTokens = modelName == ChatModel ? 2000 : 5000, 
                Temperature = 0.7,
                VeniceParameters = new VeniceParameters { IncludeVeniceSystemPrompt = false },
                
                // Configuración de razonamiento: "medium" para el modelo de chat
                Reasoning = modelName == ChatModel ? new ReasoningConfig { Effort = "medium" } : null
            };

            // 1. Capa Estática: Identidad y Reglas (Base del Caché)
            requestPayload.Messages.Add(new VeniceMessage { Role = "system", Content = staticSystemPrompt });

            // 2. Capa de Memoria: Historial de chat (Crece linealmente)
            if (chatHistory != null)
            {
                requestPayload.Messages.AddRange(chatHistory);
            }

            // 3. Capa Dinámica: Contexto cambiante (Evita romper el caché previo)
            if (!string.IsNullOrWhiteSpace(dynamicSystemContext))
            {
                requestPayload.Messages.Add(new VeniceMessage { Role = "system", Content = dynamicSystemContext });
            }

            // 4. Mensaje del Usuario
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
                
                // Registro de métricas de uso y caché
                if (root.TryGetProperty("usage", out var usage))
                {
                    _logger.Log($"\n[DEBUG CACHE KIMI] Uso de Tokens:\n{usage.GetRawText()}\n", LogLevel.Info);
                }

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var message = choices[0].GetProperty("message");
                    return message.GetProperty("content").GetString()?.Trim() ?? "[Respuesta vacía]";
                }
                
                return "[Formato desconocido]";
            }
            catch (TaskCanceledException)
            {
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