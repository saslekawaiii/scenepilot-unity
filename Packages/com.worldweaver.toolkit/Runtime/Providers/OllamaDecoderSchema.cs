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
                foreach(var property in value.Properties())RemoveStringRepetitions(property.Value);
            }
            else if(node is JArray array)foreach(var child in array)RemoveStringRepetitions(child);
        }
    }
}
