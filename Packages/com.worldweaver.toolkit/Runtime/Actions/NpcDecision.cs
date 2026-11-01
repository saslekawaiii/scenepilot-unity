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
            var allowed=(allowedActions??new[]{"Talk","Wait","GiveItem","TakeItem","MoveTo","Follow","Flee","Interact"}).Distinct().ToArray();
            var branches=new JArray();
            foreach(string type in allowed)
            {
                bool noTarget=type=="Talk"||type=="Wait",transfer=type=="GiveItem"||type=="TakeItem",known=transfer||new[]{"Talk","Wait","MoveTo","Follow","Flee","Interact"}.Contains(type);
                var properties=new JObject {
                    ["type"]=new JObject { ["type"]="string",["enum"]=new JArray(type) },
                    ["target"]=noTarget?new JObject { ["type"]="string",["enum"]=new JArray("") }:new JObject { ["type"]="string",["maxLength"]=100 },
                    ["item"]=!known?new JObject { ["type"]="string",["maxLength"]=100 }:transfer?new JObject { ["type"]="string",["minLength"]=1,["maxLength"]=100 }:new JObject { ["type"]="string",["enum"]=new JArray("") },
                    ["amount"]=!known?new JObject { ["type"]="integer",["minimum"]=0,["maximum"]=10000 }:transfer?new JObject { ["type"]="integer",["minimum"]=1,["maximum"]=10000 }:new JObject { ["type"]="integer",["enum"]=new JArray(0) }
                };
                branches.Add(new JObject { ["type"]="object",["additionalProperties"]=false,["required"]=new JArray("type","target","item","amount"),["properties"]=properties });
            }
            var items=branches.Count>0?new JObject { ["anyOf"]=branches }:new JObject { ["type"]="object",["additionalProperties"]=false,["properties"]=new JObject() };
            return new JObject { ["type"]="object",["additionalProperties"]=false,["required"]=new JArray("speech","actions"),["properties"]=new JObject {
                ["speech"]=new JObject { ["type"]="string",["maxLength"]=2000 },
                ["actions"]=new JObject { ["type"]="array",["maxItems"]=allowed.Length==0?0:4,["items"]=items }
            } };
        }
    }
}
