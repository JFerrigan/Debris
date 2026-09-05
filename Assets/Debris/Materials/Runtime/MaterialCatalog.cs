using System;
using System.Collections.Generic;
using UnityEngine;

namespace Debris.Materials
{

[CreateAssetMenu(menuName = "Debris/Materials/Material Catalog")]
public sealed class MaterialCatalog : ScriptableObject
{
    [SerializeField] private MaterialDefinition[] definitions = Array.Empty<MaterialDefinition>();
    private Dictionary<string, ushort> _indices;
    public int Count => definitions.Length;

    public void Configure(MaterialDefinition[] values)
    {
        definitions = (MaterialDefinition[])values.Clone(); _indices = null; Validate();
    }

    private void OnValidate() => _indices = null;
    public void Validate() { _indices = null; BuildIndex(); }

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
        if (definitions.Length == 0 || definitions.Length > ushort.MaxValue)
            throw new InvalidOperationException("Catalog needs 1..65535 materials.");
        var indices = new Dictionary<string, ushort>(StringComparer.Ordinal);
        for (int i = 0; i < definitions.Length; i++)
        {
            var definition = definitions[i];
            if (definition == null) throw new InvalidOperationException("Catalog contains a null material.");
            definition.Validate();
            if (!indices.TryAdd(definition.MaterialKey, (ushort)(i + 1)))
                throw new InvalidOperationException($"Duplicate material key '{definition.MaterialKey}'.");
        }
        _indices = indices;
    }
}

}
