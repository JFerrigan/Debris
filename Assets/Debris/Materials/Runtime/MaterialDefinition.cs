using UnityEngine;

namespace Debris.Materials;

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

    private void OnValidate()
    {
        materialKey = materialKey?.Trim().ToLowerInvariant();
    }
}
