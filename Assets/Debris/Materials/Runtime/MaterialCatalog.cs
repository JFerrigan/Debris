using System;
using System.Collections.Generic;
using UnityEngine;

namespace Debris.Materials;

[CreateAssetMenu(menuName = "Debris/Materials/Material Catalog")]
public sealed class MaterialCatalog : ScriptableObject
{
    [SerializeField] private MaterialDefinition[] definitions = Array.Empty<MaterialDefinition>();
    private Dictionary<string, ushort> _indices;

    public ushort IndexOf(string materialKey)
    {
        BuildIndex();
        if (!_indices.TryGetValue(materialKey, out var index))
            throw new KeyNotFoundException($"Unknown material key '{materialKey}'.");
        return index;
    }

    /// <summary>Index zero is permanently reserved for empty space in material fields.</summary>
    public MaterialDefinition DefinitionAt(ushort index) => index > 0 && index <= definitions.Length ? definitions[index - 1] : null;

    private void BuildIndex()
    {
        if (_indices != null) return;
        _indices = new Dictionary<string, ushort>(StringComparer.Ordinal);
        for (ushort i = 0; i < definitions.Length; i++)
        {
            var definition = definitions[i];
            if (definition == null || string.IsNullOrWhiteSpace(definition.MaterialKey)) continue;
            if (!_indices.TryAdd(definition.MaterialKey, (ushort)(i + 1)))
                throw new InvalidOperationException($"Duplicate material key '{definition.MaterialKey}'.");
        }
    }
}
