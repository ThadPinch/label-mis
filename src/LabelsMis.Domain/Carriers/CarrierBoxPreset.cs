using System.Text.Json;

namespace LabelsMis.Domain.Carriers;

/// <summary>One of the shop's usual boxes: picking it on Record shipment fills in the size.</summary>
public sealed record CarrierBoxPreset(string Name, decimal LengthIn, decimal WidthIn, decimal HeightIn)
{
    public string Label => $"{Name} ({LengthIn:0.##} × {WidthIn:0.##} × {HeightIn:0.##} in)";
}

public static class CarrierBoxPresets
{
    public const int MaxPresets = 20;
    public const int MaxNameLength = 40;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>The presets a saved value holds; anything unreadable is simply no presets.</summary>
    public static IReadOnlyList<CarrierBoxPreset> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return (JsonSerializer.Deserialize<List<CarrierBoxPreset>>(json, Options) ?? [])
                .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Checks and saves a list. Rows with no name are dropped (that is how one is removed
    /// on the settings page); a named row needs all three sides.</summary>
    public static string? ToJson(IReadOnlyList<CarrierBoxPreset> presets)
    {
        var kept = new List<CarrierBoxPreset>();
        foreach (var preset in presets)
        {
            var name = (preset.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                continue;
            }

            if (name.Length > MaxNameLength)
            {
                throw new ArgumentException($"A box name can be at most {MaxNameLength} characters: \"{name}\".");
            }

            if (preset.LengthIn <= 0 || preset.WidthIn <= 0 || preset.HeightIn <= 0)
            {
                throw new ArgumentException($"Box \"{name}\" needs a length, a width and a height.");
            }

            if (kept.Any(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Two boxes are named \"{name}\".");
            }

            kept.Add(preset with { Name = name });
        }

        if (kept.Count > MaxPresets)
        {
            throw new ArgumentException($"At most {MaxPresets} box presets can be kept.");
        }

        return kept.Count == 0 ? null : JsonSerializer.Serialize(kept, Options);
    }
}
