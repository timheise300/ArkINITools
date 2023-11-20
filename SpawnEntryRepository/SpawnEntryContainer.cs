using System.Collections.Generic;

namespace SpawnEntryRepository
{
    public class SpawnEntryContainer
    {
        public string ContainerName { get; set; }
        public List<SpawnEntry> SpawnEntries { get; set; }
        public List<SpawnLimit> SpawnLimits { get; set; }
    }

    public class SpawnEntry
    {
        public string EntryName { get; set; }
        public decimal EntryWeight { get; set; }
        public List<string> SpawnStrings { get; set; }
        public decimal SpawnRadius { get; set; }
        public List<decimal> SpawnPercentChance { get; set; }
        public List<Coords> SpawnOffsets { get; set; }
        public string Colorsets { get; set; }
    }

    public class SpawnLimit
    {
        public string ClassString { get; set; }
        public decimal PercentToAllow { get; set; }
    }

    public class Coords
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }
}
