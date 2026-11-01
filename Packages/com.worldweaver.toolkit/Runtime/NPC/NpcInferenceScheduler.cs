using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
namespace WorldWeaver
{
    /// <summary>One NPC inference at a time. A player preempts background work and passes queued ambient turns.</summary>
    public static class NpcInferenceScheduler
    {
        sealed class Pending
        {
            public bool Ambient;
            public CancellationToken Token;
            public readonly TaskCompletionSource<Lease> Completion = new TaskCompletionSource<Lease>();
        }
        public sealed class Lease : IDisposable
        {
            internal readonly CancellationTokenSource Source;
            internal readonly bool Ambient;
            bool disposed;
            internal Lease(bool ambient, CancellationToken token) { Ambient=ambient; Source=CancellationTokenSource.CreateLinkedTokenSource(token); }
            public CancellationToken Token => Source.Token;
            public void Dispose()
            {
                lock(sync)
                {
                    if(disposed)return; disposed=true;
                    if(active==this)active=null;
                    Source.Dispose(); Dispatch();
                }
            }
        }
        static readonly object sync = new object();
        static readonly List<Pending> waiting = new List<Pending>();
        static readonly Queue<double> ambientStarts = new Queue<double>();
        static Lease active;
        /// <summary>Ambient completions, not frames or generated dialogue lines, consume this global rolling budget.</summary>
        public const int MaxAmbientRequestsPerMinute = 4;
        public static bool AmbientBudgetAvailable
        {
            get { lock(sync) { TrimBudget(); return ambientStarts.Count<MaxAmbientRequestsPerMinute; } }
        }
        static double Seconds => (double)System.Diagnostics.Stopwatch.GetTimestamp()/System.Diagnostics.Stopwatch.Frequency;
        static void TrimBudget() { while(ambientStarts.Count>0&&Seconds-ambientStarts.Peek()>=60)ambientStarts.Dequeue(); }
        public static void PrioritizePlayer()
        { lock(sync) { if(active!=null&&active.Ambient)active.Source.Cancel(); } }
        public static async Task<Lease> AcquireAsync(bool ambient, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var item=new Pending { Ambient=ambient,Token=token };
            lock(sync)
            {
                if(waiting.Count(p=>p.Ambient==ambient)>=(ambient?8:4))throw new InvalidOperationException("Очередь ИИ занята. Повторите запрос позже.");
                waiting.Add(item);
                if(!ambient&&active!=null&&active.Ambient)active.Source.Cancel();
                Dispatch();
            }
            using(token.Register(()=> { lock(sync) { if(waiting.Remove(item))item.Completion.TrySetCanceled(); } }))
                return await item.Completion.Task;
        }
        static void Dispatch()
        {
            if(active!=null)return;
            foreach(var cancelled in waiting.Where(p=>p.Token.IsCancellationRequested).ToArray())
            { waiting.Remove(cancelled); cancelled.Completion.TrySetCanceled(); }
            var item=waiting.FirstOrDefault(p=>!p.Ambient)??waiting.FirstOrDefault();
            if(item==null)return;
            waiting.Remove(item);
            if(item.Ambient)
            {
                TrimBudget();
                if(ambientStarts.Count>=MaxAmbientRequestsPerMinute)
                { item.Completion.TrySetException(new InvalidOperationException("Фоновые разговоры приостановлены: лимит 4 запросов в минуту.")); Dispatch(); return; }
                ambientStarts.Enqueue(Seconds);
            }
            active=new Lease(item.Ambient,item.Token); item.Completion.TrySetResult(active);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession()
        {
            lock(sync)
            {
                var previous=active; active=null;
                foreach(var item in waiting)item.Completion.TrySetCanceled();
                waiting.Clear(); ambientStarts.Clear();
                previous?.Source.Cancel();
            }
        }
    }
}
