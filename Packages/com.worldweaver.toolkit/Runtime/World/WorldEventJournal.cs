using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace WorldWeaver
{
    [Serializable] public sealed class WorldEventRecord { public string Id,Kind,Actor,Text;public int Day;public float Hour; }
    /// <summary>Only public, actually executed world events belong here; NPC conversations remain scoped RAG memory.</summary>
    public sealed class WorldEventJournal : MonoBehaviour,IWorldContextSource
    {
        [NonSerialized] public bool SuppressRecording;
        public List<WorldEventRecord> Events=new List<WorldEventRecord>();
        public void Record(string kind,string text,string actor="")
        {
            if(SuppressRecording)return;
            var clock=GetComponent<WorldClock>();Events.Add(new WorldEventRecord {Id=Guid.NewGuid().ToString("N"),Kind=kind,Actor=actor,Text=(text??"").Substring(0,Math.Min(500,(text??"").Length)),Day=clock?clock.Day:1,Hour=clock?clock.Hour:0});while(Events.Count>128)Events.RemoveAt(0);
        }
        public JObject Snapshot()=>new JObject { ["public_world_events"]=JArray.FromObject(Events.Skip(Math.Max(0,Events.Count-4))) };
    }
}
