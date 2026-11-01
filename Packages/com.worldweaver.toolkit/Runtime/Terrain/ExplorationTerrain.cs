using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldWeaver
{
    [Serializable] public struct ImpactCrater
    {
        public Vector2 Center;
        public float Radius, Depth;
    }

    /// <summary>Deterministic playable terrain. This is procedural geography, not a surveyed NASA heightmap.</summary>
    public static class ExplorationTerrain
    {
        public static List<ImpactCrater> Craters(int seed, int count, float width, float length)
        {
            var random = new System.Random(seed); var result = new List<ImpactCrater>();
            for (int attempt = 0; result.Count < count && attempt < 4000; attempt++)
            {
                float radius = Mathf.Min(width,length) * (.025f + .028f * (float)random.NextDouble());
                var center = new Vector2(((float)random.NextDouble()-.5f)*width*.78f, ((float)random.NextDouble()-.5f)*length*.78f);
                // Reserve the central habitat, cave approaches and a traversable exploration corridor.
                if (Mathf.Abs(center.x) < 15 + radius && Mathf.Abs(center.y) < 35 + radius) continue;
                if (result.Exists(c => Vector2.Distance(c.Center,center) < (c.Radius+radius)*1.4f)) continue;
                result.Add(new ImpactCrater { Center=center, Radius=radius, Depth=radius*.26f });
            }
            if (result.Count != count) throw new ArgumentException("Crater count does not fit these dimensions; increase the map or reduce craters.");
            return result;
        }
        public static float[,] Generate(TerrainSpec spec, IReadOnlyList<ImpactCrater> craters, bool lunar)
        {
            var heights = TerrainGenerator.Generate(spec); int n=spec.resolution;
            for (int z=0;z<n;z++) for (int x=0;x<n;x++)
            {
                float px=x/(float)(n-1)*spec.width-spec.width/2, pz=z/(float)(n-1)*spec.length-spec.length/2;
                float h=lunar ? .35f + (heights[z,x]-.3f)*.19f : heights[z,x];
                float corridor = Mathf.SmoothStep(0,1,Mathf.Clamp01((Mathf.Abs(px)-8)/10));
                float campus = Mathf.SmoothStep(0,1,Mathf.Clamp01((new Vector2(px,pz).magnitude-14)/10));
                h=Mathf.Lerp(.35f,h,Mathf.Min(corridor,campus));
                foreach (var crater in craters)
                {
                    float r=Vector2.Distance(new Vector2(px,pz),crater.Center)/crater.Radius;
                    float bowl=r<1 ? -crater.Depth*Mathf.Pow(1-r*r,2) : 0;
                    float rim=crater.Depth*.35f*Mathf.Exp(-Mathf.Pow((r-1)/.16f,2));
                    h+=(bowl+rim)/spec.height;
                }
                heights[z,x]=Mathf.Clamp01(h);
            }
            return heights;
        }
    }
}
