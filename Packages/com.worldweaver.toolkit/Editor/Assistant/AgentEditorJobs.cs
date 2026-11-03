using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    [Serializable] public sealed class AgentEditorJob
    {
        public string Id, Kind, Status, Filter, RunGuid, ResultPath, Error;
        public string StartedUtc = DateTime.UtcNow.ToString("o"), FinishedUtc;
        public int Passed, Failed, Skipped;
        public bool Success;
        public List<string> Failures = new List<string>();
        public string StableTarget, Measurement="local Y rotation sampled in actual Play Mode";
        public double RequestedSeconds, DegreesPerSecond, StartedGameTime, LastGameTime, ActualSeconds, ActualDegrees, ExpectedDegrees, ToleranceDegrees, OffAxisDegrees;
        public float LastRotationX,LastRotationY,LastRotationZ,LastRotationW;
        public int Samples;
        public bool MeasurementStarted;
        public bool TestCancelled;
        public string ResultReceivedUtc, SceneScopeId;
    }
    /// <summary>Actual Unity test/build jobs with durable result files. No arbitrary shell or method execution.</summary>
    [InitializeOnLoad]
    public static partial class AgentEditorJobs
    {
        const string Folder="UserSettings/WorldWeaverAgentJobs";
        const string Active="UserSettings/WorldWeaverAgentJobs/active.json";
        static TestRunnerApi api;
        static readonly Callbacks callbacks=new Callbacks();
        static readonly List<JObject> console=new List<JObject>();
        // Unity's result callback precedes ExitPlayMode/RestoreSceneSetup/DeleteBootstrapScene.
        // Framework 1.6 exposes no public completion signal; fail closed if this pinned probe changes.
        static readonly MethodInfo isTestRunRunning=typeof(TestRunnerApi).GetMethod("IsRunning",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(string)},null);
        public static event Action Changed;
        static AgentEditorJobs()
        {
            Directory.CreateDirectory(Folder);
            api=ScriptableObject.CreateInstance<TestRunnerApi>();api.hideFlags=HideFlags.HideAndDontSave;api.RegisterCallbacks(callbacks);
            Application.logMessageReceived+=OnLog;
            EditorApplication.playModeStateChanged+=RotationPlayModeChanged;
            EditorApplication.update+=TickRotation;
            EditorApplication.update+=TickTestCompletion;
            EditorApplication.playModeStateChanged+=PlayerCheckModeChanged;
            EditorApplication.update+=TickPlayerCheck;
            EditorApplication.delayCall+=Recover;
        }
        static void Recover()
        {
            var job=LoadActive();
            if(job?.Kind=="rotation"&&ActiveStatus(job.Status))
            {
                if(SessionState.GetString("WorldWeaver.RotationJob","")!=job.Id) {job.Status="interrupted";job.Success=false;job.Error="Editor restarted before rotation measurement finished.";Save(job);return;}
                if(job.Status=="exiting_play"&&!EditorApplication.isPlayingOrWillChangePlaymode) {job.Status=job.Success?"completed":"failed";job.FinishedUtc=DateTime.UtcNow.ToString("o");Save(job);WriteRotationResult(job);SessionState.EraseString("WorldWeaver.RotationJob");return;}
                if(job.Status=="entering_play"&&!EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.EnterPlaymode();
            }
            if(job!=null&&(ActiveStatus(job.Status))&&DateTime.TryParse(job.StartedUtc,out var started)&&DateTime.UtcNow-started.ToUniversalTime()>TimeSpan.FromMinutes(15))
            {job.Status="interrupted";job.Error="Job exceeded its recovery window. No success is inferred from missing callbacks.";Save(job);}
        }
        static void OnLog(string condition,string stack,LogType type)
        {
            string text=ChatHistoryStore.Redact(condition??"");
            console.Add(new JObject{["utc"]=DateTime.UtcNow.ToString("o"),["type"]=type.ToString(),["message"]=text.Substring(0,Math.Min(1800,text.Length)),["stack"]=ChatHistoryStore.Redact((stack??"").Substring(0,Math.Min(1200,(stack??"").Length)))});
            if(console.Count>100)console.RemoveAt(0);
        }
        public static JObject ConsoleSnapshot(int limit=20)=>new JObject{["entries"]=new JArray(console.Skip(Math.Max(0,console.Count-Mathf.Clamp(limit,1,100)))),["scope"]="Messages observed since this editor domain loaded; compiler-specific evidence is also in ScriptDrafts.CompilationContext.",["compilation"]=ScriptDrafts.CompilationContext()};
        static string PathFor(string id)
        {if(!ChatHistoryStore.ValidId(id))throw new ArgumentException("Invalid job id.");return Folder+"/"+id+".json";}
        static void Atomic(string path,string text)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));string tmp=path+".tmp";File.WriteAllText(tmp,text);
            if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
        }
        static void Save(AgentEditorJob job)
        {
            string text=ChatHistoryStore.Redact(JsonConvert.SerializeObject(job,Formatting.Indented));Atomic(PathFor(job.Id),text);Atomic(Active,text);Changed?.Invoke();
        }
        public static AgentEditorJob Load(string id)
        {string path=PathFor(id);return File.Exists(path)?JsonConvert.DeserializeObject<AgentEditorJob>(File.ReadAllText(path)):null;}
        static AgentEditorJob LoadActive()
        {try{return File.Exists(Active)?JsonConvert.DeserializeObject<AgentEditorJob>(File.ReadAllText(Active)):null;}catch(IOException){return null;}catch(JsonException){return null;}}
        static bool ActiveStatus(string status)=>status=="queued"||status=="running"||status=="entering_play"||status=="exiting_play"||status=="cancelling"||status=="finishing";
        public static bool Busy { get { var job=LoadActive();return job!=null&&(ActiveStatus(job.Status)); } }
        public static JObject Poll(string id)
        {var job=Load(id)??throw new ArgumentException("Job not found.");return JObject.FromObject(job);}
        public static AgentEditorJob RunTests(string mode,string filter)
        {
            if(Busy||EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Finish the active editor job/compilation/Play Mode first.");
            if(mode!="edit"&&mode!="play")throw new ArgumentException("Test mode must be edit or play.");
            if(string.IsNullOrWhiteSpace(filter)||filter.Length>250||filter.Any(char.IsControl))throw new ArgumentException("Specify an explicit test fixture/name filter (<=250 characters).");
            if(isTestRunRunning==null)throw new NotSupportedException("The installed Test Framework does not expose the verified run-completion probe. No tests started.");
            var job=new AgentEditorJob{Id=Guid.NewGuid().ToString("N"),Kind="tests:"+mode,Status="queued",Filter=filter};job.ResultPath=Folder+"/"+job.Id+".xml";Save(job);
            EditorApplication.delayCall+=()=>
            {
                try
                {
                    var queued=Load(job.Id);if(queued==null||queued.Status!="queued")return;job=queued;
                    job.SceneScopeId=AgentCheckpoints.BeginTestSceneScope();
                    job.Status="running";Save(job);
                    job.RunGuid=api.Execute(new ExecutionSettings(new Filter{testMode=mode=="edit"?TestMode.EditMode:TestMode.PlayMode,groupNames=new[]{filter}}));
                    var latest=Load(job.Id);if(latest!=null&&(latest.Status=="running"||latest.Status=="finishing")){latest.RunGuid=job.RunGuid;Save(latest);}
                }
                catch(Exception e)
                {
                    job.Status="failed";job.Error=ChatHistoryStore.Redact(e.Message);
                    if(!string.IsNullOrEmpty(job.SceneScopeId))try { AgentCheckpoints.EndTestSceneScope(job.SceneScopeId); }catch(Exception restore){job.Error+=" Scene recovery: "+ChatHistoryStore.Redact(restore.Message);}
                    job.FinishedUtc=DateTime.UtcNow.ToString("o");Save(job);
                }
            };
            return job;
        }
        public static AgentEditorJob BuildPlayer()
        {
            if(Busy||EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Editor is busy.");
            var scenes=EditorBuildSettings.scenes.Where(s=>s.enabled&&File.Exists(s.path)).Select(s=>s.path).ToArray();
            if(scenes.Length==0)throw new ArgumentException("No enabled saved scenes in Build Settings. Save and select scenes before building.");
            var job=new AgentEditorJob{Id=Guid.NewGuid().ToString("N"),Kind="build",Status="queued",Filter=EditorUserBuildSettings.activeBuildTarget.ToString()};
            string extension=EditorUserBuildSettings.activeBuildTarget==BuildTarget.StandaloneOSX?".app":EditorUserBuildSettings.activeBuildTarget==BuildTarget.StandaloneWindows64?".exe":"";
            job.ResultPath="Builds/WorldWeaver/"+job.Id+"/WorldWeaver"+extension;Save(job);
            EditorApplication.delayCall+=()=>
            {
                try
                {
                    var queued=Load(job.Id);if(queued==null||queued.Status!="queued")return;job=queued;
                    job.Status="running";Save(job);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(job.ResultPath));
                    var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=scenes,locationPathName=job.ResultPath,target=EditorUserBuildSettings.activeBuildTarget,options=BuildOptions.Development});
                    job.Success=report.summary.result==BuildResult.Succeeded;job.Status=job.Success?"completed":"failed";
                    job.Failed=(int)report.summary.totalErrors;job.Error=job.Success?null:report.summary.result+": "+report.summary.totalErrors+" build errors";
                }
                catch(Exception e){job.Status="failed";job.Error=ChatHistoryStore.Redact(e.Message);}
                finally{job.FinishedUtc=DateTime.UtcNow.ToString("o");Save(job);}
            };
            return job;
        }
        public static void Cancel(string id)
        {
            var job=Load(id)??throw new ArgumentException("Job not found.");
            if(!ActiveStatus(job.Status))return;
            if(job.Status=="finishing") {job.TestCancelled=true;job.Success=false;job.Error="Test run cancelled; waiting for TestRunner cleanup.";Save(job);return;}
            if(job.Kind=="rotation")
            {
                job.Status="cancelled";job.Success=false;job.Error="Rotation measurement cancelled.";job.FinishedUtc=DateTime.UtcNow.ToString("o");Save(job);
                if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.isPlaying=false;return;
            }
            if(job.Kind=="player_control"){EndPlayerCheck(job,"Player input check cancelled.");return;}
            if(job.Kind.StartsWith("tests:",StringComparison.Ordinal)&&!string.IsNullOrEmpty(job.RunGuid))
            {
                job.Status="cancelling";job.Success=false;Save(job);TestRunnerApi.CancelTestRun(job.RunGuid);return;
            }
            job.Status="cancelled";job.Success=false;job.FinishedUtc=DateTime.UtcNow.ToString("o");Save(job);
        }
        public static AgentEditorJob RunRotationCheck(string stableTarget,float seconds,float degreesPerSecond)
        {
            if(Busy||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Finish the current editor job, compilation and Play Mode first.");
            if(string.IsNullOrEmpty(stableTarget)||(!stableTarget.StartsWith("owned:",StringComparison.Ordinal)&&!stableTarget.StartsWith("global:",StringComparison.Ordinal)))throw new ArgumentException("Rotation check requires a persistent owned: or global: target.");
            if(float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<.25f||seconds>10||float.IsNaN(degreesPerSecond)||float.IsInfinity(degreesPerSecond)||Mathf.Abs(degreesPerSecond)>360)throw new ArgumentException("Rotation check supports .25..10 seconds and -360..360 degrees per second.");
            var target=ProjectContextIndex.ResolveStable(stableTarget);if(!target||!target.activeInHierarchy)throw new ArgumentException("Rotation target must be an active object in the active scene.");
            var job=new AgentEditorJob { Id=Guid.NewGuid().ToString("N"),Kind="rotation",Status="entering_play",StableTarget=stableTarget,RequestedSeconds=seconds,DegreesPerSecond=degreesPerSecond };
            job.ResultPath=Folder+"/"+job.Id+".rotation.json";SessionState.SetString("WorldWeaver.RotationJob",job.Id);Save(job);
            EditorApplication.delayCall+=()=>
            {
                var current=Load(job.Id);if(current==null||current.Status!="entering_play"||EditorApplication.isPlayingOrWillChangePlaymode)return;
                try { EditorApplication.EnterPlaymode(); }
                catch(Exception error) { RotationFailed(current,error.Message,false); }
            };
            return job;
        }
        static void RotationPlayModeChanged(PlayModeStateChange state)
        {
            var job=LoadActive();if(job?.Kind!="rotation"||!ActiveStatus(job.Status))return;
            if(state==PlayModeStateChange.EnteredPlayMode)StartRotation(job);
            if(state==PlayModeStateChange.EnteredEditMode)
            {
                if(job.Status!="exiting_play") {job.Success=false;job.Error="Play Mode ended before the requested measurement completed.";job.Failed=1;}
                job.Status=job.Success?"completed":"failed";job.FinishedUtc=DateTime.UtcNow.ToString("o");Save(job);WriteRotationResult(job);SessionState.EraseString("WorldWeaver.RotationJob");
            }
        }
        static void StartRotation(AgentEditorJob job)
        {
            if(job.MeasurementStarted)return;
            try
            {
                var target=ProjectContextIndex.ResolveStable(job.StableTarget);if(!target)throw new InvalidOperationException("Target did not survive entering Play Mode.");
                Quaternion rotation=target.transform.localRotation;job.StartedGameTime=job.LastGameTime=Time.timeAsDouble;RememberRotation(job,rotation);job.MeasurementStarted=true;job.Status="running";Save(job);
            }
            catch(Exception error) { RotationFailed(job,error.Message,true); }
        }
        static void TickRotation()
        {
            var job=LoadActive();if(job?.Kind!="rotation"||!ActiveStatus(job.Status))return;
            if(DateTime.TryParse(job.StartedUtc,out var began)&&DateTime.UtcNow-began.ToUniversalTime()>TimeSpan.FromSeconds(90)) {RotationFailed(job,"Rotation job exceeded its 90 second wall-clock limit.",EditorApplication.isPlaying);return;}
            if(!EditorApplication.isPlaying||job.Status=="exiting_play")return;
            if(!job.MeasurementStarted){StartRotation(job);return;}
            double now=Time.timeAsDouble,dt=now-job.LastGameTime;if(dt<-.001){RotationFailed(job,"Game clock reset during measurement.",true);return;}if(dt<.02)return;
            if(dt*Math.Abs(job.DegreesPerSecond)>150){RotationFailed(job,"Sampling gap too large to distinguish complete turns; no rotation success inferred.",true);return;}
            try
            {
                var target=ProjectContextIndex.ResolveStable(job.StableTarget);if(!target)throw new InvalidOperationException("Rotation target was deleted.");
                var current=target.transform.localRotation;var previous=new Quaternion(job.LastRotationX,job.LastRotationY,job.LastRotationZ,job.LastRotationW);Quaternion delta=Quaternion.Inverse(previous)*current;
                if(delta.w<0)delta=new Quaternion(-delta.x,-delta.y,-delta.z,-delta.w);
                job.ActualDegrees+=2*Math.Atan2(delta.y,delta.w)*Mathf.Rad2Deg;
                job.OffAxisDegrees+=2*Math.Atan2(Math.Sqrt(delta.x*delta.x+delta.z*delta.z),Math.Abs(delta.w))*Mathf.Rad2Deg;
                job.ActualSeconds=now-job.StartedGameTime;job.LastGameTime=now;job.ExpectedDegrees=job.DegreesPerSecond*job.ActualSeconds;job.Samples++;RememberRotation(job,current);
                if(job.ActualSeconds>=job.RequestedSeconds)
                {
                    job.ToleranceDegrees=Math.Max(1.5,Math.Abs(job.ExpectedDegrees)*.08);
                    job.Success=job.Samples>=3&&Math.Abs(job.ActualDegrees-job.ExpectedDegrees)<=job.ToleranceDegrees&&job.OffAxisDegrees<=job.ToleranceDegrees;
                    job.Passed=job.Success?1:0;job.Failed=job.Success?0:1;job.Error=job.Success?null:"Measured local Y rotation or off-axis drift differs from the requested angular speed.";job.Status="exiting_play";Save(job);WriteRotationResult(job);EditorApplication.isPlaying=false;
                }
                else Save(job);
            }
            catch(Exception error) { RotationFailed(job,error.Message,true); }
        }
        static void RememberRotation(AgentEditorJob job,Quaternion rotation)
        {job.LastRotationX=rotation.x;job.LastRotationY=rotation.y;job.LastRotationZ=rotation.z;job.LastRotationW=rotation.w;}
        static void RotationFailed(AgentEditorJob job,string error,bool leavePlay)
        {
            job.Success=false;job.Failed=1;job.Error=ChatHistoryStore.Redact(error);job.Status=leavePlay?"exiting_play":"failed";if(!leavePlay)job.FinishedUtc=DateTime.UtcNow.ToString("o");Save(job);WriteRotationResult(job);if(leavePlay)EditorApplication.isPlaying=false;
        }
        static void WriteRotationResult(AgentEditorJob job)=>Atomic(job.ResultPath,ChatHistoryStore.Redact(JsonConvert.SerializeObject(job,Formatting.Indented)));
        static void TickTestCompletion()
        {
            var job=LoadActive();if(job==null||job.Status!="finishing"||!job.Kind.StartsWith("tests:",StringComparison.Ordinal))return;
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                if(string.IsNullOrEmpty(job.RunGuid)||isTestRunRunning==null)throw new InvalidOperationException("Missing TestRunner identity/completion probe; cleanup is unverified.");
                if((bool)isTestRunRunning.Invoke(null,new object[]{job.RunGuid}))return;
                if(string.IsNullOrEmpty(job.SceneScopeId))throw new InvalidOperationException("Missing original scene snapshot.");
                AgentCheckpoints.EndTestSceneScope(job.SceneScopeId);
                job.Status=job.TestCancelled?"cancelled":job.Success?"completed":"failed";job.FinishedUtc=DateTime.UtcNow.ToString("o");Save(job);
            }
            catch(Exception error) {job.Success=false;job.Status="failed";job.Error="Cannot verify TestRunner scene cleanup: "+ChatHistoryStore.Redact(error.Message);job.FinishedUtc=DateTime.UtcNow.ToString("o");Save(job);}
        }
        sealed class Callbacks:ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun){}
            public void TestStarted(ITestAdaptor test){}
            public void TestFinished(ITestResultAdaptor result)
            {
                var job=LoadActive();if(job==null||job.Status!="running"||!job.Kind.StartsWith("tests:",StringComparison.Ordinal))return;
                if(result.ResultState.StartsWith("Failed",StringComparison.Ordinal)&&job.Failures.Count<20)
                {job.Failures.Add(ChatHistoryStore.Redact(result.FullName+": "+result.Message));Save(job);}
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                var job=LoadActive();if(job==null||(job.Status!="running"&&job.Status!="cancelling")||!job.Kind.StartsWith("tests:",StringComparison.Ordinal))return;
                bool cancelled=job.Status=="cancelling";
                TestRunnerApi.SaveResultToFile(result,job.ResultPath);
                job.Passed=result.PassCount;job.Failed=result.FailCount;job.Skipped=result.SkipCount;
                job.Success=!cancelled&&result.FailCount==0&&result.PassCount>0&&result.InconclusiveCount==0&&result.SkipCount==0;
                job.TestCancelled=cancelled;job.Status="finishing";job.ResultReceivedUtc=DateTime.UtcNow.ToString("o");job.Error=cancelled?"Test run cancelled; partial results are retained.":job.Success?null:"Tests did not all execute and pass; inspect XML, skipped/inconclusive and failure records.";
                Save(job);
            }
        }
    }
}
