using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StardewModdingAPI;
using StardewValley;
using StardewLivingValley.Models;

namespace StardewLivingValley.Brain
{
    /// <summary>
    /// Gestiona las emociones pareadas entre NPCs.
    /// Inspirado en el PairEmotionService de StardewLivingRPG, pero adaptado
    /// para funcionar como contexto de entrada y salida del LLM.
    /// </summary>
    public class PairEmotionService
    {
        private readonly IMonitor _logger;
        private readonly string _dataFilePath;
        private Dictionary<string, NpcPairEmotion> _emotions = new Dictionary<string, NpcPairEmotion>(StringComparer.OrdinalIgnoreCase);

        // Límites de seguridad para evitar que la IA infle valores
        private const int MaxDeltaPerCommand = 5;
        private const int MaxDeltaPerDay = 10;
        private readonly Dictionary<string, int> _dailyDeltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public PairEmotionService(IMonitor logger, string modDirectoryPath)
        {
            _logger = logger;
            _dataFilePath = Path.Combine(modDirectoryPath, "data", "pair_emotions.json");
            Load();
        }

        /// <summary>
        /// Construye una clave ordenada alfabéticamente para el par de NPCs.
        /// Ej: ("Haley", "Emily") → "emily|haley"
        /// </summary>
        public static string BuildPairKey(string npcA, string npcB)
        {
            string a = (npcA ?? "").Trim().ToLowerInvariant();
            string b = (npcB ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return "";
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0
                ? $"{a}|{b}"
                : $"{b}|{a}";
        }

        /// <summary>
        /// Obtiene o crea la entrada emocional para un par de NPCs.
        /// Si no existe, se crea con valores neutros (Friendship: 50, Trust: 50).
        /// </summary>
        public NpcPairEmotion GetOrCreate(string npcA, string npcB)
        {
            string key = BuildPairKey(npcA, npcB);
            if (string.IsNullOrWhiteSpace(key)) return new NpcPairEmotion();

            if (!_emotions.TryGetValue(key, out var entry))
            {
                entry = new NpcPairEmotion();
                _emotions[key] = entry;
            }
            return entry;
        }

        /// <summary>
        /// Ajusta un eje emocional con protecciones de seguridad.
        /// Clampea el delta a [-5, +5] por comando y el total diario a [-10, +10].
        /// </summary>
        public bool AdjustAxis(string npcA, string npcB, string axis, int delta)
        {
            if (delta == 0) return true;

            // Clampear el delta individual
            delta = Math.Clamp(delta, -MaxDeltaPerCommand, MaxDeltaPerCommand);

            string key = BuildPairKey(npcA, npcB);
            if (string.IsNullOrWhiteSpace(key)) return false;

            // Verificar tope diario
            string dailyKey = $"{key}:{axis}";
            _dailyDeltas.TryGetValue(dailyKey, out int accumulatedToday);
            if (Math.Abs(accumulatedToday + delta) > MaxDeltaPerDay)
            {
                _logger.Log($"[PairEmotion] Tope diario alcanzado para {key} eje {axis}. Ignorando delta {delta}.", LogLevel.Trace);
                return false;
            }

            var entry = GetOrCreate(npcA, npcB);

            switch (axis.ToLowerInvariant())
            {
                case "friendship":
                    entry.Friendship = Math.Clamp(entry.Friendship + delta, 0, 100);
                    break;
                case "trust":
                    entry.Trust = Math.Clamp(entry.Trust + delta, 0, 100);
                    break;
                case "anger":
                    entry.Anger = Math.Clamp(entry.Anger + delta, 0, 100);
                    break;
                case "awkwardness":
                    entry.Awkwardness = Math.Clamp(entry.Awkwardness + delta, 0, 100);
                    break;
                default:
                    _logger.Log($"[PairEmotion] Eje desconocido: {axis}", LogLevel.Warn);
                    return false;
            }

            // Incrementar familiaridad en +1 por cada interacción (independiente del eje)
            entry.Familiarity = Math.Clamp(entry.Familiarity + 1, 0, 100);
            entry.LastInteractionDay = (int)Game1.stats.DaysPlayed;

            _dailyDeltas[dailyKey] = accumulatedToday + delta;

            _logger.Log($"[PairEmotion] {key} → {axis} {(delta > 0 ? "+" : "")}{delta} (ahora: F:{entry.Friendship} T:{entry.Trust} A:{entry.Anger} Awk:{entry.Awkwardness} Fam:{entry.Familiarity})", LogLevel.Trace);
            return true;
        }

        /// <summary>
        /// Decay diario: reduce emociones negativas y fortalece la confianza de parejas familiares.
        /// Se llama al inicio de cada día desde ModEntry.
        /// </summary>
        public void Decay()
        {
            _dailyDeltas.Clear(); // Resetear topes diarios

            foreach (var kvp in _emotions)
            {
                var e = kvp.Value;

                // Emociones negativas se desvanecen con el tiempo
                e.Anger = Math.Max(0, e.Anger - 2);
                e.Awkwardness = Math.Max(0, e.Awkwardness - 2);

                // Si son familiares (Familiarity > 30), la confianza crece gradualmente
                if (e.Familiarity > 30)
                {
                    e.Trust = Math.Min(100, e.Trust + 1);
                }

                // Limpiar entradas completamente neutrales que no se han usado en 28+ días
                // (Se hace en Save para no modificar la colección durante iteración)
            }

            _logger.Log($"[PairEmotion] Decay diario aplicado a {_emotions.Count} pares.", LogLevel.Trace);
        }

        /// <summary>
        /// Genera un resumen legible para inyectar en el prompt del LLM.
        /// </summary>
        public string GetEmotionSummary(string npcA, string npcB)
        {
            var e = GetOrCreate(npcA, npcB);
            string summary = $"Amistad: {e.Friendship}/100 | Confianza: {e.Trust}/100 | Enojo: {e.Anger}/100 | Incomodidad: {e.Awkwardness}/100 | Familiaridad: {e.Familiarity}/100";
            
            if (!string.IsNullOrWhiteSpace(e.FamilyTie))
            {
                summary = $"Vínculo Familiar: {e.FamilyTie}\n{summary}";
            }

            return summary;
        }

        /// <summary>
        /// Devuelve el vínculo familiar como string (para el prompt, reemplaza el antiguo GetRelationship).
        /// </summary>
        public string GetFamilyTie(string npcA, string npcB)
        {
            var e = GetOrCreate(npcA, npcB);
            return string.IsNullOrWhiteSpace(e.FamilyTie) ? "Vecinos (Conocidos de la ciudad)" : e.FamilyTie;
        }

        // --- Persistencia ---

        public void Load()
        {
            if (!File.Exists(_dataFilePath))
            {
                _logger.Log("[PairEmotion] No se encontró pair_emotions.json. Se usarán valores por defecto.", LogLevel.Info);
                return;
            }

            try
            {
                string json = File.ReadAllText(_dataFilePath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, NpcPairEmotion>>(json);
                if (loaded != null)
                {
                    _emotions = new Dictionary<string, NpcPairEmotion>(loaded, StringComparer.OrdinalIgnoreCase);
                    _logger.Log($"[PairEmotion] Cargadas {_emotions.Count} relaciones emocionales.", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"[PairEmotion] Error cargando pair_emotions.json: {ex.Message}", LogLevel.Error);
            }
        }

        public void Save()
        {
            try
            {
                // Limpiar entradas muertas (neutrales y sin interacción en 28+ días)
                int currentDay = (int)Game1.stats.DaysPlayed;
                var keysToRemove = _emotions
                    .Where(kvp =>
                        string.IsNullOrWhiteSpace(kvp.Value.FamilyTie) &&
                        kvp.Value.Friendship == 50 && kvp.Value.Trust == 50 &&
                        kvp.Value.Anger == 0 && kvp.Value.Awkwardness == 0 &&
                        kvp.Value.Familiarity == 0 &&
                        currentDay - kvp.Value.LastInteractionDay > 28)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _emotions.Remove(key);
                }

                string dir = Path.GetDirectoryName(_dataFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_dataFilePath, JsonSerializer.Serialize(_emotions, options));

                _logger.Log($"[PairEmotion] Guardadas {_emotions.Count} relaciones emocionales.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                _logger.Log($"[PairEmotion] Error guardando pair_emotions.json: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
