using System.Collections.Generic;
using UnityEngine;

namespace WorldWeaver
{
    public sealed class WorldEnvironment : MonoBehaviour
    {
        public string Profile = "custom";
        public Terrain Terrain;
        public List<ImpactCrater> Craters = new List<ImpactCrater>();
        public string HeightmapSource = "Procedural seeded terrain; not measured lunar topography";
    }
}
