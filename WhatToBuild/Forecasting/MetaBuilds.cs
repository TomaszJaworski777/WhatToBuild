using WhatToBuild.Data;

namespace WhatToBuild.Forecasting;

public sealed class MetaBuilds
{
    private readonly Dictionary<Guid, IReadOnlyList<Item>> _builds;

    public MetaBuilds(ChampionRepository champions, ItemRepository items)
    {
        _builds = champions.All.ToDictionary(
            c => c.Id,
            c =>
            {
                if (c.MetaBuild.Count == 0)
                {
                    throw new InvalidDataException($"{c.Name} has no metaBuild.");
                }

                return (IReadOnlyList<Item>)c.MetaBuild
                    .Select(id => items.ById(id) ?? throw new InvalidDataException($"{c.Name}'s metaBuild lists unknown item {id}."))
                    .ToList();
            });
    }

    public int Count => _builds.Count;

    public IReadOnlyList<Item> For(Champion champion) => _builds.GetValueOrDefault(champion.Id) ?? [];
}
