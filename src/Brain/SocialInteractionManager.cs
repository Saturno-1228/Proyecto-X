using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewLivingValley.Models;
using StardewLivingValley.Configuration;

namespace StardewLivingValley.Brain
{
    public class SocialInteractionManager
    {
        private readonly IModHelper _helper;
        private readonly IMonitor _monitor;
        private readonly NeuralLink _apiClient;
        private readonly Subconscious _subconscious;
        private readonly ConversationPlaybackManager _playbackManager;
        private readonly Hippocampus _hippocampus;
        private readonly KnowledgeCortex _knowledgeCortex;
        private readonly SensoryCortex _sensoryCortex;
        private readonly PairEmotionService _pairEmotionService;
        private readonly ModConfig _config;

        // Guarda el momento en que dos NPCs hablaron por Ãºltima vez
        private readonly Dictionary<string, int> _lastInteractionTimes = new Dictionary<string, int>();
        private readonly HashSet<string> _busyNpcs = new HashSet<string>();

        public SocialInteractionManager(
            IModHelper helper, 
            IMonitor monitor, 
            NeuralLink apiClient, 
            Subconscious subconscious,
            ConversationPlaybackManager playbackManager,
            Hippocampus hippocampus,
            KnowledgeCortex knowledgeCortex,
            SensoryCortex sensoryCortex,
            PairEmotionService pairEmotionService,
            ModConfig config)
        {
            _helper = helper;
            _monitor = monitor;
            _apiClient = apiClient;
            _subconscious = subconscious;
            _playbackManager = playbackManager;
            _hippocampus = hippocampus;
            _knowledgeCortex = knowledgeCortex;
            _sensoryCortex = sensoryCortex;
            _pairEmotionService = pairEmotionService;
            _config = config;

            _helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
        }

        private void OnOneSecondUpdateTicked(object sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.player?.currentLocation == null) return;

            var location = Game1.player.currentLocation;
            var characters = location.characters.ToList();

