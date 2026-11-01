using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;
namespace WorldWeaver
{
    /// <summary>One bounded daily routine, with verified complete paths and explicit blocked/fallback outcomes.</summary>
    public sealed class NpcDailyRoutine : MonoBehaviour,IWorldContextSource
    {
        public WorldClock Clock;
        public WorldPlace DayPlace,NightPlace,BlockedFallback;
        public float StartHour=6,EndHour=20;
        public string Status {get;private set;}="Not started";
        public bool GoalReached {get;private set;}
        public WorldPlace CurrentGoal {get;private set;}
        public WorldPlace ActualDestination {get;private set;}
        public string BlockedReason {get;private set;}
        public string LastPeriod;
        NavMeshAgent agent;
        float nextCheck;
        string pendingMemory;
        bool restoring;
        public void ResetAfterLoad()
        {
            LastPeriod=Clock&&WorldClock.InPeriod(Clock.Hour,StartHour,EndHour)?"day":"night";CurrentGoal=LastPeriod=="day"?DayPlace:NightPlace;ActualDestination=null;GoalReached=false;BlockedReason=null;Status="Restored; path verification pending";pendingMemory=null;restoring=CurrentGoal&&Vector3.Distance(transform.position,CurrentGoal.ArrivalPosition)<.6f;nextCheck=0;
        }
        void OnEnable(){agent=GetComponent<NavMeshAgent>();nextCheck=0;}
        void OnDisable(){if(agent&&agent.enabled&&agent.isOnNavMesh)agent.ResetPath();}
        void Update()
        {
            if(!Clock||!agent||!agent.enabled||!agent.isOnNavMesh||Time.time<nextCheck)return;nextCheck=Time.time+.4f;
            var npc=GetComponent<IntelligentNPC>();if(npc&&(npc.Busy||npc.PlayerConversationActive||(npc.GetComponent<NpcSocialAutonomy>()?.HoldsMovement??false)))return;
            if(!string.IsNullOrEmpty(pendingMemory)&&npc){npc.RememberEvent(pendingMemory);pendingMemory=null;}
            string period=WorldClock.InPeriod(Clock.Hour,StartHour,EndHour)?"day":"night";var goal=period=="day"?DayPlace:NightPlace;
            if(!goal){Status="Missing named destination";GoalReached=false;return;}
            if(period!=LastPeriod||CurrentGoal!=goal)
            {
                LastPeriod=period;CurrentGoal=goal;GoalReached=false;ActualDestination=null;pendingMemory="Наступил "+(period=="day"?"дневной":"ночной")+" период. Текущая цель: "+goal.Key+". Прибытие ещё не подтверждено.";
            }
            if(Vector3.Distance(transform.position,CurrentGoal.ArrivalPosition)<=Mathf.Max(.5f,agent.stoppingDistance+.2f))
            {
                if(!GoalReached&&!restoring){pendingMemory="Прибыл в "+CurrentGoal.Key+" по расписанию.";GetComponentInParent<WorldEventJournal>()?.Record("npc_arrived",name+" прибыл: "+CurrentGoal.Key,GetComponent<WorldPlace>()?.PersistentId??"");}
                restoring=false;GoalReached=true;Status="Arrived at primary goal";BlockedReason=null;ActualDestination=CurrentGoal;if(agent.hasPath)agent.ResetPath();return;
            }
            GoalReached=false;
            if(WorldNavigation.TryPath(transform.position,CurrentGoal.ArrivalPosition,out var path))
            {
                BlockedReason=null;Status="Moving to primary goal";ActualDestination=CurrentGoal;agent.isStopped=false;agent.SetPath(path);return;
            }
            BlockedReason="Primary destination is unreachable";
            if(BlockedFallback&&WorldNavigation.TryPath(transform.position,BlockedFallback.ArrivalPosition,out var fallback))
            {ActualDestination=BlockedFallback;Status=Vector3.Distance(transform.position,BlockedFallback.ArrivalPosition)<.6f?"Primary blocked; waiting at fallback":"Primary blocked; moving to fallback";agent.isStopped=false;agent.SetPath(fallback);}
            else {ActualDestination=null;Status="Blocked; waiting";agent.ResetPath();}
        }
        public JObject Snapshot()=>new JObject { ["routine"]=LastPeriod,["primary_goal"]=CurrentGoal?CurrentGoal.Key:"",["actual_destination"]=ActualDestination?ActualDestination.Key:"",["goal_reached"]=GoalReached,["status"]=Status,["blocked_reason"]=BlockedReason };
    }
}
