using Snap.Hutao.Remastered.Service.AI.Models;
using Snap.Hutao.Remastered.Service.AI.Settings;
using System.Collections.Generic;
using System.Linq;

namespace Snap.Hutao.Remastered.Service.AI;

internal static class ModelProfileMerger
{
    public static ObservableSettingsCollection<ModelProfile, Guid> CreateMerged(ObservableSettingsCollection<ModelProfile, Guid> current, IReadOnlyList<ModelProfile> fetched)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(fetched);

        Dictionary<string, ModelProfile> existing = new(StringComparer.Ordinal);
        foreach (ModelProfile model in current)
        {
            if (!string.IsNullOrWhiteSpace(model.ModelId))
            {
                existing.TryAdd(model.ModelId.Trim(), model);
            }
        }

        List<ModelProfile> models = new(fetched.Count + current.Count);
        HashSet<string> seen = new(StringComparer.Ordinal);
        HashSet<ModelProfile> retained = [];
        foreach (ModelProfile model in fetched)
        {
            string id = model.ModelId?.Trim() ?? string.Empty;
            if (id.Length == 0 || !seen.Add(id))
            {
                continue;
            }
            ModelProfile entry = existing.TryGetValue(id, out ModelProfile? configured)
                ? configured
                : new() { ModelId = id, Name = string.IsNullOrWhiteSpace(model.Name) ? id : model.Name };
            models.Add(entry);
            retained.Add(entry);
        }

        // Keep configured profiles and conversation references, but discard empty
        // placeholders. Preparing the new source does not mutate observed models.
        foreach (ModelProfile model in current)
        {
            if (!retained.Contains(model) && (!string.IsNullOrWhiteSpace(model.ModelId) || !string.IsNullOrWhiteSpace(model.Name)))
            {
                models.Add(model);
            }
        }

        ObservableSettingsCollection<ModelProfile, Guid> result = new(models);
        ModelProfile? selected = current.CurrentItem;
        result.CurrentItem = selected is not null && !string.IsNullOrWhiteSpace(selected.ModelId) && models.Contains(selected)
            ? selected
            : models.FirstOrDefault();
        return result;
    }
}
