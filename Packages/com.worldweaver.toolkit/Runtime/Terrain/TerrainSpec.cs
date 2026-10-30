using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WorldWeaver
{
    [Serializable]
    public sealed class TerrainSpec
    {
        public int seed = 42;
        public float width = 160, length = 160, height = 40;
        public int resolution = 129;
        public float scale = 4;
        public int octaves = 4;
        public float persistence = .5f, lacunarity = 2;
        public float mountainStrength = .65f, valleyDepth = .3f, waterLevel = .22f, southSlope = .2f;
        public int smoothingPasses = 2;

        public void Validate()
        {
            if (resolution != 33 && resolution != 65 && resolution != 129 && resolution != 257 && resolution != 513)
                throw new ArgumentException("Resolution must be 33, 65, 129, 257 or 513.");
            Range(width, 32, 2048, "width"); Range(length, 32, 2048, "length"); Range(height, 4, 512, "height");
            Range(scale, .1f, 32, "scale"); Range(persistence, .1f, .9f, "persistence"); Range(lacunarity, 1, 4, "lacunarity");
            Range(mountainStrength, 0, 1, "mountainStrength"); Range(valleyDepth, 0, .8f, "valleyDepth");
            Range(waterLevel, 0, .8f, "waterLevel"); Range(southSlope, 0, 1, "southSlope");
            if (octaves < 1 || octaves > 8 || smoothingPasses < 0 || smoothingPasses > 8) throw new ArgumentException("Invalid generation passes.");
        }
        private static void Range(float value, float min, float max, string name)
        { if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max) throw new ArgumentException("Invalid " + name); }
        public static TerrainSpec Parse(string json)
        {
            var value = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            foreach (var prop in Schema()["properties"].Children<JProperty>())
            {
                var token = value[prop.Name];
                if (token == null || token.Type == JTokenType.Null) throw new ArgumentException("Missing TerrainSpec field " + prop.Name);
                bool integer = (string)prop.Value["type"] == "integer";
                if (integer ? token.Type != JTokenType.Integer : token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                    throw new ArgumentException("Invalid TerrainSpec numeric type " + prop.Name);
            }
            var spec = JsonConvert.DeserializeObject<TerrainSpec>(json, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
            spec.Validate(); return spec;
        }
        public static JObject Schema()
        {
            var properties = new JObject(); var required = new JArray();
            foreach (var field in typeof(TerrainSpec).GetFields())
            { properties[field.Name] = new JObject { ["type"] = field.FieldType == typeof(int) ? "integer" : "number" }; required.Add(field.Name); }
            return new JObject { ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false };
        }
    }
}
