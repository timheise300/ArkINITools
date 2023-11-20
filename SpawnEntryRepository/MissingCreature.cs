using System.Collections.Generic;

namespace SpawnEntryRepository
{
    public class MissingCreature
    {
        public string CreatureName { get; set; }
        public List<MapLocations> MapLocations { get; set; }
    }

    public class MapLocations
    {
        public string MapName { get; set; }
        public List<string> Locations { get; set; }
    }
}
