using System.Text.Json;
using WhatToBuild.Data;

namespace WhatToBuild.Game;

public static class GameStateParser
{
    private const string RawNamePrefix = "game_character_displayname_";

    public const int SoulDragonCount = 4;

    public static GameState Parse(string json, ChampionRepository champions, ItemRepository items)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var gameTime = Number(root, "gameData", "gameTime");
        var active = root.TryGetProperty("activePlayer", out var a) ? a : default;
        var activeNames = active.ValueKind == JsonValueKind.Object ? Names(active) : [];

        var players = new List<PlayerState>();
        var teamByName = new Dictionary<string, Team>(StringComparer.OrdinalIgnoreCase);
        var unknownItems = new List<int>();
        var unknownChampions = new List<string>();

        foreach (var raw in Array(root, "allPlayers"))
        {
            var champion = ResolveChampion(raw, champions);

            if (champion is null)
            {
                unknownChampions.Add(Text(raw, "championName"));
                continue;
            }

            var team = Text(raw, "team") == "CHAOS" ? Team.Chaos : Team.Order;
            var names = Names(raw);
            var isActive = names.Overlaps(activeNames);

            foreach (var name in names)
            {
                teamByName[name] = team;
            }

            players.Add(new PlayerState
            {
                Champion = champion,
                Team = team,
                Level = (int)Number(raw, "level"),
                Items = ReadItems(raw, items, unknownItems),
                EstimatedStacks = isActive
                    ? []
                    : champion.Stacking
                        .Select(s => new StackEstimate(s.Stat, s.InitialStacksPerMinute * gameTime / 60))
                        .ToList(),
                Kills = (int)Number(raw, "scores", "kills"),
                Deaths = (int)Number(raw, "scores", "deaths"),
                Assists = (int)Number(raw, "scores", "assists"),
                CreepScore = (int)Number(raw, "scores", "creepScore"),
                IsDead = raw.TryGetProperty("isDead", out var dead) && dead.ValueKind == JsonValueKind.True,
                Position = Text(raw, "position"),
                IsActivePlayer = isActive,
            });
        }

        return new GameState
        {
            GameTime = gameTime,
            CurrentGold = active.ValueKind == JsonValueKind.Object ? Number(active, "currentGold") : 0,
            Players = players,
            Objectives = ReadObjectives(root, teamByName),
            ActivePlayerStats = active.ValueKind == JsonValueKind.Object ? ReadStats(active) : null,
            UnknownItemIds = unknownItems,
            UnknownChampions = unknownChampions,
        };
    }

    private static Champion? ResolveChampion(JsonElement raw, ChampionRepository champions)
    {
        var rawName = Text(raw, "rawChampionName");

        if (rawName.StartsWith(RawNamePrefix, StringComparison.Ordinal)
            && champions.ByInternalName(rawName[RawNamePrefix.Length..]) is { } byInternal)
        {
            return byInternal;
        }

        return champions.ByName(Text(raw, "championName"));
    }

    private static List<OwnedItem> ReadItems(JsonElement raw, ItemRepository items, List<int> unknown)
    {
        var owned = new List<OwnedItem>();

        foreach (var entry in Array(raw, "items"))
        {
            var id = (int)Number(entry, "itemID");
            var count = Math.Max(1, (int)Number(entry, "count"));

            if (items.ByRiotId(id) is { } item)
            {
                owned.Add(new OwnedItem(item, count));
            }
            else
            {
                unknown.Add(id);
            }
        }

        return owned;
    }

    private static Dictionary<Team, ObjectiveCounts> ReadObjectives(JsonElement root, Dictionary<string, Team> teamByName)
    {
        var objectives = new Dictionary<Team, ObjectiveCounts>
        {
            [Team.Order] = new(),
            [Team.Chaos] = new(),
        };

        var events = (root.TryGetProperty("events", out var e) ? Array(e, "Events") : [])
            .OrderBy(ev => Number(ev, "EventTime"));

        foreach (var ev in events)
        {
            switch (Text(ev, "EventName"))
            {
                case "DragonKill" when KillerTeam(ev, teamByName) is { } team:
                    var type = Text(ev, "DragonType");
                    if (type == "Elder")
                    {
                        objectives[team].Elders++;
                    }
                    else
                    {
                        objectives[team].Dragons[type] = objectives[team].Dragons.GetValueOrDefault(type) + 1;

                        if (objectives[team].TotalDragons == SoulDragonCount && objectives[team].SoulType is null)
                        {
                            objectives[team].SoulType = type;
                        }
                    }
                    break;

                case "BaronKill" when KillerTeam(ev, teamByName) is { } team:
                    objectives[team].Barons++;
                    break;

                case "HeraldKill" when KillerTeam(ev, teamByName) is { } team:
                    objectives[team].Heralds++;
                    break;

                case "TurretKilled" when DestroyingTeam(Text(ev, "TurretKilled")) is { } team:
                    objectives[team].Turrets++;
                    break;

                case "InhibKilled" when DestroyingTeam(Text(ev, "InhibKilled")) is { } team:
                    objectives[team].Inhibitors++;
                    break;
            }
        }

        return objectives;
    }

    private static Team? KillerTeam(JsonElement ev, Dictionary<string, Team> teamByName) =>
        teamByName.TryGetValue(Text(ev, "KillerName"), out var team) ? team : null;

    private static Team? DestroyingTeam(string structure) =>
        structure.Contains("_T1_", StringComparison.Ordinal) ? Team.Chaos
        : structure.Contains("_T2_", StringComparison.Ordinal) ? Team.Order
        : null;

    private static StatSheet ReadStats(JsonElement active)
    {
        if (!active.TryGetProperty("championStats", out var s))
        {
            return new StatSheet();
        }

        return new StatSheet
        {
            Health = Number(s, "maxHealth"),
            Mana = Number(s, "resourceMax"),
            AttackDamage = Number(s, "attackDamage"),
            AbilityPower = Number(s, "abilityPower"),
            Armor = Number(s, "armor"),
            MagicResist = Number(s, "magicResist"),
            AttackSpeed = Number(s, "attackSpeed"),
            HealthRegen = Number(s, "healthRegenRate"),
            ManaRegen = Number(s, "resourceRegenRate"),
            MoveSpeed = Number(s, "moveSpeed"),
            AttackRange = Number(s, "attackRange"),
        };
    }

    private static HashSet<string> Names(JsonElement player)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in new[] { "riotId", "riotIdGameName", "summonerName" })
        {
            var value = Text(player, key);
            if (value.Length > 0)
            {
                names.Add(value);
            }
        }

        return names;
    }

    private static IEnumerable<JsonElement> Array(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToList()
            : [];

    private static string Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static double Number(JsonElement element, params string[] path)
    {
        var current = element;

        foreach (var step in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(step, out current))
            {
                return 0;
            }
        }

        return current.ValueKind == JsonValueKind.Number ? current.GetDouble() : 0;
    }
}
