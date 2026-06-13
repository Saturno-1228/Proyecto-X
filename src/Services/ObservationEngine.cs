using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StardewModdingAPI;
using StardewValley;
using StardewLivingValley.Models;

namespace StardewLivingValley.Services
{
    public class ObservationEngine
    {
        private readonly IMonitor _logger;
        private readonly string _observationDirPath;

        public ObservationEngine(IMonitor logger, string modDirPath)
        {
            _logger = logger;
            _observationDirPath = Path.Combine(modDirPath, "data", "observations");
            Directory.CreateDirectory(_observationDirPath);
        }

        private ObservationLog GetObservationLog(string npcName)
        {
            string path = Path.Combine(_observationDirPath, $"{npcName}_Observation.json");
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<ObservationLog>(json) ?? new ObservationLog();
                }
                catch (Exception e)
                {
                    _logger.Log($"Error leyendo observación de {npcName}: {e.Message}", LogLevel.Error);
                }
            }
            return new ObservationLog();
        }

        private void SaveObservationLog(string npcName, ObservationLog memory)
        {
            string path = Path.Combine(_observationDirPath, $"{npcName}_Observation.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(memory, options));
        }

        // Se llama al interactuar para obtener el contexto
        public string GetObservationContext(NPC npc)
        {
            UpdateObservationLogIfApplicable(npc);
            
            string npcName = npc.Name;
            var memory = GetObservationLog(npcName);
            string context = "";

            // 1. Conocimiento Inmediato (Sentidos en Tiempo Real)
            string immediateContext = GetImmediateSenses(npc);
            if (!string.IsNullOrEmpty(immediateContext))
            {
                context += $"- OBSERVACIÓN EN TIEMPO REAL (Frente a ti): {immediateContext}\n";
            }

            // 2. Conocimiento Sensorial Guardado (Lo que vio en la granja o casa)
            if (memory.LastFarmVisitDay > 0)
            {
                context += $"- Recuerdo visual de su granja (Visto en {memory.LastFarmVisitSeason} día {memory.LastFarmVisitDay}): Viste {memory.LastSeenAnimalCount} animales ({string.Join(", ", memory.LastSeenAnimalTypes.Distinct())}), {memory.LastSeenCropCount} cultivos plantados, y {memory.LastSeenDebrisCount} escombros/maleza. Sus edificios: {string.Join(", ", memory.LastSeenBuildingTypes.Distinct())}. Su mascota: {(string.IsNullOrEmpty(memory.LastSeenPetName) ? "ninguna" : memory.LastSeenPetName)}.\n";
            }
            else
            {
                context += $"- Recuerdo visual de su granja: NUNCA has visitado la granja del jugador.\n";
            }

            if (memory.LastHouseVisitDay > 0)
            {
                context += $"- Recuerdo visual de su casa (Visto en {memory.LastHouseVisitSeason} día {memory.LastHouseVisitDay}): Su casa era nivel {memory.LastSeenHouseLevel}. Tiene {memory.LastSeenChildrenCount} hijos. {(memory.LastSeenSpouseRoom ? "Viste la habitación de un cónyuge instalada." : "")}\n";
            }

            // 3. Conocimiento Profesional (Omnisciencia Laboral)
            string prof = GetProfessionalKnowledge(npcName);
            if (!string.IsNullOrEmpty(prof))
            {
                context += $"- Conocimiento Profesional: {prof}\n";
            }

            if (!string.IsNullOrEmpty(context))
            {
                return "\n--- OBSERVACIONES Y CONOCIMIENTO (LO QUE SABES O HAS VISTO) ---\n" + context + "REGLA DE CONOCIMIENTO: NO sueltes estos datos de forma robótica. Úsalos SOLO si la conversación fluye naturalmente hacia estos temas o si te preguntan, de lo contrario ignóralos.";
            }

            return "";
        }

        private string GetImmediateSenses(NPC npc)
        {
            var p = Game1.player;
            string senses = "";

            // Salud y Energía
            float healthPct = (float)p.health / Math.Max(1, p.maxHealth);
            if (healthPct < 0.3f) senses += "El jugador está gravemente herido y se ve en mal estado físico. ";
            
            float stamPct = p.Stamina / Math.Max(1f, p.MaxStamina);
            if (stamPct < 0.2f) senses += "El jugador se ve exhausto, sudando y a punto de desmayarse de cansancio. ";

            // Objeto en Mano
            if (p.ActiveObject != null)
            {
                string objName = p.ActiveObject.DisplayName;
                senses += $"Lleva en las manos: {objName}. ";
                
                // Olor por objeto
                if (objName.ToLower().Contains("trash") || objName.ToLower().Contains("basura") || p.ActiveObject.Category == StardewValley.Object.FishCategory)
                {
                    senses += "Desprende un olor desagradable por lo que lleva. ";
                }
            }
            else if (p.CurrentTool != null)
            {
                senses += $"Sostiene una herramienta: {p.CurrentTool.BaseName}. ";
            }

            // Olor por locación
            var loc = p.currentLocation;
            if (loc != null && (loc.Name.Contains("Sewer") || loc.Name.Contains("BugLand")))
            {
                senses += "El jugador huele muy mal, como a aguas residuales o humedad estancada. ";
            }

            return senses.Trim();
        }

        private void UpdateObservationLogIfApplicable(NPC npc)
        {
            if (Game1.currentLocation == null) return;

            bool updated = false;
            var memory = GetObservationLog(npc.Name);

            if (Game1.currentLocation.IsFarm)
            {
                var farm = Game1.getFarm();
                
                memory.LastFarmVisitDay = Game1.dayOfMonth;
                memory.LastFarmVisitSeason = Game1.currentSeason;
                
                // Solo animales al aire libre
                var animals = farm.animals.Values.ToList();
                memory.LastSeenAnimalCount = animals.Count;
                memory.LastSeenAnimalTypes.Clear();
                foreach (var a in animals)
                {
                    memory.LastSeenAnimalTypes.Add(a.type.Value);
                }
                
                memory.LastSeenHouseLevel = Game1.player.HouseUpgradeLevel;
                
                // En 1.6 getPet() puede devolver null si no hay mascota o ser modificado. Fallback genérico:
                var characters = farm.characters;
                bool foundPet = false;
                foreach (var character in characters)
                {
                    if (character is StardewValley.Characters.Pet pet)
                    {
                        memory.LastSeenPetName = pet.Name;
                        foundPet = true;
                        break;
                    }
                }
                if (!foundPet) memory.LastSeenPetName = "";

                // Maleza y escombros
                int debris = 0;
                foreach (var tf in farm.terrainFeatures.Values)
                {
                    if (tf is StardewValley.TerrainFeatures.Tree tree && !tree.tapped.Value) debris++;
                    if (tf is StardewValley.TerrainFeatures.Grass) debris++;
                }
                debris += farm.resourceClumps.Count;
                debris += farm.objects.Values.Count(o => o.IsWeeds() || o.Name.Contains("Stone") || o.Name.Contains("Wood"));
                memory.LastSeenDebrisCount = debris;

                // Cultivos
                int crops = 0;
                foreach (var tf in farm.terrainFeatures.Values)
                {
                    if (tf is StardewValley.TerrainFeatures.HoeDirt dirt && dirt.crop != null) crops++;
                }
                memory.LastSeenCropCount = crops;

                // Edificios
                memory.LastSeenBuildingTypes.Clear();
                foreach (var b in farm.buildings)
                {
                    memory.LastSeenBuildingTypes.Add(b.buildingType.Value);
                }

                updated = true;
            }
            else if (Game1.currentLocation is StardewValley.AnimalHouse animalHouse)
            {
                memory.LastFarmVisitDay = Game1.dayOfMonth;
                memory.LastFarmVisitSeason = Game1.currentSeason;
                
                // Solo animales dentro de este edificio
                var animals = animalHouse.animals.Values.ToList();
                memory.LastSeenAnimalCount = animals.Count;
                memory.LastSeenAnimalTypes.Clear();
                foreach (var a in animals)
                {
                    memory.LastSeenAnimalTypes.Add(a.type.Value);
                }
                
                updated = true;
            }
            else if (Game1.currentLocation is StardewValley.Locations.FarmHouse house)
            {
                memory.LastHouseVisitDay = Game1.dayOfMonth;
                memory.LastHouseVisitSeason = Game1.currentSeason;
                memory.LastSeenHouseLevel = house.upgradeLevel;
                memory.LastSeenChildrenCount = house.getChildrenCount();
                
                // Chequear si hay un spouse room (esto se determina si el mapa tiene la room añadida)
                // Usualmente en FarmHouse si el jugador está casado, hay una SpouseRoom.
                memory.LastSeenSpouseRoom = !string.IsNullOrEmpty(Game1.player.spouse);
                
                updated = true;
            }

            if (updated)
            {
                SaveObservationLog(npc.Name, memory);
            }
        }

        private string GetProfessionalKnowledge(string npcName)
        {
            switch (npcName)
            {
                case "Marnie":
                    var farm = Game1.getFarm();
                    int numAnimals = farm.getAllFarmAnimals().Count;
                    return $"Eres la proveedora de animales. Sabes EXACTAMENTE que el jugador tiene {numAnimals} animales ahora mismo en su granja, ya que tienes los registros de ventas y entregas.";
                case "Robin":
                    int houseLvl = Game1.player.HouseUpgradeLevel;
                    return $"Eres la carpintera del pueblo. Sabes que la casa del jugador es Nivel {houseLvl} y tienes todos sus registros de construcción en tu oficina.";
                case "Pierre":
                    return $"Eres el comerciante principal. Sabes que el jugador tiene {Game1.player.Money}g en este momento porque gestionas la economía local.";
                case "Marlon":
                    return $"Gestionas el Gremio de Aventureros. Sabes que el jugador ha llegado al nivel {Game1.player.deepestMineLevel} de las minas de Pelican Town.";
                case "Lewis":
                    return $"Eres el alcalde. Sabes que el jugador ha ganado en total {Game1.player.totalMoneyEarned}g a lo largo de su vida cobrando impuestos y gestionando la caja de envíos.";
                default:
                    return "";
            }
        }
    }
}
