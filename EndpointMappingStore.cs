using System.Collections.Concurrent;
using System.Text.Json;

namespace NSTProxy;

public enum ResourceType
{
    Printer,
    SerialPort
}

public sealed class EndpointMapping
{
    public required string EndpointName { get; init; }
    public required ResourceType ResourceType { get; init; }
    public required string ResourceName { get; init; }
}

public sealed class EndpointMappingStore
{
    private static readonly string PersistPath =
        Path.Combine(AppContext.BaseDirectory, "endpoint-mappings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<string, EndpointMapping> _mappings
        = new(StringComparer.OrdinalIgnoreCase);

    public EndpointMappingStore()
    {
        Load();
    }

    public bool TryAdd(EndpointMapping mapping)
    {
        if (!_mappings.TryAdd(mapping.EndpointName, mapping))
            return false;
        Save();
        return true;
    }

    public bool TryRemove(string endpointName)
    {
        if (!_mappings.TryRemove(endpointName, out _))
            return false;
        Save();
        return true;
    }

    public EndpointMapping? Get(string endpointName)
        => _mappings.TryGetValue(endpointName, out var m) ? m : null;

    public IReadOnlyList<EndpointMapping> GetAll()
        => _mappings.Values.ToList().AsReadOnly();

    private void Save()
    {
        var json = JsonSerializer.Serialize(_mappings.Values.ToList(), JsonOptions);
        File.WriteAllText(PersistPath, json);
    }

    private void Load()
    {
        if (!File.Exists(PersistPath))
            return;

        try
        {
            var json = File.ReadAllText(PersistPath);
            var list = JsonSerializer.Deserialize<List<EndpointMapping>>(json);
            if (list is null) return;
            foreach (var m in list)
                _mappings.TryAdd(m.EndpointName, m);
        }
        catch
        {
            // Corrupted file — start fresh
        }
    }
}
