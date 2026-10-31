using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace WorldWeaver
{
    [Serializable] public sealed class NpcRelationship { public string ActorId;public int Trust; }
    public sealed class NpcRelationships : MonoBehaviour,IWorldContextSource
    {
        public List<NpcRelationship> Relations=new List<NpcRelationship>();
        public int Trust(string actor)=>Relations.FirstOrDefault(r=>r.ActorId==actor)?.Trust??0;
        public void ChangeTrust(string actor,int amount)
        {
            if(string.IsNullOrWhiteSpace(actor)||actor.Length>160)throw new ArgumentException("Relationship needs a persistent actor id.");
            var relation=Relations.FirstOrDefault(r=>r.ActorId==actor);if(relation==null){if(Relations.Count>=32)throw new InvalidOperationException("Relationship limit exceeded.");relation=new NpcRelationship {ActorId=actor};Relations.Add(relation);}relation.Trust=Mathf.Clamp(relation.Trust+amount,-100,100);
        }
        public JObject Snapshot()=>new JObject { ["relationships"]=JArray.FromObject(Relations.Take(8)) };
    }
}
