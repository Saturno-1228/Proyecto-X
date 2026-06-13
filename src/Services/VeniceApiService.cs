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

namespace StardewLivingValley.Services
{
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

        public VeniceApiService(string apiKey, IMonitor logger)
        {
            _apiKey = apiKey;
            _logger = logger;
            _httpClient = new HttpClient();
            if (!string.IsNullOrWhiteSpace(_apiKey) && _apiKey != "INGRESA_TU_API_KEY_AQUI")
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }
        }

        public async Task<string> GenerateResponseAsync(
            string staticSystemPrompt,
            string dynamicSystemContext,
            List<VeniceMessage>? chatHistory, 
            string currentUserMessage, 
            string modelName, 
            string? cacheKey,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "INGRESA_TU_API_KEY_AQUI")
            {
                _logger.Log("Error: Venice API Key no está configurada en config.json.", LogLevel.Error);
                return "[Error de configuración: Revisa tu config.json y añade la clave de Venice. La IA no puede hablar.]";
            }

            var requestPayload = new VeniceRequest
            {
                Model = modelName,
                PromptCacheKey = cacheKey,
                MaxTokens = 2000, // Aumentado para soportar reasoning_content sin truncar
                Temperature = 0.7,
                VeniceParameters = new VeniceParameters { IncludeVeniceSystemPrompt = false },
                Reasoning = new ReasoningConfig { Effort = "low" }
            };

            // 1. Capa Estática: Identidad y Reglas
            requestPayload.Messages.Add(new VeniceMessage { Role = "system", Content = staticSystemPrompt });

            // 2. Capa de Memoria: Historial de chat
            if (chatHistory != null)
            {
                requestPayload.Messages.AddRange(chatHistory);
            }

            // 3. Capa Dinámica: Contexto cambiante (evita romper caché de capas anteriores)
            if (!string.IsNullOrWhiteSpace(dynamicSystemContext))
            {
                requestPayload.Messages.Add(new VeniceMessage { Role = "system", Content = dynamicSystemContext });
            }

            // 4. Mensaje del Usuario
            requestPayload.Messages.Add(new VeniceMessage { Role = "user", Content = currentUserMessage });

            try
            {
                string requestJson = JsonSerializer.Serialize(requestPayload, new JsonSerializerOptions { WriteIndented = true });
                _logger.Log($"[Venice API Enviando Peticion]\n{requestJson}", LogLevel.Info);

                var jsonContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(ApiUrl, jsonContent, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.Log($"Venice API Error {response.StatusCode}: {error}", LogLevel.Error);
                    return $"[Error de conexión con la IA HTTP {response.StatusCode}]";
                }

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                
                _logger.Log($"[Venice API Respuesta Recibida]\n{responseString}", LogLevel.Info);

                using JsonDocument doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("usage", out var usage))
                {
                    _logger.Log($"[IA Token Usage] {usage.GetRawText()}", LogLevel.Trace);
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
                return "[Operación Cancelada]";
            }
            catch (Exception ex)
            {
                _logger.Log($"Excepción Venice API: {ex.Message}", LogLevel.Error);
                return "[Error interno en la petición HTTP]";
            }
        }
    }
}
