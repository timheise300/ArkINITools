namespace SpawnEntryRepository
{
    public class CreatureSpawnInfo
    {
        public string CreatureName { get; set; }
        public string NameTag { get; set; }
        public string CreatureID { get; set; }
        public decimal MaxPercentage { get; set; }

        public CreatureSpawnInfo(string creatureName, string nameTag, string creatureID, decimal maxPercentage)
        {
            CreatureName = creatureName;
            NameTag = nameTag;
            CreatureID = creatureID;
            MaxPercentage = maxPercentage;
        }
    }
}
