using System;

namespace WorldWeaver
{
    /// <summary>Game rules independent of rendering, input and model responses.</summary>
    [Serializable] public sealed class SurvivalState
    {
        public float Oxygen=100, Health=100, Exposure;
        public int Samples;
        public bool Completed;
        public bool Alive => Health>0;
        public void Tick(float seconds, bool pressurized, bool covered, float oxygenSeconds=180)
        {
            if(seconds<=0 || float.IsNaN(seconds) || float.IsInfinity(seconds) || !Alive)return;
            Oxygen=Clamp(Oxygen+seconds*(pressurized?12:-100/Math.Max(10,oxygenSeconds)),0,100);
            Exposure=Clamp(Exposure+seconds*(pressurized||covered?-8:.18f),0,100);
            Health=Clamp(Health+seconds*(pressurized?5:Oxygen<=0?-12:Exposure>=100?-4:0),0,100);
            if(pressurized && Samples>=3)Completed=true;
        }
        public void Supply(float oxygen) { if(Alive)Oxygen=Clamp(Oxygen+oxygen,0,100); }
        static float Clamp(float v,float lo,float hi)=>Math.Max(lo,Math.Min(hi,v));
    }
}
