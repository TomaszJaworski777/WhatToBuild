using System.Text.Json;

namespace WhatToBuild.Data;

public class ChampionRepository
{
    public const string FolderName = "Champions";

    private readonly Dictionary<Guid, Champion> _byId;
    private readonly Dictionary<int, Champion> _byRiotId;
    private readonly Dictionary<string, Champion> _byInternalName;

    public ChampionRepository(IEnumerable<Champion> champions)
    {
        var list = champions.ToList();
        _byId = list.ToDictionary(c => c.Id);
        _byRiotId = list.ToDictionary(c => c.RiotId);
        _byInternalName = list.ToDictionary(c => c.InternalName, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<Champion> All => _byId.Values;

    public int Count => _byId.Count;

    public Champion? ById(Guid id) => _byId.GetValueOrDefault(id);

    public Champion? ByRiotId(int riotId) => _byRiotId.GetValueOrDefault(riotId);

    public Champion? ByInternalName(string internalName) => _byInternalName.GetValueOrDefault(internalName);

    public static ChampionRepository Load(string dataRoot)
    {
        var champions = Directory
            .EnumerateFiles(Path.Combine(dataRoot, FolderName), "*.json")
            .Select(Read)
            .ToList();

        return new ChampionRepository(champions);
    }

    private static Champion Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Champion>(File.ReadAllText(path), GameDataJson.Options)
                   ?? throw new InvalidDataException("empty file");
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a valid champion: {ex.Message}", ex);
        }
    }
}
