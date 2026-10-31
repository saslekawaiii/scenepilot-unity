using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;
namespace WorldWeaver
{
    /// <summary>A real collider and carved NavMesh obstacle control one physical gateway.</summary>
    [ExecuteAlways]
    public sealed class WorldGate : MonoBehaviour,IWorldContextSource
    {
        public WorldClock Clock;
        public GameObject Panel;
        public WorldPlace Inside,Outside;
        [Range(0,24)] public float OpenHour=6,CloseHour=20;
        public bool IsOpen=>Clock&&WorldClock.InPeriod(Clock.Hour,OpenHour,CloseHour);
        public bool ConnectionReachable {get;private set;}
        public string LastPathState {get;private set;}="Not checked";
        bool initialized,lastOpen;
        float nextCheck;
        void OnEnable(){if(Clock)Clock.Changed+=ApplyClock;ApplyClock();}
        void OnDisable(){if(Clock)Clock.Changed-=ApplyClock;}
        public void ApplyClock()
        {
            if(!Clock||!Panel)return;
            bool open=WorldClock.InPeriod(Clock.Hour,OpenHour,CloseHour);bool changed=!initialized||open!=lastOpen;lastOpen=open;initialized=true;
            if(Panel.activeSelf==open)Panel.SetActive(!open);
            if(changed)
            {
                LastPathState="Waiting for NavMesh carving update";nextCheck=Time.time+.15f;
                var events=GetComponentInParent<WorldEventJournal>();if(Application.isPlaying&&events)events.Record("gate",name+(open?": открыты.":": закрыты."));
            }
        }
        void Update()
        {
            if(!Application.isPlaying)return;ApplyClock();if(!Inside||!Outside||Time.time<nextCheck)return;nextCheck=Time.time+.25f;
            ConnectionReachable=WorldNavigation.TryPath(Inside.ArrivalPosition,Outside.ArrivalPosition,out _);LastPathState=ConnectionReachable?"Complete path":"Blocked/unreachable";
        }
        public JObject Snapshot()=>new JObject { ["gate"]=name,["open"]=IsOpen,["opens_at"]=OpenHour,["closes_at"]=CloseHour,["connection_reachable"]=ConnectionReachable,["path_state"]=LastPathState,["navigation_scope"]=Application.isPlaying?"runtime carving":"Editor snapshot; runtime carving must be checked in Play Mode" };
    }
}
