using System.Collections.Generic;

namespace SpawnEntryRepository
{
    public class Map
    {
        public string MapName { get; set; }
        public List<Container> Containers { get; set; }

        public Map(string mapName)
        {
            MapName = mapName;
            Containers = new List<Container>();
        }
    }

    public class Container
    {
        public string ClassName { get; set; }
        public List<SpawnGroup> Groups { get; set; }

        public Container(string className)
        {
            ClassName = className;
            Groups = new List<SpawnGroup>();
        }
    }

    public class SpawnGroup
    {
        public decimal Weight { get; set; }
        public List<Creature> Creatures { get; set; }

        public SpawnGroup(decimal weight)
        {
            Weight = weight;
            Creatures = new List<Creature>();
        }
    }

    public class Creature
    {
        public string CreatureName { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public List<decimal> Percentages { get; set; }

        public Creature(string creatureName)
        {
            CreatureName = creatureName;
            Percentages = new List<decimal>();
        }
    }
}
