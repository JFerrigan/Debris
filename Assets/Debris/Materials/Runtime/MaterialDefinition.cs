using UnityEngine;

namespace Debris.Materials
{

[CreateAssetMenu(menuName = "Debris/Materials/Material Definition")]
public sealed class MaterialDefinition : ScriptableObject
{
    [SerializeField] private string materialKey;
    [SerializeField] private Color baseColor = Color.gray;
    [SerializeField] private Color shadowColor = Color.black;
    [SerializeField] private Color emissiveColor = Color.black;
    [SerializeField, Min(0)] private float emissiveIntensity;
    [SerializeField, Min(0.01f)] private float durability = 1f;
    [SerializeField, Min(0.01f)] private float density = 1f;
    [SerializeField, Min(0)] private int unitValue;

    public string MaterialKey => materialKey;
    public Color BaseColor => baseColor;
    public Color ShadowColor => shadowColor;
    public Color EmissiveColor => emissiveColor;
    public float EmissiveIntensity => emissiveIntensity;
    public float Durability => durability;
    public float Density => density;
    public int UnitValue => unitValue;

    public void Configure(string key, Color color, float hardness, float mass, int value, Color emission)
    {
        materialKey = key; baseColor = color; shadowColor = color * .55f;
        durability = hardness; density = mass; unitValue = value;
        emissiveColor = emission; emissiveIntensity = emission.maxColorComponent > 0 ? 1 : 0;
        Validate();
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(materialKey) || materialKey != materialKey.Trim().ToLowerInvariant()
            || !(durability > 0) || float.IsInfinity(durability) || !(density > 0)
            || float.IsInfinity(density) || unitValue < 0)
            throw new System.InvalidOperationException("Invalid material definition: " + materialKey);
    }

    private void OnValidate()
    {
        materialKey = materialKey?.Trim().ToLowerInvariant();
    }
}

}
