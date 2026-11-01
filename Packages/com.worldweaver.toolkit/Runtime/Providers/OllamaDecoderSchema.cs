using Newtonsoft.Json.Linq;
namespace WorldWeaver
{
    /// <summary>Ollama grammar dialect only. Host parsing and action validation retain their original bounds.</summary>
    public static class OllamaDecoderSchema
    {
        public static JObject FromHost(JObject host)
        {
            if(host==null)return null;
            var decoder=(JObject)host.DeepClone();RemoveStringRepetitions(decoder);return decoder;
        }
        static void RemoveStringRepetitions(JToken node)
        {
            if(node is JObject value)
            {
                if(value["type"]?.Type==JTokenType.String&&(string)value["type"]=="string")
                { value.Remove("minLength");value.Remove("maxLength"); }
                // Native llama.cpp grammar rejects huge bounded repetitions (observed with
                // create_mesh's 2048 vertices / 12000 indices on Ollama 0.32.6). Keep vector
                // arity and small arrays constrained; the unchanged host schema checks all bounds.
                if(value["type"]?.Type==JTokenType.String&&(string)value["type"]=="array"&&((int?)value["maxItems"]??0)>256)value.Remove("maxItems");
                foreach(var property in value.Properties())RemoveStringRepetitions(property.Value);
            }
            else if(node is JArray array)foreach(var child in array)RemoveStringRepetitions(child);
        }
    }
}
