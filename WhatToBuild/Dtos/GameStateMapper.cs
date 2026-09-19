using WhatToBuild.Data;
using WhatToBuild.Game;

namespace WhatToBuild.Dtos;

public static class GameStateMapper
{
    private static readonly string[] PositionOrder = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    public static string ChampionIconUrl(string patch, string icon) =>
        $"https://ddragon.leagueoflegends.com/cdn/{patch}/img/champion/{icon}";

    public static string ItemIconUrl(string patch, string icon) =>
        $"https://ddragon.leagueoflegends.com/cdn/{patch}/img/item/{icon}";

    public static GameStateDto ToDto(GameState state, NeutralRepository neutrals, string patch)
    {
        var teams = new[] { Team.Order, Team.Chaos }
            .Select(team => MapTeam(state, team, neutrals, patch))
            .ToList();

        return new GameStateDto(
            GameStateDto.InGame,
            patch,
            state.GameTime,
            state.CurrentGold,
            state.GoldEarned,
            state.ActiveTeam?.ToString(),
            teams,
            state.UnknownChampions,
            state.UnknownItemIds);
    }

    private static TeamDto MapTeam(GameState state, Team team, NeutralRepository neutrals, string patch)
    {
        var players = state.Players
            .Where(p => p.Team == team)
            .OrderBy(p => PositionRank(p.Position))
            .Select(p => MapPlayer(state, p, neutrals, patch))
            .ToList();

        return new TeamDto(
            team.ToString(),
            players.Sum(p => p.Kills),
            players.Sum(p => p.Deaths),
            players.Sum(p => p.Assists),
            players.Sum(p => p.ItemValue),
            MapObjectives(state.Objectives[team], neutrals),
            players);
    }

    private static ObjectivesDto MapObjectives(ObjectiveCounts objectives, NeutralRepository neutrals) =>
        new(
            objectives.Dragons
                .Select(d => new DragonDto(d.Key, neutrals.DragonFor(d.Key)?.Drake ?? d.Key, d.Value))
                .OrderByDescending(d => d.Count)
                .ToList(),
            objectives.SoulType,
            neutrals.SoulFor(objectives.SoulType)?.Name,
            objectives.Elders,
            objectives.Barons,
            objectives.Heralds,
            objectives.Turrets,
            objectives.Inhibitors);

    private static PlayerDto MapPlayer(GameState state, PlayerState player, NeutralRepository neutrals, string patch)
    {
        var exact = player.IsActivePlayer && state.ActivePlayerStats is not null;
        var rebuilt = state.EntityFor(player, neutrals).Stats;
        var stats = exact ? state.ActivePlayerStats! : rebuilt;

        return new PlayerDto(
            player.Champion.Name,
            ChampionIconUrl(patch, player.Champion.Icon),
            player.Level,
            player.IsActivePlayer,
            player.IsDead,
            player.Position,
            player.Kills,
            player.Deaths,
            player.Assists,
            player.CreepScore,
            player.ItemValue,
            player.Items.Select(i => MapItem(i.Item, i.Count, patch, i.Slot)).ToList(),
            new StatsDto(
                stats.Health,
                stats.AttackDamage,
                stats.AbilityPower,
                stats.Armor,
                stats.MagicResist,
                stats.AttackSpeed,
                stats.MoveSpeed,
                rebuilt.Tenacity),
            player.IsActivePlayer ? state.ActivePlayerCurrentHealth : null,
            exact,
            player.EstimatedStacks.Select(s => new StackDto(s.Stat.Name, s.Stacks, s.Max)).ToList());
    }

    public static ItemDto MapItem(Item item, int count, string patch, int slot = -1) =>
        new(
            item.RiotId,
            item.Name,
            ItemIconUrl(patch, item.Icon),
            count,
            item.Cost,
            ItemDescriber.StatLines(item.Stats),
            ItemDescriber.EffectLines(item.Effects),
            slot);

    private static int PositionRank(string position)
    {
        var index = Array.IndexOf(PositionOrder, position.ToUpperInvariant());
        return index < 0 ? PositionOrder.Length : index;
    }
}
