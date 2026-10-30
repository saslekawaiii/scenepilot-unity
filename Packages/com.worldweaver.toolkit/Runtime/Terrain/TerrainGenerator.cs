using System;
using UnityEngine;

namespace WorldWeaver
{
    /// <summary>Seeded value noise, fBm, radial mountain rim, valley and synchronous thermal smoothing.</summary>
    public static class TerrainGenerator
    {
        public static float[,] Generate(TerrainSpec spec)
        {
            spec.Validate(); int n = spec.resolution; var heights = new float[n, n];
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
            {
                float u = x / (float)(n - 1), v = z / (float)(n - 1);
                float amplitude = 1, frequency = spec.scale, sum = 0, weight = 0;
                for (int o = 0; o < spec.octaves; o++)
                { sum += Noise(u * frequency, v * frequency, unchecked(spec.seed + o * 1013)) * amplitude; weight += amplitude; amplitude *= spec.persistence; frequency *= spec.lacunarity; }
                float dx = u - .5f, dz = v - .5f;
                float radius = Mathf.Sqrt(dx * dx + dz * dz) * 1.41421356f;
                float rim = Mathf.SmoothStep(.05f, 1, Mathf.Clamp01((radius - .3f) / .5f));
                float valley = spec.valleyDepth * Mathf.Exp(-(dx * dx / .035f + dz * dz / .08f));
                float southern = 1 - spec.southSlope * Mathf.Clamp01((.5f - v) * 2);
                heights[z, x] = Mathf.Clamp01(.20f + .24f * sum / weight + spec.mountainStrength * rim * southern - valley);
            }
            for (int pass = 0; pass < spec.smoothingPasses; pass++)
            {
                var next = (float[,])heights.Clone();
                for (int z = 1; z < n - 1; z++) for (int x = 1; x < n - 1; x++)
                {
                    float average = (heights[z - 1, x] + heights[z + 1, x] + heights[z, x - 1] + heights[z, x + 1]) * .25f;
                    next[z, x] = Mathf.Lerp(heights[z, x], average, .35f);
                }
                heights = next;
            }
            return heights;
        }
        private static float Noise(float x, float z, int seed)
        {
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z); float tx = x - ix, tz = z - iz;
            tx = tx * tx * (3 - 2 * tx); tz = tz * tz * (3 - 2 * tz);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iz, seed), Hash(ix + 1, iz, seed), tx), Mathf.Lerp(Hash(ix, iz + 1, seed), Hash(ix + 1, iz + 1, seed), tx), tz);
        }
        private static float Hash(int x, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + z * 668265263) ^ (uint)seed;
                h = (h ^ (h >> 13)) * 1274126177; h ^= h >> 16;
                return (h & 0x00ffffff) / 16777215f;
            }
        }
    }
}
