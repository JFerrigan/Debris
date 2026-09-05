using System;
using UnityEngine;

namespace Debris.Sites;

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
}
