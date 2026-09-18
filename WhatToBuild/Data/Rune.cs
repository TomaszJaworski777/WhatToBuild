using System.Text.Json;

namespace WhatToBuild.Data;

public enum RuneTree
{
    Precision,
    Domination,
    Sorcery,
    Resolve,
    Inspiration,
    Shard,
}

public class Rune
{
    public Guid Id { get; set; }

    public int RiotId { get; set; }

    public string Key { get; set; } = "";

    public string Name { get; set; } = "";

    public string Icon { get; set; } = "";

    public RuneTree Tree { get; set; }

    public int Slot { get; set; }

    public List<Effect> Effects { get; set; } = new();

    public bool IsKeystone => Tree != RuneTree.Shard && Slot == 0;

    public bool IsShard => Tree == RuneTree.Shard;

    public override string ToString() => Name;
}

public class RuneRepository
{
    public const string FolderName = "Runes";

    private readonly Dictionary<Guid, Rune> _byId;
    private readonly Dictionary<int, Rune> _byRiotId;

    public RuneRepository(IEnumerable<Rune> runes)
    {
        var list = runes.ToList();
        _byId = list.ToDictionary(r => r.Id);
        _byRiotId = list.ToDictionary(r => r.RiotId);
    }

    public IReadOnlyCollection<Rune> All => _byId.Values;

    public int Count => _byId.Count;

    public Rune? ById(Guid id) => _byId.GetValueOrDefault(id);

    public Rune? ByRiotId(int riotId) => _byRiotId.GetValueOrDefault(riotId);

    public IEnumerable<Rune> InTree(RuneTree tree) => All.Where(r => r.Tree == tree);

    public IEnumerable<Rune> Keystones => All.Where(r => r.IsKeystone);

    public IEnumerable<Rune> Shards => All.Where(r => r.IsShard);

    public static RuneRepository Load(string dataRoot)
    {
        var runes = Directory
            .EnumerateFiles(Path.Combine(dataRoot, FolderName), "*.json")
            .Select(Read)
            .ToList();

        return new RuneRepository(runes);
    }

    private static Rune Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Rune>(File.ReadAllText(path), GameDataJson.Options)
                   ?? throw new InvalidDataException("empty file");
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a valid rune: {ex.Message}", ex);
        }
    }
}
