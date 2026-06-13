using System.Collections.Generic;

namespace StardewLivingValley.Models
{
    public class ObservationLog
    {
        public int LastFarmVisitDay { get; set; } = -1;
        public string LastFarmVisitSeason { get; set; } = "";
        public int LastSeenAnimalCount { get; set; } = 0;
        public List<string> LastSeenAnimalTypes { get; set; } = new List<string>();
        public int LastSeenHouseLevel { get; set; } = 0;
        public string LastSeenPetName { get; set; } = "";
        
        public int LastSeenDebrisCount { get; set; } = 0;
        public int LastSeenCropCount { get; set; } = 0;
        public List<string> LastSeenBuildingTypes { get; set; } = new List<string>();

        public int LastHouseVisitDay { get; set; } = -1;
        public string LastHouseVisitSeason { get; set; } = "";
        public int LastSeenChildrenCount { get; set; } = 0;
        public bool LastSeenSpouseRoom { get; set; } = false;
    }
}
