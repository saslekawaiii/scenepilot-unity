using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Small closed-schema validator shared by constructive tools; structured decoding is not a validator.</summary>
    internal static class ToolArgumentValidation
    {
        internal static JObject Obj(JObject properties,params string[] required)=>new JObject{["type"]="object",["properties"]=properties,["additionalProperties"]=false,["required"]=new JArray(required)};
        internal static JObject Text(int max=160)=>new JObject{["type"]="string",["maxLength"]=max};
        internal static JObject Number(double min,double max)=>new JObject{["type"]="number",["minimum"]=min,["maximum"]=max};
        internal static JObject Integer(int min,int max)=>new JObject{["type"]="integer",["minimum"]=min,["maximum"]=max};
        internal static JObject Enum(params string[] choices)=>new JObject{["type"]="string",["enum"]=new JArray(choices)};
        internal static JObject Bool()=>new JObject{["type"]="boolean"};
        internal static JObject Array(JObject item,int min,int max)=>new JObject{["type"]="array",["items"]=item,["minItems"]=min,["maxItems"]=max};
        internal static JObject Vector(int n=3,double min=-10000,double max=10000)=>Array(Number(min,max),n,n);
        internal static void Validate(JToken value,JObject schema,string path="args")
        {
            if(schema["anyOf"] is JArray variants)
            {
                foreach(JObject variant in variants)try{Validate(value,variant,path);return;}catch(ArgumentException){}
                throw new ArgumentException("Value does not match an allowed type: "+path);
            }
            string type=(string)schema["type"];
            bool valid=type=="object"?value is JObject:type=="array"?value is JArray:type=="string"?value?.Type==JTokenType.String:type=="boolean"?value?.Type==JTokenType.Boolean:type=="integer"?value?.Type==JTokenType.Integer:value?.Type==JTokenType.Integer||value?.Type==JTokenType.Float;
            if(!valid)throw new ArgumentException("Wrong type: "+path);
            if(schema["enum"] is JArray choices&&!choices.Any(c=>JToken.DeepEquals(c,value)))throw new ArgumentException("Unknown value: "+path);
            if(value is JObject obj)
            {
                var props=(JObject)schema["properties"];
                foreach(string key in schema["required"]??new JArray())if(obj[key]==null)throw new ArgumentException("Missing "+path+"."+key);
                foreach(var p in obj.Properties()){if(props[p.Name]==null)throw new ArgumentException("Unknown "+path+"."+p.Name);Validate(p.Value,(JObject)props[p.Name],path+"."+p.Name);}
            }
            else if(value is JArray array)
            {
                if(array.Count<((int?)schema["minItems"]??0)||array.Count>((int?)schema["maxItems"]??int.MaxValue))throw new ArgumentException("Array size: "+path);
                foreach(var item in array)Validate(item,(JObject)schema["items"],path+"[]");
            }
            else if(type=="number"||type=="integer")
            {
                double n=(double)value;if(double.IsNaN(n)||double.IsInfinity(n)||n<((double?)schema["minimum"]??double.MinValue)||n>((double?)schema["maximum"]??double.MaxValue))throw new ArgumentException("Number out of range: "+path);
            }
            else if(type=="string"&&(((string)value).Length>((int?)schema["maxLength"]??int.MaxValue)||((string)value).Length<((int?)schema["minLength"]??0)))throw new ArgumentException("Text length out of range: "+path);
        }
    }
}
