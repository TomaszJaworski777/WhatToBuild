using System.Text.Json;

namespace WhatToBuild.Data;

public class ItemRepository
{
    public const string FolderName = "Items";
    public const string PatchFileName = "_patch.json";

    private readonly Dictionary<Guid, Item> _byId;
    private readonly Dictionary<int, Item> _byRiotId;

    public ItemRepository(string patch, IEnumerable<Item> items)
    {
        Patch = patch;

        var list = items.ToList();
        _byId = list.ToDictionary(i => i.Id);
        _byRiotId = list.ToDictionary(i => i.RiotId);
    }

    public string Patch { get; }

    public IReadOnlyCollection<Item> All => _byId.Values;

    public int Count => _byId.Count;

    public Item? ById(Guid id) => _byId.GetValueOrDefault(id);

    public Item? ByRiotId(int riotId) => _byRiotId.GetValueOrDefault(riotId);

    public static ItemRepository Load(string dataRoot)
    {
        var patch = "";
        var patchPath = Path.Combine(dataRoot, PatchFileName);

        if (File.Exists(patchPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(patchPath));
            patch = doc.RootElement.GetProperty("patch").GetString() ?? "";
        }

        var items = Directory
            .EnumerateFiles(Path.Combine(dataRoot, FolderName), "*.json")
            .Select(Read)
            .ToList();

        return new ItemRepository(patch, items);
    }

    private static Item Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Item>(File.ReadAllText(path), GameDataJson.Options)
                   ?? throw new InvalidDataException("empty file");
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a valid item: {ex.Message}", ex);
        }
    }
}
