using System;
using UnityEngine;

namespace Debris.Sites
{

[Serializable]
public struct MaterialBand
{
    public string MaterialKey;
    [Range(0, 1)] public float Weight;
}

[CreateAssetMenu(menuName = "Debris/Sites/Asteroid Profile")]
public sealed class AsteroidProfile : ScriptableObject
{
    [SerializeField, Min(8)] private int minimumRadiusCells = 48;
    [SerializeField, Min(8)] private int maximumRadiusCells = 96;
    [SerializeField] private MaterialBand[] materials = Array.Empty<MaterialBand>();

    public int MinimumRadiusCells => minimumRadiusCells;
    public int MaximumRadiusCells => maximumRadiusCells;
    public MaterialBand[] Materials => materials;

    public void Configure(int minimum, int maximum, MaterialBand[] bands)
    { minimumRadiusCells = minimum; maximumRadiusCells = maximum; materials = (MaterialBand[])bands.Clone(); }

    public void Validate(Debris.Materials.MaterialCatalog catalog)
    {
        if (minimumRadiusCells < 8 || maximumRadiusCells < minimumRadiusCells || maximumRadiusCells > 100000)
            throw new InvalidOperationException("Invalid asteroid radius range.");
        float total = 0;
        foreach (var band in materials)
        {
            catalog.IndexOf(band.MaterialKey);
            if (band.Weight < 0 || float.IsNaN(band.Weight) || float.IsInfinity(band.Weight))
                throw new InvalidOperationException("Invalid material weight.");
            total += band.Weight;
        }
        if (!(total > 0) || float.IsInfinity(total)) throw new InvalidOperationException("Positive material weights required.");
    }
}

}
