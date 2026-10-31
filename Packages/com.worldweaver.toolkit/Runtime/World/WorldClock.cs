using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace WorldWeaver
{
    public sealed class WorldClock : MonoBehaviour,IWorldContextSource
    {
        [Range(0,24)] public float Hour=8;
        public int Day=1;
        [Range(30,3600)] public float DaySeconds=600;
        public bool Running;
        public event Action Changed;
        public void SetTime(float hour,int day=-1)
        {
            if(float.IsNaN(hour)||float.IsInfinity(hour)||hour<0||hour>=24||day==0||day< -1)throw new ArgumentException("Hour must be 0..<24, day >=1.");
            Hour=hour;if(day>0)Day=day;Changed?.Invoke();
        }
        void Update()
        {
            if(!Running||DaySeconds<30)return;
            Hour+=Time.deltaTime*24/DaySeconds;if(Hour>=24){Day+=(int)(Hour/24);Hour%=24;}Changed?.Invoke();
        }
        public static bool InPeriod(float hour,float start,float end)=>start<=end?hour>=start&&hour<end:hour>=start||hour<end;
        public JObject Snapshot()=>new JObject { ["world_day"]=Day,["hour"]=Math.Round(Hour,2),["clock_running"]=Running,["day_seconds"]=DaySeconds };
    }
}
