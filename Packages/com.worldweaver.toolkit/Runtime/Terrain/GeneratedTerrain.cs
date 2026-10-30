using UnityEngine;
namespace WorldWeaver
{
    /// <summary>Ownership marker for safe regeneration without touching unrelated scene terrain.</summary>
    public sealed class GeneratedTerrain : MonoBehaviour { public TerrainSpec Specification = new TerrainSpec(); }
}
