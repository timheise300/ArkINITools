using SpawnEntryRepository;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ArkINIValidator
{
    class Program
    {
        static void Main()
        {
            string filePath = @"C:\Users\timhe\OneDrive\Documents\Ark Server Backup\GeneratedSpawnConfigs\";
            List<string> maps = MapNames();

            Console.WriteLine("Ark INI Validator");
            Console.WriteLine();

            string map = null;

            while (map is null)
            {
                Console.WriteLine("Please Enter a map name from below:");
                maps.ForEach(map => Console.WriteLine(map));
                string mapName = Console.ReadLine();
                map = maps.FirstOrDefault(map => map.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                    Console.WriteLine("invalid map selection, try again");
            }

            Console.WriteLine();
            string fileText = File.ReadAllText($@"{filePath}{map.Replace(" ", "")}SpawnConfig.INI");

            List<string> results = FindAllSpawnEntryContainers(fileText);

            List<SpawnEntryContainer> containers = BuildSpawnEntryContainerList(results);

            CheckForDuplicateContainerIds(containers);
            CheckForDuplicateSpawnEntryIds(containers);
            containers.ForEach(container => EvaluateContainerCodes(container));

            Console.WriteLine("Validation Complete");
            Console.Read();
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

        private static void CheckForDuplicateSpawnEntryIds(List<SpawnEntryContainer> containers)
        {
            List<string> DuplicateSpawnEntries = containers.SelectMany(container => container.SpawnEntries)
                .Select(entry => entry.EntryName)
                .GroupBy(x => x)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            string message = DuplicateSpawnEntries.Count.Equals(0) ?
                "No duplicate Spawn IDs found." :
                $"Duplicate SpawnEntryIDs found: \n{String.Join(Environment.NewLine, DuplicateSpawnEntries)}";
            Console.WriteLine(message);
            Console.WriteLine();
        }

        private static void CheckForDuplicateContainerIds(List<SpawnEntryContainer> containers)
        {
            List<string> DuplicateContainerEntries = containers.GroupBy(container => container.ContainerName)
              .Where(g => g.Count() > 1)
              .Select(y => y.Key)
              .ToList();

            string message = DuplicateContainerEntries.Count.Equals(0) ?
                "No duplicate Container IDs found." :
                $"Duplicate Container IDs found: \n{String.Join(Environment.NewLine, DuplicateContainerEntries)}";
            Console.WriteLine(message);
        }

        private static void EvaluateContainerCodes(SpawnEntryContainer container)
        {
            ValidateEntryLimitMatches(container);
            container.SpawnEntries.ForEach(entry =>
            {
                if (entry.SpawnStrings.Count > 1)
                {
                    ValidateMobSpawnCodes(entry, container.ContainerName);
                }
            });
        }

        private static void ValidateMobSpawnCodes(SpawnEntry entry, string containerName)
        {
            if (entry.SpawnRadius > 650)
                Console.WriteLine($"{containerName} has a Spawn Radius entry lower than 650, this is not recommended.");

            if (!entry.SpawnStrings.Count.Equals(entry.SpawnPercentChance.Count))
            {
                string message = entry.SpawnStrings.Count > entry.SpawnPercentChance.Count ? "spawn strings than % chance values" : "% chance values than spawn strings";
                Console.WriteLine($"{containerName} entry {entry.EntryName} has more {message}");
            }

            if (!entry.SpawnStrings.Count.Equals(entry.SpawnOffsets.Count))
            {
                string message = entry.SpawnStrings.Count > entry.SpawnPercentChance.Count ? "spawn strings than offset values" : "offset values than spawn strings";
                Console.WriteLine($"{containerName} entry {entry.EntryName} has more {message}");
            }

            List<int> offSetYMatches = entry.SpawnOffsets
                .GroupBy(offset => offset.Y)
                .Where(g => g.Count() > 1)
                .Select(offset => offset.Key)
                .ToList();

            if (offSetYMatches.Count > 0)
            {
                Console.WriteLine($"{containerName} entry {entry.EntryName} has matching offset Y coords");
                Console.WriteLine();
            }

            if (entry.SpawnPercentChance.Any(chance => chance > 1))
            {
                Console.WriteLine($"{containerName} entry {entry.EntryName} has spawn percent chance above 1 (valid values are decimals between 0 and 1)");
                Console.WriteLine();
            }
        }

        private static void ValidateEntryLimitMatches(SpawnEntryContainer container)
        {
            List<string> missingLimits = container.SpawnEntries
                .SelectMany(entry => entry.SpawnStrings.Distinct())
                .Except(container.SpawnLimits.Select(limit => limit.ClassString))
                .ToList();

            List<string> missingEntries = container.SpawnLimits
                .Select(entry => entry.ClassString)
                .Except(container.SpawnEntries.SelectMany(limit => limit.SpawnStrings.Distinct()))
                .ToList();

            if (missingLimits.Count.Equals(0) && missingEntries.Count.Equals(0))
                return;

            if (missingLimits.Count > 0)
                Console.WriteLine($"{container.ContainerName} has missing limit matches:");
            missingLimits.ForEach(spawnString => Console.WriteLine($" - {spawnString}"));

            if (missingEntries.Count > 0)
                Console.WriteLine($"{container.ContainerName} has missing entry matches:");
            missingEntries.ForEach(spawnString => Console.WriteLine($" - {spawnString}"));

            Console.WriteLine();
        }

        private static List<string> FindAllSpawnEntryContainers(string iniText)
        {
            List<string> matches = new();

            List<string> lines = iniText.Split(Environment.NewLine).ToList();

            foreach (string line in lines)
            {
                if (line.Contains("ConfigOverrideNPCSpawnEntriesContainer"))
                    matches.Add(line);
            }

            return matches;
        }

        private static List<SpawnEntryContainer> BuildSpawnEntryContainerList(List<string> SpawnEntryContainerStringList)
        {
            List<SpawnEntryContainer> spawnEntryContainers = new();

            foreach (string containerString in SpawnEntryContainerStringList)
            {
                string containerName = Regex.Match(
                        Regex.Match(containerString, "NPCSpawnEntriesContainerClassString=\".*?\"").Value, "\".*?\"")
                        .Value
                        .Trim('"');

                ValidateContainerFormat(containerString, containerName);

                SpawnEntryContainer container = new()
                {
                    ContainerName = containerName,
                    SpawnEntries = FindAllSpawnEntries(containerString),
                    SpawnLimits = FindAllSpawnLimits(containerString)
                };
                spawnEntryContainers.Add(container);
            }

            return spawnEntryContainers;
        }

        private static void ValidateContainerFormat(string containerString, string containerName)
        {
            int startPerenCount = containerString.Count(p => (p.Equals('(')));
            int endPerenCount = containerString.Count(p => (p.Equals(')')));
            bool hasExcessCommas = Regex.IsMatch(containerString, @"(,)\1{1,}", RegexOptions.IgnoreCase);
            bool hasIrregularCommaParensSequence = Regex.IsMatch(containerString, @"(\),\)|,\),|\(,\(|,\(,|"""")", RegexOptions.IgnoreCase);
            bool hasMissingParameters = Regex.IsMatch(containerString, @"(\(\))");

            if (!startPerenCount.Equals(endPerenCount))
            {
                Console.WriteLine($"{containerName} contains mismatched parentheses");
                Console.WriteLine();
            }

            if (hasExcessCommas)
            {
                Console.WriteLine($"{containerName} contains consecutive commas");
                Console.WriteLine();
            }

            if (hasIrregularCommaParensSequence)
            {
                Console.WriteLine($"{containerName} contains irregular comma/parentheses sequence");
                Console.WriteLine();
            }

            if (containerString.Contains("()"))
            {
                Console.WriteLine($"{containerName} has empty parentheses sequences");
            }
        }

        private static List<SpawnEntry> FindAllSpawnEntries(string iniText)
        {
            List<SpawnEntry> matches = new();

            var entryList = Regex.Split(iniText, @"\((?=[AnEntryName=])")
                .Select(entry => entry.Trim(','))
                .Where(entry => entry.Contains("AnEntryName"))
                .ToList();

            foreach (string entryString in entryList)
            {
                SpawnEntry entry = new()
                {
                    EntryName = GetFieldValue(entryString, "AnEntryName=\".*?\""),
                    EntryWeight = decimal.Parse(GetFieldValue(entryString, @"EntryWeight=(\d*\.?\d+|\d+\.?\d*)")),
                    Colorsets = GetFieldValue(entryString, "ColorSets=\".*?\""),
                    SpawnStrings = GetFieldValue(entryString, @"NPCsToSpawnStrings=\(([^)]*)\)")
                        .Trim('"')
                        .Split(',')
                        .ToList()
                };

                if (entry.SpawnStrings.Count > 1)
                {
                    entry.SpawnRadius = decimal.Parse(GetFieldValue(entryString, @"ManualSpawnPointSpreadRadius=(\d*\.?\d+|\d+\.?\d*)"));
                    entry.SpawnPercentChance = GetSpawnPercentChances(entryString);
                    entry.SpawnOffsets = GetSpawnOffsets(entryString);
                }

                if (entry.SpawnStrings.Count.Equals(1))
                {
                    if (entryString.Contains("ManualSpawnPointSpreadRadius") ||
                        entryString.Contains("NPCsToSpawnPercentageChance") ||
                        entryString.Contains("NPCsSpawnOffsets"))
                    {
                        Console.WriteLine($"{entry.EntryName} contains mob spawn info, but only one SpawnString");
                        Console.WriteLine();
                    }
                }

                matches.Add(entry);
            }

            return matches;
        }

        private static List<Coords> GetSpawnOffsets(string iniText)
        {
            string spawnOffsets = GetFieldValue(iniText, @"NPCsSpawnOffsets=\(\(.*?\)\)", false);
            List<Coords> chances = spawnOffsets.Split("),(").ToList().Select(chance => GetCoords(chance.Trim('(', ')'))).ToList();
            return chances;
        }

        private static Coords GetCoords(string chance)
        {
            int x = int.Parse(GetFieldValue(chance, "X=.*?,").Replace(",", ""));
            int y = int.Parse(GetFieldValue(chance, "Y=.*?,").Replace(",", ""));
            int z = int.Parse(GetFieldValue(chance, "Z=.*?$").Replace(",", ""));
            return new Coords
            {
                X = x,
                Y = y,
                Z = z
            };
        }

        private static List<decimal> GetSpawnPercentChances(string iniText)
        {
            string spawnChances = GetFieldValue(iniText, @"NPCsToSpawnPercentageChance=\(([^)]*)\)");
            try
            {
                List<decimal> chances = spawnChances.Split(',').ToList().Select(chance => decimal.Parse(chance)).ToList();
                return chances;
            }
            catch (Exception e)
            {
                Console.WriteLine($@"Error getting SpawnPercentChance on {GetFieldValue(iniText, "AnEntryName=\".*?\"")}: {e.Message}{Environment.NewLine}");
                return new List<decimal>();
            }
        }

        private static List<SpawnLimit> FindAllSpawnLimits(string iniText)
        {
            List<SpawnLimit> matches = new();

            Regex regex = new(@"\((NPCClassString=.*?)\)");
            foreach (Match match in regex.Matches(iniText).Cast<Match>())
            {
                SpawnLimit entry = new()
                {
                    ClassString = GetFieldValue(match.Value, "NPCClassString=\".*?\""),
                    PercentToAllow = decimal.Parse(GetFieldValue(match.Value, @"MaxPercentageOfDesiredNumToAllow=\d*\.?\d+|\d+\.?\d*"))
                };
                matches.Add(entry);
            }

            return matches;
        }

        private static string GetFieldValue(string text, string regexPattern, bool removeParens = true)
        {
            string value = string.Empty;
            string fieldName = regexPattern.Split("=")[0];

            if (!text.Contains(fieldName))
                return value;

            try
            {
                string field = Regex.Match(text, regexPattern).Value;
                value = field[(field.IndexOf('=') + 1)..];
                value = removeParens ? value.Replace("(", "").Replace(")", "") : value;
                value = value.Replace("\"", "");

                if (string.IsNullOrEmpty(value))
                    throw new Exception("INVALID");
            }
            catch (Exception e)
            {
                string entryName = Regex.Match(text, "AnEntryName=\".*?\"").Value;
                entryName = entryName[(text.IndexOf('=') + 1)..];

                string message = e.Message.Contains("INVALID") ?
                    $"{fieldName} doesn't contain a valid value for Entry {entryName}" :
                    $"Error getting {fieldName} for Entry {entryName}: {e.Message}";

                Console.WriteLine(message);
                Console.WriteLine();
            }

            return value.Replace("\"", "");
        }
    }
}
