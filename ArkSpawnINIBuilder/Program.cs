using SpawnEntryRepository;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArkSpawnINIBuilder
{
    class Program
    {
        static void Main()
        {
            string directory = AppContext.BaseDirectory;
            if (!File.Exists($@"{directory}\SpawnEntries.json"))
                BuildMapJsonList();

            if (!File.Exists($@"{directory}\CreatureIDs.json"))
                BuildCreatureIDsJsonList();

            string jsonDirectory = File.ReadAllText($@"{directory}\SpawnEntries.json");
            List<Map> maps = JsonSerializer.Deserialize<List<Map>>(jsonDirectory);

            Console.WriteLine("Ark Spawn Config Generator");
            Console.WriteLine();

            Map map = null;

            while (map is null)
            {
                Console.WriteLine("Please Enter a map name from below:");
                maps.ForEach(map => Console.WriteLine(map.MapName));
                string mapName = Console.ReadLine();
                map = maps.FirstOrDefault(map => map.MapName.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    Console.WriteLine("invalid map selection, try again");
            }

            Console.WriteLine();
            BuildSpawnConfigs(map);

            Console.WriteLine("Finished writing spawn configs, press any key to close");
            Console.Read();
        }

        private static void BuildCreatureIDsJsonList()
        {
            string directory = AppContext.BaseDirectory;
            string entriesTextFile = $@"{directory}\CreatureIDs.txt";
            List<CreatureSpawnInfo> mapList = GetCreatureIDsList(entriesTextFile);
            WriteItemListToFiles(mapList, "CreatureIDs");
        }

        private static void BuildSpawnConfigs(Map map)
        {
            Console.WriteLine("Use The Hunted Mod? Y/N");
            bool useHunted = Console.ReadLine().Equals("Y", StringComparison.OrdinalIgnoreCase);
            //Console.WriteLine("Override basic dinos? Y/N");
            //bool overrideBasic = Console.ReadLine().Equals("Y", StringComparison.OrdinalIgnoreCase);
            List<Creature> mapCreatures = map.Containers
                .SelectMany(container => container.Groups
                    .SelectMany(group => group.Creatures))
                .Distinct()
                .ToList();
            List<CreatureSpawnInfo> allCreatures = GetAllCreatureSpawnInfo();

            List<MissingCreature> allMissingCreatures = MissingCreatureInfo();
            string spawnconfigPath = $@"C:\Users\timhe\OneDrive\Documents\Ark Server Backup\GeneratedSpawnConfigs\{map.MapName.Replace(" ", "")}SpawnConfig.INI";

            if (File.Exists(spawnconfigPath))
                File.Delete(spawnconfigPath);

            List<CreatureSpawnInfo> missingCreatures = allCreatures
                .Where(creature => !mapCreatures.Any(mapCreature => creature.CreatureName.Equals(mapCreature.CreatureName)))
                .Where(creature => allMissingCreatures.Any(missing =>
                    missing.MapLocations.Any(mapLocation => mapLocation.MapName.Equals(map.MapName)) &&
                    creature.CreatureName.Equals(missing.CreatureName)))
                .ToList();

            List<CreatureSpawnInfo> creatureInfos = GetAllCreatureSpawnInfo();
            creatureInfos = FilterProblemAndBasicDinos(creatureInfos);

            //List<CreatureSpawnInfo> fjordurOverrides = new();
            //if (map.MapName.Equals("Fjordur") && overrideBasic)
            //    fjordurOverrides = GetFjordurOverrides(allCreatures);

            foreach (Container container in map.Containers)
            {
                List<string> spawnEntries = new();
                List<string> spawnLimits = new();

                foreach (SpawnGroup group in container.Groups)
                {
                    //if (!group.Creatures.Any(creature => fjordurOverrides.Any(fo => fo.CreatureName.Equals(creature.CreatureName))))
                    //    continue;

                    List<string> groupCreatures = group.Creatures.Select(creature => creature.CreatureName).ToList();
                    List<CreatureSpawnInfo> groupCreatureSpawnInfo = GroupCreatures(creatureInfos, groupCreatures);

                    List<List<CreatureSpawnInfo>> allGroups = groupCreatures.Count > 1 ?
                        GetMobVariantGroups(groupCreatureSpawnInfo) : GetVariantGroups(groupCreatureSpawnInfo);

                    int uniqueNameTags = allGroups.SelectMany(group =>
                            group.Select(creature => creature.NameTag))
                        .Distinct()
                        .Count();
                    foreach (List<CreatureSpawnInfo> uniqueGroupCreatures in allGroups)
                    {
                        bool containsOG = ContainsOriginalCreature(groupCreatures, uniqueGroupCreatures);
                        uniqueGroupCreatures.ForEach(creature => {
                            creature.MaxPercentage = !useHunted || containsOG ? creature.MaxPercentage : 0.01m;
                            creature.MaxPercentage = container.ClassName.Contains(creature.CreatureName.Replace(" ", "")) ? 1m : creature.MaxPercentage;
                        });
                        decimal weightOverrride = useHunted ? 0.001m : Math.Round(group.Weight / allGroups.Count, 3);
                        group.Weight = (containsOG) ? group.Weight : weightOverrride;
                        string spawnEntry = BuildSpawnEntryString(group, container.ClassName, uniqueGroupCreatures);

                        if (spawnEntries.Contains(spawnEntry)) continue;

                        spawnEntries.Add(spawnEntry);
                        spawnLimits.AddRange(GetSpawnLimits(uniqueGroupCreatures));
                    }
                }

                List<CreatureSpawnInfo> extraCreatures = GetExtraCreatures(map.MapName, container.ClassName);
                foreach (CreatureSpawnInfo extraCreature in extraCreatures)
                {
                    spawnEntries.Add(GetExtraCreatureSpawnEntry(extraCreature, container.ClassName));
                    spawnLimits.Add(GetSpawnLimit(extraCreature));
                }

                foreach (CreatureSpawnInfo missingCreature in missingCreatures)
                {
                    spawnEntries.Add(GetMissingCreatureSpawnEntry(missingCreature, container.ClassName, map.MapName));
                    spawnLimits.AddRange(GetMissingCreatureSpawnLimit(missingCreature, container.ClassName, map.MapName));
                }

                if (string.IsNullOrEmpty(string.Join("", spawnEntries)))
                    continue;

                string spawnEntriesString = string.Join(",", spawnEntries.Where(entry => !string.IsNullOrEmpty(entry)));
                string spawnLimitsString = string.Join(",", spawnLimits.Where(limit => !string.IsNullOrEmpty(limit)));
                string configID = useHunted ? "ConfigAddNPCSpawnEntriesContainer" : "ConfigOverrideNPCSpawnEntriesContainer";
                string containerString = $@"{configID}=(NPCSpawnEntriesContainerClassString=""{container.ClassName}"",NPCSpawnEntries=({spawnEntriesString}),NPCSpawnLimits=({spawnLimitsString})){Environment.NewLine}";

                File.AppendAllText(spawnconfigPath, containerString);
            }
        }

        private static List<CreatureSpawnInfo> GetFjordurOverrides(List<CreatureSpawnInfo> allCreatures)
        {
            List<CreatureSpawnInfo> overrides = allCreatures.Where(creature => 
                    creature.CreatureName.Equals("Deinonychus") ||
                    creature.CreatureName.Equals("Gasbags") ||
                    creature.CreatureName.Equals("Rock Drake"))
                .ToList();

            return overrides;
        }

        private static bool ContainsOriginalCreature(List<string> groupCreatures, List<CreatureSpawnInfo> uniqueGroupCreatures)
        {
            return groupCreatures.Any(gCreature => uniqueGroupCreatures.Any(ugCreature => ugCreature.CreatureName.Equals(gCreature)));
        }

        private static List<List<CreatureSpawnInfo>> GetVariantGroups(List<CreatureSpawnInfo> groupCreatureSpawnInfo)
        {
            IEnumerable<string> uniqueDinoTags = groupCreatureSpawnInfo.Select(dino => dino.NameTag).Distinct();
            if (uniqueDinoTags.Count() > 1 || (uniqueDinoTags.Contains("Ant") || (uniqueDinoTags.Contains("Leech"))))
                return new List<List<CreatureSpawnInfo>>() { groupCreatureSpawnInfo };

            List<List<CreatureSpawnInfo>> variantGroupCreatureSpawnInfo = groupCreatureSpawnInfo.GroupBy(info => info.CreatureID)
                .Select(g => g.Select(cInfo => cInfo).ToList()).ToList();

            return variantGroupCreatureSpawnInfo;
        }

        private static List<List<CreatureSpawnInfo>> GetMobVariantGroups(List<CreatureSpawnInfo> groupCreatureSpawnInfo)
        {
            CreatureSpawnInfo defaultBoss = groupCreatureSpawnInfo.FirstOrDefault();
            List<List<CreatureSpawnInfo>> variantGroupCreatureSpawnInfo = new();
            List<CreatureSpawnInfo> bosses = groupCreatureSpawnInfo
                .Where(dino => dino.NameTag.Equals("Yutyrannus") || dino.NameTag.Equals("Basilosaurus"))
                .DefaultIfEmpty(defaultBoss)
                .ToList();
            List<CreatureSpawnInfo> minions = groupCreatureSpawnInfo
                .Where(creature => !bosses.Contains(creature))
                .ToList();
            minions.RemoveAll(dino => dino.CreatureID.Equals("Carno_Character_BP_Aberrant_C"));

            foreach (CreatureSpawnInfo creature in bosses)
            {
                List<CreatureSpawnInfo> newList = new()
                {
                    creature
                };

                CreatureSpawnInfo selectedMinion = creature.CreatureID.StartsWith("DA_") ?
                    minions.FirstOrDefault(dino => dino.CreatureID.StartsWith("DA_")) :
                    minions.FirstOrDefault(dino => !dino.CreatureID.StartsWith("DA_"));

                newList.Add(selectedMinion);
                variantGroupCreatureSpawnInfo.Add(newList);
            }

            return variantGroupCreatureSpawnInfo;
        }

        private static string GetSpawnLimit(CreatureSpawnInfo creatureInfo)
        {
            return $@"(NPCClassString=""{creatureInfo.CreatureID}"",MaxPercentageOfDesiredNumToAllow={creatureInfo.MaxPercentage})";
        }

        private static IEnumerable<string> GetSpawnLimits(List<CreatureSpawnInfo> creatureInfos)
        {
            List<string> spawnLimits = new();

            foreach (CreatureSpawnInfo creature in creatureInfos)
            {
                if (!spawnLimits.Any(limit => limit.Contains(creature.CreatureID)))
                {
                    decimal maxPercent = creature.MaxPercentage.Equals(1m) ? 1m : creature.MaxPercentage / 10;
                    maxPercent = Math.Max(maxPercent, 0.001m);
                    string spawnLimit = $@"(NPCClassString=""{creature.CreatureID}"",MaxPercentageOfDesiredNumToAllow={maxPercent})";
                    spawnLimits.Add(spawnLimit);
                }
            }

            return spawnLimits;
        }

        private static string BuildSpawnEntryString(SpawnGroup group, string containerClassName, List<CreatureSpawnInfo> creatureSpawnInfos, bool isMissing = false)
        {
            string creatureName = string.Join("", creatureSpawnInfos.Select(info => info.CreatureName));
            creatureName += (group.Creatures.Count > 1 || group.Creatures.Any(creature => creature.Percentages.Count > 1)) ? "Mob" : string.Empty;
            string entryName = GetEntryName(containerClassName, creatureName);

            List<string> idsList = group.Creatures.SelectMany(creature =>
                creature.Percentages.SelectMany(per => creatureSpawnInfos
                .Where(info => BaseDinoName(info.CreatureName).Equals(BaseDinoName(creature.CreatureName), StringComparison.OrdinalIgnoreCase))
                .Select(info => info.CreatureID)))
                .ToList();

            string percentList = string.Empty;
            string offsets = string.Empty;
            if (idsList.Count > 1)
            {
                List<decimal> fullPercentList = group.Creatures
                    .SelectMany(creature => creature.Percentages.Select(per => per)).ToList();
                percentList = GetPercentageList(fullPercentList);
                offsets = GetSpawnOffsets(idsList);
            }
            bool isDeinonychus = idsList.Any(id => id.Contains("Deinonychus", StringComparison.OrdinalIgnoreCase));
            string difficultyRanges = isMissing || isDeinonychus ? GetDifficultyRanges(idsList.FirstOrDefault()) : string.Empty;
            string creatureIDsString = string.Join(",", idsList.Select(id => $@"""{id}"""));
            string spawnEntry = $@"(AnEntryName=""{entryName}"",ColorSets=""DinoColorSet_AllColors_C""{percentList}{offsets},EntryWeight={group.Weight},NPCsToSpawnStrings=({creatureIDsString}){difficultyRanges})";

            return spawnEntry;
        }

        private static string GetDifficultyRanges(string creatureId)
        {
            string levelRanges = ",NPCDifficultyLevelRanges=((EnemyLevelsMin=({0}),EnemyLevelsMax=({1}),GameDifficulties=(0)))";
            return creatureId switch
            {
                "BogSpider_Character_BP_C" => string.Format(levelRanges, 20, 37),
                "RockDrake_Character_BP_C" => string.Format(levelRanges, 15, 27),
                "Wyvern_Character_BP_Fire_C" => string.Format(levelRanges, 15, 27),
                "Wyvern_Character_BP_Lightning_C" => string.Format(levelRanges, 15, 27),
                "Wyvern_Character_BP_Poison_C" => string.Format(levelRanges, 15, 27),
                "Ragnarok_Wyvern_Override_Ice_C" => string.Format(levelRanges, 15, 27),
                "Deinonychus_Character_BP_C" => string.Format(levelRanges, 15, 38),
                "Cherufe_Character_BP_C" => string.Format(levelRanges, 10, 20),
                _ => string.Empty
            };
        }

        private static List<CreatureSpawnInfo> FilterProblemAndBasicDinos(List<CreatureSpawnInfo> allCreatures)
        {
            List<CreatureSpawnInfo> problemDinos = allCreatures.Select(dino => dino)
                .Where(dino => ProblemDinos().Contains(dino.NameTag) && VariantTypes().Any(variant => !variant.Equals("Bionic") && dino.CreatureID.Contains(variant)))
                .ToList();

            allCreatures.RemoveAll(dino => problemDinos.Any(problemDino => dino.CreatureID.Equals(problemDino.CreatureID)));

            List<CreatureSpawnInfo> basicDinos = allCreatures.Where(dino => !IgnoreBasic().Any(basic => basic.Equals(dino.CreatureName)))
                .GroupBy(dino => dino.NameTag)
                .Where(g => g.Skip(1).Any() && g.Any(gdino => VariantTypes().Any(variant => gdino.CreatureID.Contains(variant))))
                .SelectMany(dino => dino)
                .Where(dino => !VariantTypes().Any(variant => dino.CreatureID.Contains(variant)))
                .OrderBy(dino => dino.NameTag)
                .ToList();

            allCreatures.RemoveAll(dino => basicDinos.Any(basicDino => dino.CreatureID.Equals(basicDino.CreatureID)));

            return allCreatures.OrderBy(dino => dino.NameTag).ToList();
        }

        private static List<CreatureSpawnInfo> GroupCreatures(List<CreatureSpawnInfo> creatureInfos, List<string> groupCreatures)
        {
            List<CreatureSpawnInfo> groupCreaturesAllVariants = creatureInfos
                .Where(creature => groupCreatures.Any(gCreature => CreatureHasVariant(creature.CreatureName, gCreature)))
                .ToList();

            return groupCreaturesAllVariants;
        }

        private static bool CreatureHasVariant(string creatureName1, string creatureName2)
        {
            return BaseDinoName(creatureName1).Equals(BaseDinoName(creatureName2), StringComparison.OrdinalIgnoreCase);
        }

        private static string BaseDinoName(string dinoName)
        {
            return dinoName.Replace(" ", "")
                .Replace("Aberrant", "")
                .Replace("X", "")
                .Replace("R", "")
                .Replace("Lunar", "")
                .Replace("Tek", "");
        }

        private static string GetExtraCreatureSpawnEntry(CreatureSpawnInfo extraCreature, string containerClassName)
        {
            SpawnGroup group = new(extraCreature.MaxPercentage);
            group.Creatures.Add(new Creature(extraCreature.CreatureName.Split(",").FirstOrDefault()) { Percentages = new List<decimal>() { extraCreature.MaxPercentage } });
            string newEntry = BuildSpawnEntryString(group, containerClassName, new List<CreatureSpawnInfo>() { extraCreature });
            return newEntry;
        }

        private static string GetMissingCreatureSpawnEntry(CreatureSpawnInfo missingCreature, string containerClassName, string mapName)
        {
            string newEntry = string.Empty;
            List<string> creatureLocations = MissingCreatureInfo()
                .FirstOrDefault(missingCreatureInfo => missingCreatureInfo.CreatureName.Equals(missingCreature.CreatureName))
                .MapLocations
                .FirstOrDefault(mapLocation => mapLocation.MapName.Equals(mapName))
                .Locations;

            foreach (string location in creatureLocations)
            {
                if (containerClassName.Replace("_", "").Contains(location))
                {
                    SpawnGroup group = new(missingCreature.MaxPercentage);
                    group.Creatures.Add(new Creature(missingCreature.CreatureName.Split(",").FirstOrDefault()) { Percentages = new List<decimal>() { missingCreature.MaxPercentage } });
                    newEntry = BuildSpawnEntryString(group, containerClassName, new List<CreatureSpawnInfo>() { missingCreature }, true);
                }
            }

            return newEntry;
        }

        private static List<string> GetMissingCreatureSpawnLimit(CreatureSpawnInfo missingCreature, string containerClassName, string mapName)
        {
            List<string> newLimits = new();
            List<string> creatureLocations = MissingCreatureInfo()
                .FirstOrDefault(location => location.CreatureName.Equals(missingCreature.CreatureName))
                .MapLocations
                .FirstOrDefault(mapLocations => mapLocations.MapName.Equals(mapName))
                .Locations;

            foreach (string location in creatureLocations)
            {
                if (containerClassName.Replace("_", "").Contains(location))
                {
                    newLimits.AddRange(GetSpawnLimits(new List<CreatureSpawnInfo>() { missingCreature }));
                }
            }

            return newLimits;
        }

        private static string GetPercentageList(IEnumerable<decimal> percentages)
        {
            return $@",NPCsToSpawnPercentageChance=({string.Join(",", percentages.Select(per => Math.Round(per, 3)))}),ManualSpawnPointSpreadRadius=650";
        }

        private static string GetSpawnOffsets(IEnumerable<string> spawns)
        {
            List<string> offsets = new();

            string neg = string.Empty;
            int yOffset = 0;
            foreach (string spawn in spawns)
            {
                string offset = $"(X=0,Y={neg}{yOffset},Z=0)";
                neg = (neg.Equals(string.Empty) && !yOffset.Equals(0)) ? "-" : string.Empty;
                int offsetAmountAdd = ChonkList().Any(chonk => spawn.Contains(chonk)) ? 1000 : 400;
                yOffset += neg.Equals(string.Empty) ? offsetAmountAdd : 0;
                offsets.Add(offset);
            }

            return $",NPCsSpawnOffsets=({string.Join(",", offsets)})";
        }

        private static string GetEntryName(string className, string creatureName)
        {
            //string classNameSection = className.Substring(className.IndexOf("DinoSpawnEntries") + 16).Replace("DinoSpawnEntries", "").Replace("-", "");

            int place = className.LastIndexOf("_C");
            string classNameSection = className.Remove(place, 2).Replace("_", "");
            return $@"{creatureName.Replace(" ", "")}{classNameSection}";
        }

        static List<CreatureSpawnInfo> GetAllCreatureSpawnInfo()
        {
            string fileText = File.ReadAllText($@"{AppContext.BaseDirectory}\CreatureIDs.json");
            List<CreatureSpawnInfo> creatureInfos = JsonSerializer.Deserialize<List<CreatureSpawnInfo>>(fileText);
            return creatureInfos;
        }

        private static List<MissingCreature> MissingCreatureInfo()
        {
            string fileText = File.ReadAllText($@"{AppContext.BaseDirectory}\MissingCreatures.json");
            List<MissingCreature> creatureLocations = JsonSerializer.Deserialize<List<MissingCreature>>(fileText);
            return creatureLocations;
        }

        static void BuildMapJsonList()
        {
            string directory = AppContext.BaseDirectory;
            string entriesTextFile = $@"{directory}\SpawnEntries.txt";
            List<Map> mapList = GetMapList(entriesTextFile);
            WriteItemListToFiles(mapList, "spawnEntries");
        }

        private static void WriteItemListToFiles<T>(List<T> itemList, string fileName)
        {
            string workingDirectory = AppContext.BaseDirectory;
            string projDirectory = Directory.GetParent(AppContext.BaseDirectory).Parent.Parent.Parent.FullName;

            string mapListJson = JsonSerializer.Serialize(itemList, itemList.GetType());
            File.WriteAllText(@$"{workingDirectory}\{fileName}.json", mapListJson);
            File.WriteAllText(@$"{projDirectory}\{fileName}.json", mapListJson);
        }

        private static List<Map> GetMapList(string entriesTextFile)
        {
            List<Map> mapList = new();

            using (StreamReader streamReader = new(entriesTextFile))
            {
                string fileText = streamReader.ReadToEnd();
                List<string> mapTextList = new(fileText.Split($"{Environment.NewLine}{Environment.NewLine}"));
                mapTextList.ForEach(mapText => mapList.Add(GetMapInfo(mapText)));
            }

            return mapList;
        }

        private static List<CreatureSpawnInfo> GetCreatureIDsList(string entriesTextFile)
        {
            List<CreatureSpawnInfo> creatureSpawnInfoList = new();

            using (StreamReader streamReader = new(entriesTextFile))
            {
                string fileText = streamReader.ReadToEnd();
                List<string> creatureIDsTextList = new(fileText.Split($"{Environment.NewLine}"));
                creatureIDsTextList.ForEach(mapText => creatureSpawnInfoList.Add(GetCreatureIDInfo(mapText)));
            }

            return creatureSpawnInfoList;
        }

        private static CreatureSpawnInfo GetCreatureIDInfo(string creatureIDText)
        {
            string[] textList = creatureIDText.Split(',');
            string creatureName = textList[0];
            string nameTag = textList[1];
            string id = textList[2];

            CreatureSpawnInfo creatureSpawnInfo = new(creatureName, nameTag, id, 0.3m);

            return creatureSpawnInfo;
        }

        private static Map GetMapInfo(string mapText)
        {
            List<string> entries = new(mapText.Split(Environment.NewLine));
            Map map = new(string.Empty);
            Container container = new(string.Empty);

            foreach (string entry in entries)
            {
                if (MapNames().Contains(entry, StringComparer.OrdinalIgnoreCase))
                {
                    map.MapName = entry;
                    continue;
                }

                if (Regex.IsMatch(entry, @"((\w+[-]?\w+)(_C))"))
                {
                    if (!string.IsNullOrEmpty(container.ClassName))
                        map.Containers.Add(container);
                    container = new Container(entry);
                    continue;
                }

                if (!Regex.IsMatch(entry, @"^\d"))
                {
                    AppendToGroupEntry(container.Groups.Last(), entry);
                    continue;
                }

                container.Groups.Add(GetGroupEntry(entry));
            }

            map.Containers.Add(container);
            return map;
        }

        private static void AppendToGroupEntry(SpawnGroup spawnGroup, string entry)
        {
            List<string> entryOptions = new(entry.Split(','));

            Creature creature = new(entryOptions[0]);

            foreach (string option in entryOptions.Skip(1))
            {
                if (option.Contains('%'))
                {
                    decimal percent = decimal.Parse(option.Trim('%')) / 100;
                    creature.Percentages.Add(percent);
                    continue;
                }

                if (creature.Min.Equals(0))
                {
                    creature.Min = int.Parse(option);
                    continue;
                }

                creature.Max = int.Parse(option);
                creature.Max = creature.Min > creature.Max ? creature.Min : creature.Max;
            }

            spawnGroup.Creatures.Add(creature);
        }

        private static SpawnGroup GetGroupEntry(string entry)
        {
            List<string> entryOptions = new(entry.Split(','));

            SpawnGroup spawnGroup = new(decimal.Parse(entryOptions[0]));

            Creature creature = new(entryOptions[1]);

            foreach (string option in entryOptions.Skip(2))
            {
                if (option.Contains('%'))
                {
                    decimal percent = decimal.Parse(option.Trim('%')) / 100;
                    creature.Percentages.Add(percent);
                    continue;
                }

                if (creature.Min.Equals(0))
                {
                    creature.Min = int.Parse(option);
                    continue;
                }

                creature.Max = int.Parse(option);
            }

            spawnGroup.Creatures.Add(creature);

            return spawnGroup;
        }

        /// <summary>
        /// Adds extra spawn locations for dinos to balance progression
        /// </summary>
        /// <param name="mapName"></param>
        /// <param name="containerClassName"></param>
        /// <returns></returns>
        private static List<CreatureSpawnInfo> GetExtraCreatures(string mapName, string containerClassName)
        {
            List<CreatureSpawnInfo> creatures = new();
            List<Tuple<string, List<CreatureSpawnInfo>>> creatureLocations;

            switch (mapName)
            {
                case "Lost Island":
                    creatureLocations = GetLostIslandAddedCreatures();
                    if (creatureLocations.Select(cl => cl.Item1).Any(container => container.Equals(containerClassName)))
                        creatures = creatureLocations.FirstOrDefault(cl => cl.Item1.Equals(containerClassName)).Item2;
                    break;
                case "The Center":
                    creatureLocations = GetTheCenterAddedCreatures();
                    if (creatureLocations.Select(cl => cl.Item1).Any(container => container.Equals(containerClassName)))
                        creatures = creatureLocations.FirstOrDefault(cl => cl.Item1.Equals(containerClassName)).Item2;
                    break;
                case "Fjordur":
                    creatureLocations = GetFjordurAddedCreatures();
                    if (creatureLocations.Select(cl => cl.Item1).Any(container => container.Equals(containerClassName)))
                        creatures = creatureLocations.FirstOrDefault(cl => cl.Item1.Equals(containerClassName)).Item2;
                    break;
            }

            return creatures;
        }

        private static List<Tuple<string, List<CreatureSpawnInfo>>> GetLostIslandAddedCreatures()
        {
            var containerCreatureList = new List<Tuple<string, List<CreatureSpawnInfo>>>();
            List<CreatureSpawnInfo> allCreatures = GetAllCreatureSpawnInfo();

            containerCreatureList.Add(new Tuple<string, List<CreatureSpawnInfo>>("DinoSpawnEntriesBeach_LostIsland_C",
                new List<CreatureSpawnInfo>() {
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Sinomacrops")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Aberrant Sarco")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("R Procoptodon")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("R Equus"))
                }));

            containerCreatureList.Add(new Tuple<string, List<CreatureSpawnInfo>>("DinoSpawnEntriesJungle_LostIsland_C",
                new List<CreatureSpawnInfo>() {
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("X Sabertooth")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Ravager")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Lymantria")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Megaloceros"))
                }));

            containerCreatureList.Add(new Tuple<string, List<CreatureSpawnInfo>>("DinoSpawnEntriesMountain_C",
                new List<CreatureSpawnInfo>() {
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Morellatops")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Thorny Dragon")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Aberrant Megalania")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Aberrant Araneo")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Mantis"))
                }));

            containerCreatureList.Add(new Tuple<string, List<CreatureSpawnInfo>>("DinoSpawnEntriesMountain1_C",
                new List<CreatureSpawnInfo>() {
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Morellatops")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Thorny Dragon")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Aberrant Megalania")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Aberrant Araneo")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Mantis")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("R Thylacoleo"))
                }));

            containerCreatureList.Add(new Tuple<string, List<CreatureSpawnInfo>>("DinoSpawnEntriesGrassland_LostIsland_1_C",
                new List<CreatureSpawnInfo>() {
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("R Thylacoleo")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Chalicotherium")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("X Woolly Rhino"))
                }));

            containerCreatureList.Add(new Tuple<string, List<CreatureSpawnInfo>>("DinoSpawnEntriesGrassland_LostIsland_2_C",
                new List<CreatureSpawnInfo>() {
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Chalicotherium")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("X Woolly Rhino"))
                }));

            containerCreatureList.Add(new Tuple<string, List<CreatureSpawnInfo>>("DinoSpawnEntriesGrassland_LostIsland_C",
                new List<CreatureSpawnInfo>() {
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("R Daeodon")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Chalicotherium")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("X Woolly Rhino"))
                }));

            return containerCreatureList;
        }

        private static List<Tuple<string, List<CreatureSpawnInfo>>> GetTheCenterAddedCreatures()
        {
            var containerCreatureList = new List<Tuple<string, List<CreatureSpawnInfo>>>();
            List<CreatureSpawnInfo> allCreatures = GetAllCreatureSpawnInfo();

            containerCreatureList.Add(new Tuple<string, List<CreatureSpawnInfo>>("DinoSpawnEntriesJungle_C",
                new List<CreatureSpawnInfo>() {
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("X Sabertooth")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("R Procoptodon")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Lymantria")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("R Equus")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Terror Bird")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Hyaenodon"))
                }));

            containerCreatureList.Add(new Tuple<string, List<CreatureSpawnInfo>>("DinoSpawnEntriesBeach_C",
                new List<CreatureSpawnInfo>() {
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Aberrant Sarco")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Hyaenodon"))
                }));

            containerCreatureList.Add(new Tuple<string, List<CreatureSpawnInfo>>("SnowGrasslandsUnderArea_Spawn_C",
                new List<CreatureSpawnInfo>() {
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("R Thylacoleo")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("R Daeodon"))
                }));

            containerCreatureList.Add(new Tuple<string, List<CreatureSpawnInfo>>("SnowGrasslands_Spawn_C",
                new List<CreatureSpawnInfo>() {
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("X Yutyrannus")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("R Daeodon"))
                }));

            //containerCreatureList.Add(new Tuple<string, List<CreatureSpawnInfo>>("DinoSpawnEntries_Ocean_C",
            //    new List<CreatureSpawnInfo>() {
            //        allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("X Basilosaurus"))
            //    }));

            containerCreatureList.Add(new Tuple<string, List<CreatureSpawnInfo>>("DinoSpawnEntriesMountain_C",
                new List<CreatureSpawnInfo>() {
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Aberrant Megalania")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Aberrant Araneo")),
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Aberrant Megalosaurus"))
                }));

            return containerCreatureList;
        }

        private static List<Tuple<string, List<CreatureSpawnInfo>>> GetFjordurAddedCreatures()
        {
            var containerCreatureList = new List<Tuple<string, List<CreatureSpawnInfo>>>();
            List<CreatureSpawnInfo> allCreatures = GetAllCreatureSpawnInfo();

            containerCreatureList.Add(new Tuple<string, List<CreatureSpawnInfo>>("DinoSpawnEntriesRedwoods_C",
                new List<CreatureSpawnInfo>() {
                    allCreatures.FirstOrDefault(creature => creature.CreatureName.Equals("Ravager"))
                }));

            return containerCreatureList;
        }

        private static List<string> MapNames()
        {
            return new List<string>()
            {
                "The Island",
                "The Center",
                "Scorched Earth",
                "Ragnarok",
                "Aberration",
                "Extinction",
                "Valguero",
                "Genesis: Part 1",
                "Crystal Isles",
                "Genesis: Part 2",
                "Lost Island",
                "Fjordur"
            };
        }

        private static List<string> VariantTypes()
        {
            return new List<string>()
            {
                "Aberrant",
                "Bionic",
                "Eden",
                "Rockwell",
                "Snow",
                "Volcano",
                "Bog",
                "Ocean",
                "Ice",
                "Chalk",
                "Rubble",
                "Lunar",
                "Yeti",
                "DA_"
            };
        }

        private static List<string> ChonkList()
        {
            return new List<string>()
            {
                "Sauropod",
                "Cherufe",
                "Plesiosaur"
            };
        }

        /// <summary>
        /// List of dinos that have variant spawn issues
        /// </summary>
        /// <returns></returns>
        private static List<string> ProblemDinos()
        {
            return new List<string>()
            {
                "Carno",
                "Rex",
                "Mega",
                "Mosasaur",
                "Coel"
            };
        }

        /// <summary>
        /// Basic dinos to allow to spawn
        /// </summary>
        /// <returns></returns>
        private static List<string> IgnoreBasic()
        {
            return new List<string>()
            {
                "Carnotaurus",
                "Rex",
                "Megalodon",
                "Mosasaur",
                "Coelacanth",
                "Basilosaurus"
            };
        }
    }
}
