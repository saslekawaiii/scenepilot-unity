using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WorldWeaver
{
    [Serializable]
    public sealed class ActionCommand
    {
        public string type, target, item;
        public int amount;
    }
    [Serializable]
    public sealed class NpcDecision
    {
        public string speech;
        public ActionCommand[] actions;
        public static NpcDecision Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 32000) throw new ArgumentException("Empty or oversized decision.");
            var obj = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            Exact(obj, new[] { "speech", "actions" });
            if (obj["speech"].Type != JTokenType.String || !(obj["actions"] is JArray array) || array.Count > 4 || ((string)obj["speech"]).Length > 2000)
                throw new ArgumentException("Invalid decision structure.");
            foreach (var action in array)
            {
                if (!(action is JObject a)) throw new ArgumentException("Action must be an object.");
                Exact(a, new[] { "type", "target", "item", "amount" });
                foreach (string field in new[] { "type", "target", "item" })
                    if (a[field].Type != JTokenType.String || ((string)a[field]).Length > 100) throw new ArgumentException("Invalid action string.");
                if (a["amount"].Type != JTokenType.Integer || (long)a["amount"] > 10000 || (long)a["amount"] < 0) throw new ArgumentException("Invalid action amount.");
            }
            return obj.ToObject<NpcDecision>();
        }
        private static void Exact(JObject value, IEnumerable<string> fields)
        {
            var expected = new HashSet<string>(fields, StringComparer.Ordinal);
            if (!expected.SetEquals(value.Properties().Select(p => p.Name))) throw new ArgumentException("Decision fields do not match the contract.");
        }
        public static JObject Schema(IEnumerable<string> allowedActions = null)
        {
            var schema = JObject.Parse(@"{
              'type':'object','additionalProperties':false,'required':['speech','actions'],
              'properties':{'speech':{'type':'string'},'actions':{'type':'array','items':{
                'type':'object','additionalProperties':false,'required':['type','target','item','amount'],
                'properties':{'type':{'type':'string'},'target':{'type':'string'},'item':{'type':'string'},'amount':{'type':'integer'}}
              }}}
            }");
            var allowed = (allowedActions ?? new[] { "Talk", "Wait", "GiveItem", "TakeItem", "MoveTo", "Follow", "Flee", "Interact" }).Distinct().ToArray();
            schema["properties"]["actions"]["items"]["properties"]["type"]["enum"] = JArray.FromObject(allowed);
            return schema;
        }
    }
}