            for (int i = 0; i < characters.Count; i++)
            {
                for (int j = i + 1; j < characters.Count; j++)
                {
                    var npcA = characters[i];
                    var npcB = characters[j];

                    if (npcA == null || npcB == null || !npcA.IsVillager || !npcB.IsVillager) continue;
                    
                    // Evitar que hablen si ya estÃ¡n procesando una conversaciÃ³n o reproduciÃ©ndola
                    if (_busyNpcs.Contains(npcA.Name) || _busyNpcs.Contains(npcB.Name)) continue;
                    if (_playbackManager.IsNpcBusy(npcA.Name) || _playbackManager.IsNpcBusy(npcB.Name)) continue;

                    float distance = Vector2.Distance(npcA.Tile, npcB.Tile);
                    if (distance <= 3.0f)
                    {
                        string pairKey = GetPairKey(npcA.Name, npcB.Name);

                        // Cooldown: No charlan de nuevo si ya charlaron hace menos de 2 horas in-game
                        int currentTime = (int)(Game1.stats.DaysPlayed * 2400) + Game1.timeOfDay;
                        if (_lastInteractionTimes.TryGetValue(pairKey, out int lastTime))
                        {
                            if (currentTime - lastTime < 200) continue; 
                        }

                        // Scoring contextual dinÃ¡mico en lugar de probabilidad fija
                        float encounterScore = ScoreEncounter(npcA, npcB);
                        if (Game1.random.NextDouble() < encounterScore)
                        {
                            _monitor.Log($"[Social] Encounter score {npcA.Name}-{npcB.Name}: {encounterScore:F2} â†’ Â¡Activado!", LogLevel.Trace);
                            _lastInteractionTimes[pairKey] = currentTime;
                            _busyNpcs.Add(npcA.Name);
                            _busyNpcs.Add(npcB.Name);
                            _ = TriggerSocialInteractionAsync(npcA, npcB);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Calcula una probabilidad dinÃ¡mica de que dos NPCs inicien una charla,
        /// basada en sus emociones, el entorno y la distancia.
        /// Rango de retorno: [0.02, 0.40]
        /// </summary>
        private float ScoreEncounter(NPC npcA, NPC npcB)
        {
            var emotion = _pairEmotionService.GetOrCreate(npcA.Name, npcB.Name);

            float score = 0.05f; // Base: 5%

            // Bonificadores por emociones positivas
            score += emotion.Friendship / 500f;     // Max +0.20
            score += emotion.Familiarity / 400f;     // Max +0.25

            // PenalizaciÃ³n por incomodidad
            score -= emotion.Awkwardness / 300f;     // Max -0.33

            // Bonus por enojo alto (quieren confrontar)
            if (emotion.Anger >= 30)
                score += 0.10f;

            // Bonus por entorno
            string locationName = npcA.currentLocation?.NameOrUniqueName ?? "";
            if (locationName.Contains("Saloon", StringComparison.OrdinalIgnoreCase))
            {
                score += 0.06f;
            }
            else if (!npcA.currentLocation?.IsOutdoors ?? false)
            {
                // Interior genÃ©rico (tienda, casa, etc.)
                score += 0.12f;
            }
            else
            {
                // Exterior: menor probabilidad
                score += 0.03f;
            }

            // Proximidad extra (mÃ¡s cerca = mÃ¡s probable)
            float distance = Vector2.Distance(npcA.Tile, npcB.Tile);
            score += Math.Max(0f, (3f - distance) / 20f);

            return Math.Clamp(score, 0.02f, 0.40f);
        }

        /// <summary>
        /// Calcula cuÃ¡ntas lÃ­neas de diÃ¡logo deberÃ­a tener la charla.
        /// Rango: [1, 6]
        /// </summary>
        private int CalculateDialogueBudget(NPC npcA, NPC npcB)
        {
            var emotion = _pairEmotionService.GetOrCreate(npcA.Name, npcB.Name);

            int budget = 3; // Base

            if (emotion.Friendship + emotion.Trust >= 80)
                budget += 1;

            if (!(npcA.currentLocation?.IsOutdoors ?? true))
                budget += 1; // Interior = mÃ¡s tiempo para charlar

            if (emotion.Awkwardness >= 30)
                budget -= 1;

            if (emotion.Anger >= 40)
                budget -= 1; // Charla tensa y cortante

            if (emotion.Familiarity >= 60)
                budget += 1; // Se conocen bien, hablan mÃ¡s

            return Math.Clamp(budget, 1, 6);
        }

        private async Task TriggerSocialInteractionAsync(NPC npcA, NPC npcB)
        {
            var emotion = _pairEmotionService.GetOrCreate(npcA.Name, npcB.Name);
            float score = ScoreEncounter(npcA, npcB);
            int budget = CalculateDialogueBudget(npcA, npcB);
            string location = npcA.currentLocation?.NameOrUniqueName ?? "???";
            string familyTie = _pairEmotionService.GetFamilyTie(npcA.Name, npcB.Name);

            _monitor.Log($"\nâ•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•", LogLevel.Info);
            _monitor.Log($"â•‘ [Social Iterativo] {npcA.Name} â†” {npcB.Name}  |  {location}  |  {Game1.timeOfDay}", LogLevel.Info);
            _monitor.Log($"â•‘ RelaciÃ³n: {familyTie}", LogLevel.Info);
            _monitor.Log($"â•‘ Emociones: F:{emotion.Friendship} T:{emotion.Trust} A:{emotion.Anger} Awk:{emotion.Awkwardness} Fam:{emotion.Familiarity}", LogLevel.Info);
            _monitor.Log($"â•‘ Score: {score:F2} | Budget: {budget} lÃ­neas", LogLevel.Info);
            _monitor.Log($"â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•", LogLevel.Info);

            List<string> conversationHistory = new List<string>();
            List<string> memoriesLearnedA = new List<string>();
            List<string> memoriesLearnedB = new List<string>();

            try
            {
                // NO congelamos su movimiento ni forzamos que se miren fijamente para no interrumpir sus actividades
                npcA.doEmote(8); // '...' bubble

                var profileA = _knowledgeCortex.GetDynamicProfile(npcA.Name, Game1.player.getFriendshipHeartLevelForNPC(npcA.Name));
                var profileB = _knowledgeCortex.GetDynamicProfile(npcB.Name, Game1.player.getFriendshipHeartLevelForNPC(npcB.Name));
                
                string sensoryA = _sensoryCortex.GetObservationContext(npcA, false);
                string sensoryB = _sensoryCortex.GetObservationContext(npcB, false);

                string scheduleA = GetNpcScheduleString(npcA);
                string scheduleB = GetNpcScheduleString(npcB);

                EnvironmentState envState = new EnvironmentState
                {
                    Weather = Game1.isRaining ? "Lluvioso" : "Despejado",
                    TimeOfDay = FormatStardewTime(Game1.timeOfDay),
                    CurrentLocation = location
                };

                // Ping-Pong Loop
                for (int turn = 0; turn < budget; turn++)
                {
                    bool isTurnA = (turn % 2 == 0);
                    NPC currentSpeaker = isTurnA ? npcA : npcB;
                    NPC targetNpc = isTurnA ? npcB : npcA;
                    NpcKnowledgeProfile currentProfile = isTurnA ? profileA : profileB;
                    NpcKnowledgeProfile targetProfile = isTurnA ? profileB : profileA;
                    string currentSensory = isTurnA ? sensoryA : sensoryB;
                    envState.DailySchedule = isTurnA ? scheduleA : scheduleB;
                    
                    string staticPrompt = _subconscious.BuildStaticSystemPrompt(currentProfile ?? new NpcKnowledgeProfile { Role = $"Eres {currentSpeaker.Name}" });
                    string dynamicContext = _subconscious.BuildDynamicSystemContextForNpc(
                        envState, 
                        targetNpc.Name, 
                        targetProfile, 
                        familyTie, 
                        _pairEmotionService.GetOrCreate(currentSpeaker.Name, targetNpc.Name), 
                        currentSensory, 
                        conversationHistory.ToArray()
                    );

                    var messages = new List<VeniceMessage>
                    {
                        new VeniceMessage { Role = "system", Content = staticPrompt },
                        new VeniceMessage { Role = "system", Content = dynamicContext }
                    };

                    // Llamada a la API
                    var responseJson = await _apiClient.SendRawRequestAsync(messages, _config.ChatModel, System.Threading.CancellationToken.None);

                    var match = Regex.Match(responseJson, @"\{.*\}", RegexOptions.Singleline);
                    if (match.Success) responseJson = match.Value;
                    else responseJson = responseJson.Replace("```json", "").Replace("```", "").Trim();

                    var result = JsonSerializer.Deserialize<NpcTurnResult>(responseJson);

                    if (result != null && !string.IsNullOrWhiteSpace(result.Response))
                    {
                        string cleanResponse = Regex.Replace(result.Response, @"\[\d+\]", "").Trim();
                        conversationHistory.Add($"{currentSpeaker.Name}: {cleanResponse}");
                        
                        _monitor.Log($"â•‘   {currentSpeaker.Name}: \"{cleanResponse}\"", LogLevel.Info);

                        currentSpeaker.showTextAboveHead(cleanResponse);

                        if (result.MemoriesLearned != null && result.MemoriesLearned.Count > 0)
                        {
                            if (isTurnA) memoriesLearnedA.AddRange(result.MemoriesLearned);
                            else memoriesLearnedB.AddRange(result.MemoriesLearned);
                        }

                        if (result.EmotionDeltas != null)
                        {
                            _pairEmotionService.AdjustAxis(currentSpeaker.Name, targetNpc.Name, "friendship", result.EmotionDeltas.Friendship);
                            _pairEmotionService.AdjustAxis(currentSpeaker.Name, targetNpc.Name, "trust", result.EmotionDeltas.Trust);
                            _pairEmotionService.AdjustAxis(currentSpeaker.Name, targetNpc.Name, "anger", result.EmotionDeltas.Anger);
                            _pairEmotionService.AdjustAxis(currentSpeaker.Name, targetNpc.Name, "awkwardness", result.EmotionDeltas.Awkwardness);
                        }

                        // Esperar tiempo de lectura para que se vea natural en el juego
                        int waitMs = 1000 + (cleanResponse.Length * 50);
                        waitMs = Math.Min(waitMs, 4000); // Max 4 segundos de pausa real por mensaje
                        await Task.Delay(waitMs);
                    }
                    else
                    {
                        _monitor.Log($"â•‘ âš  {currentSpeaker.Name} devolviÃ³ un turno invÃ¡lido. Fin anticipado.", LogLevel.Warn);
                        break;
                    }
                }

                _monitor.Log($"â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•", LogLevel.Info);
                
                // Guardar las memorias aprendidas al final
                foreach (var m in memoriesLearnedA)
                {
                    _hippocampus.SaveNpcMemory(npcA.Name, $"[Charla con {npcB.Name}] {m}");
                    _monitor.Log($"â•‘ Memoria {npcA.Name} â†’ {m}", LogLevel.Info);
                }
                foreach (var m in memoriesLearnedB)
                {
                    _hippocampus.SaveNpcMemory(npcB.Name, $"[Charla con {npcA.Name}] {m}");
                    _monitor.Log($"║ Memoria {npcB.Name} → {m}", LogLevel.Info);
                }

                _monitor.Log($"╚══════════════════════════════════════════════════════════════\n", LogLevel.Info);
            }
            catch (Exception ex)
            {
                _monitor.Log($"ERROR: {ex.Message}", LogLevel.Error);
                _monitor.Log($"--------------------------------------------------\n", LogLevel.Error);
            }
            finally
            {
                _busyNpcs.Remove(npcA.Name);
                _busyNpcs.Remove(npcB.Name);
            }
        }

        private string GetPairKey(string nameA, string nameB)
        {
            var list = new List<string> { nameA, nameB };
            list.Sort();
            return string.Join("-", list);
        }

        private string GetNpcScheduleString(NPC npc)
        {
            if (npc.Schedule == null || npc.Schedule.Count == 0) return "";
            var sbSchedule = new System.Text.StringBuilder();
            foreach (var key in System.Linq.Enumerable.OrderBy(npc.Schedule.Keys, k => k))
            {
                var pathDesc = npc.Schedule[key];
                string behavior = !string.IsNullOrEmpty(pathDesc.endOfRouteBehavior) ? $" (Haciendo: {pathDesc.endOfRouteBehavior})" : "";
                string target = !string.IsNullOrEmpty(pathDesc.targetLocationName) ? pathDesc.targetLocationName : "Otro lugar";
                sbSchedule.AppendLine($"- A las {FormatStardewTime(key)}: Ir a {target}{behavior}");
            }
            return sbSchedule.ToString();
        }

        private string FormatStardewTime(int time)
        {
            int hours = time / 100;
            int minutes = time % 100;
            string amPm = hours < 12 || hours >= 24 ? "AM" : "PM";
            int displayHours = hours % 12;
            if (displayHours == 0) displayHours = 12;
            return $"{displayHours}:{minutes:D2} {amPm}";
        }
    }
}
