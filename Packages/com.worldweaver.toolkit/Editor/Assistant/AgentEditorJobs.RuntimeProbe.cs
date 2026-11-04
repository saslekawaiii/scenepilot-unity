using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif
namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Agent-authored assertions over actual Play frames; not an independent proof.</summary>
    public sealed class RuntimeProbeContext : IDisposable
    {
        public readonly JObject Measurements=new JObject();
        public readonly JArray Assertions=new JArray();
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard;Mouse mouse;
        public Keyboard Keyboard { get { if(keyboard==null){keyboard=InputSystem.AddDevice<Keyboard>("WorldWeaverRuntimeProbeKeyboard");keyboard.MakeCurrent();SessionState.SetInt("WorldWeaver.Probe.Keyboard",keyboard.deviceId);}return keyboard;} }
        public Mouse Mouse { get { if(mouse==null){mouse=InputSystem.AddDevice<Mouse>("WorldWeaverRuntimeProbeMouse");mouse.MakeCurrent();SessionState.SetInt("WorldWeaver.Probe.Mouse",mouse.deviceId);}return mouse;} }
#endif
        static JToken Value(object value)
        {
            // Unity vectors expose normalized/other computed vectors. Default
            // reflection follows those properties recursively, losing even an
            // ordinary measured position. Use the same bounded value/read-back
            // representation as Editor snippets, including nested values.
            var serializer=JsonSerializer.Create(new JsonSerializerSettings{TypeNameHandling=TypeNameHandling.None,ReferenceLoopHandling=ReferenceLoopHandling.Error,MaxDepth=8,Converters=new System.Collections.Generic.List<JsonConverter>{new EditorCodeTools.UnityReadbackConverter()}});
            var result=value==null?JValue.CreateNull():JToken.FromObject(value,serializer);
            if(result.ToString(Formatting.None).Length>6000)throw new ArgumentException("One runtime observation exceeds 6000 characters; return bounded measured primitives.");
            return result;
        }
        public void Record(string name,object value)
        {
            if(string.IsNullOrWhiteSpace(name)||name.Length>160||Measurements.Count>=64&&!Measurements.ContainsKey(name))throw new ArgumentException("Use up to 64 named bounded observations.");
            Measurements[name]=Value(value);if(Measurements.ToString(Formatting.None).Length>16000)throw new ArgumentException("Runtime observations exceeded the total 16000-character budget.");
        }
        public void Check(bool condition,string name,object observed=null)
        {
            if(string.IsNullOrWhiteSpace(name)||name.Length>160||Assertions.Count>=64)throw new ArgumentException("Use 1..64 named assertions.");
            Assertions.Add(new JObject{["name"]=name,["passed"]=condition,["observed"]=Value(observed)});if(Assertions.ToString(Formatting.None).Length>16000)throw new ArgumentException("Runtime assertions exceeded the total 16000-character budget.");
        }
        public void Dispose()
        {
#if ENABLE_INPUT_SYSTEM
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
#endif
        }
    }
    public static partial class AgentEditorJobs
    {
        internal const string RuntimeProbeReference=@"run_runtime_probe: empty target; args {code:string,seconds?:0.25..15}. Standalone actual Play Mode job: use probe.Check(actualCondition,name,observed) for actual game mechanics, UI, physics, triggers, counters and references. Requires autonomous Editor C# permission, with the same full-trust boundary as execute_editor_code. code is an IEnumerator METHOD BODY up to 16000 characters, not a class; optional leading using directives are supported: yield return null between frames; no other yield instructions. Usings System, Collections, Generic, Linq, UnityEngine, UnityEngine.UI, UnityEngine.EventSystems, WorldWeaver and Newtonsoft.Json.Linq are supplied; InputSystem/LowLevel when its backend is enabled. Object and Random default to UnityEngine aliases unless explicitly overridden by a leading using alias. Parameter probe offers Record(name, measuredPrimitiveObject), Check(actualCondition,name,observedValue), and automatically installed and cleaned-up virtual probe.Keyboard/probe.Mouse (also available as Keyboard.current/Mouse.current, even in headless Editors). Use STATIC InputSystem.QueueStateEvent(probe.Keyboard, new KeyboardState(Key.A)); release with new KeyboardState(). KeyboardState and MouseState are in UnityEngine.InputSystem.LowLevel, not UnityEngine.InputSystem. QueueStateEvent is not an instance method on Keyboard. Yield null, then measure actual game state. Use bounded Time.time loops; wait for Awake/Start (first frame is supplied). At least one real Check is required; any false check/exception/timeout fails. A host wall limit bounds yielded execution; C# must never block, spin, sleep, access network/files/secrets, save assets/scenes or alter Editor settings. Test arrangements inside Play Mode are temporary, not implementation. The job exits Play Mode and restores the original scene before completion; poll_job/job_result carries source hash, exact checks and measured data. Agent-authored checks only prove the behavior actually exercised, not unrelated criteria. Example: var light=UnityEngine.Object.FindFirstObjectByType<Light>(); probe.Check(light!=null,""light exists""); if(light!=null){float before=light.intensity; yield return null; probe.Record(""intensity"",new{before,after=light.intensity});} Never mark Check(true) as gameplay verification.";
        static IEnumerator runtimeProbe;
        static RuntimeProbeContext runtimeContext;
        static int runtimeFrame,runtimeSamples;
        static double runtimeDeadline;
        static string ProbeSource(string code)
        {
            string directives="";
            while(true)
            {
                var directive=Regex.Match(code,@"\A\s*using\s+(?:(?:static\s+)?[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*|[A-Za-z_][A-Za-z0-9_]*\s*=\s*[^;{}]+)\s*;");
                if(!directive.Success)break;directives+=directive.Value+"\n";code=code.Substring(directive.Length);
            }
            string input="";
#if ENABLE_INPUT_SYSTEM
            input="using UnityEngine.InputSystem;using UnityEngine.InputSystem.LowLevel;";
#endif
            return "using System;using System.Collections;using System.Collections.Generic;using System.Linq;using UnityEngine;using UnityEngine.UI;using UnityEngine.EventSystems;using WorldWeaver;using WorldWeaver.Editor.Assistant;using Newtonsoft.Json.Linq;"+input+"\n"+directives+EditorSnippetCompiler.DefaultUnityAliases(directives)+"\npublic static class WWRuntimeProbeSnippet{public static IEnumerator Run(RuntimeProbeContext probe){yield return null;\n"+code+"\n}}";
        }
        static Assembly CompileRuntimeProbe(string code)
        {
            var paths=AppDomain.CurrentDomain.GetAssemblies().Where(a=>!a.IsDynamic).Select(a=>{try{return a.Location;}catch{return "";}}).Where(p=>!string.IsNullOrEmpty(p)&&File.Exists(p)).Distinct().ToArray();
            var assembly=EditorSnippetCompiler.Compile(ProbeSource(code),paths,out var errors);
            if(assembly==null)throw new ArgumentException("Runtime probe compilation failed: "+string.Join("\n",errors.Take(12)));
            return assembly;
        }
        public static AgentEditorJob RunRuntimeProbe(string code,float seconds=10)
        {
            if(Busy||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Finish the active Editor operation and Play Mode first.");
            if(string.IsNullOrWhiteSpace(code)||code.Length>16000||float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<.25f||seconds>15)throw new ArgumentException("A complete iterator method body and a .25..15 second wall budget are required.");
            runtimeSamples=0;
            CompileRuntimeProbe(code); // Actual diagnostics before any Play transition.
            var job=new AgentEditorJob{Id=Guid.NewGuid().ToString("N"),Kind="runtime_probe",Status="entering_play",RequestedSeconds=seconds,ProbeCode=code,ProbeSourceHash=TextChunker.Digest(ProbeSource(code)),Measurement="Agent-authored assertions sampled in actual Play Mode; only exercised behavior is verified"};
            job.ResultPath=Folder+"/"+job.Id+".probe.json";SessionState.SetString("WorldWeaver.RuntimeProbe",job.Id);Save(job);
            EditorApplication.delayCall+=()=>{if(Load(job.Id)?.Status=="entering_play"&&!EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.EnterPlaymode();};return job;
        }
        static void RuntimeProbeModeChanged(PlayModeStateChange mode)
        {
            var job=LoadActive();if(job?.Kind!="runtime_probe"||!ActiveStatus(job.Status))return;
            if(mode==PlayModeStateChange.ExitingPlayMode){CleanupRuntimeProbe();return;}
            if(mode==PlayModeStateChange.EnteredEditMode)
            {
                if(job.Status!="exiting_play"){job.Success=false;job.Error="Play Mode ended before the probe finished.";}
                job.Status=job.Success?"completed":"failed";job.FinishedUtc=DateTime.UtcNow.ToString("o");Save(job);Atomic(job.ResultPath,JsonConvert.SerializeObject(job,Formatting.Indented));SessionState.EraseString("WorldWeaver.RuntimeProbe");return;
            }
            if(mode!=PlayModeStateChange.EnteredPlayMode)return;
            try
            {
                if(SessionState.GetString("WorldWeaver.RuntimeProbe","")!=job.Id)throw new InvalidOperationException("Runtime probe session was lost; no observation is replayed.");
#if ENABLE_INPUT_SYSTEM
                SessionState.SetInt("WorldWeaver.Probe.PreviousKeyboard",Keyboard.current?.deviceId??-1);SessionState.SetInt("WorldWeaver.Probe.PreviousMouse",Mouse.current?.deviceId??-1);
                SessionState.SetInt("WorldWeaver.Probe.Background",(int)InputSystem.settings.backgroundBehavior);SessionState.SetInt("WorldWeaver.Probe.Routing",(int)InputSystem.settings.editorInputBehaviorInPlayMode);
                SessionState.SetBool("WorldWeaver.Probe.InputConfigured",true);
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
                runtimeContext=new RuntimeProbeContext();
#if ENABLE_INPUT_SYSTEM
                // Standard InputSystem client code must have devices in headless Editors too.
                // Install before the iterator starts; cleanup restores previous devices and routing.
                _=runtimeContext.Keyboard;_=runtimeContext.Mouse;
#endif
                runtimeProbe=(IEnumerator)CompileRuntimeProbe(job.ProbeCode).GetType("WWRuntimeProbeSnippet").GetMethod("Run").Invoke(null,new object[]{runtimeContext});
                runtimeFrame=-1;runtimeSamples=0;runtimeDeadline=EditorApplication.timeSinceStartup+job.RequestedSeconds;job.Status="running";job.StartedGameTime=Time.timeAsDouble;Save(job);
            }
            catch(Exception error){EndRuntimeProbe(job,error.GetBaseException().Message);}
        }
        static void TickRuntimeProbe()
        {
            var job=LoadActive();if(job?.Kind!="runtime_probe"||!ActiveStatus(job.Status))return;
            if(SessionState.GetString("WorldWeaver.RuntimeProbe","")!=job.Id){job.Success=false;job.Status="interrupted";job.Error="Editor restarted during runtime probe; no success inferred.";Save(job);return;}
            if(job.Status=="exiting_play"||job.Status=="entering_play")
            {
                if(DateTime.TryParse(job.StartedUtc,out var start)&&DateTime.UtcNow-start.ToUniversalTime()>TimeSpan.FromSeconds(90))EndRuntimeProbe(job,"Play transition exceeded 90 seconds.");
                return;
            }
            if(!EditorApplication.isPlaying)return;
            if(runtimeProbe==null){EndRuntimeProbe(job,"Runtime domain reloaded during measurement; no replay or success inferred.");return;}
            if(EditorApplication.timeSinceStartup>runtimeDeadline){EndRuntimeProbe(job,"Runtime probe exceeded its yielded wall-time budget.");return;}
            if(runtimeFrame==Time.frameCount)return;runtimeFrame=Time.frameCount;job.Samples=++runtimeSamples;
            try
            {
                if(!runtimeProbe.MoveNext()){EndRuntimeProbe(job,null);return;}
                if(runtimeProbe.Current!=null)EndRuntimeProbe(job,"Only yield return null is supported; wait by observing actual game time between frames.");
            }
            catch(Exception error){EndRuntimeProbe(job,error.GetBaseException().Message);}
        }
        static void EndRuntimeProbe(AgentEditorJob job,string error)
        {
            job.Samples=runtimeSamples;
            if(runtimeContext!=null){job.ProbeObservations=(JObject)runtimeContext.Measurements.DeepClone();job.ProbeAssertions=(JArray)runtimeContext.Assertions.DeepClone();}
            job.Passed=job.ProbeAssertions.Count(a=>(bool)a["passed"]);job.Failed=job.ProbeAssertions.Count-job.Passed;
            job.Success=error==null&&job.Passed>0&&job.Failed==0;job.Error=ChatHistoryStore.Redact(error??(job.ProbeAssertions.Count==0?"No runtime assertion executed.":job.Failed>0?"One or more actual runtime assertions failed.":null));
            job.ActualSeconds=EditorApplication.isPlaying?Math.Max(0,Time.timeAsDouble-job.StartedGameTime):0;
            CleanupRuntimeProbe();job.Status="exiting_play";Save(job);
            if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.ExitPlaymode();else RuntimeProbeModeChanged(PlayModeStateChange.EnteredEditMode);
        }
        static void CleanupRuntimeProbe()
        {
            try{(runtimeProbe as IDisposable)?.Dispose();}catch(Exception){}runtimeProbe=null;
            runtimeContext?.Dispose();runtimeContext=null;
#if ENABLE_INPUT_SYSTEM
            foreach(string key in new[]{"WorldWeaver.Probe.Keyboard","WorldWeaver.Probe.Mouse"}){int id=SessionState.GetInt(key,-1);var device=InputSystem.devices.FirstOrDefault(d=>d.deviceId==id);if(device!=null)InputSystem.RemoveDevice(device);SessionState.EraseInt(key);}
            foreach(string key in new[]{"WorldWeaver.Probe.PreviousKeyboard","WorldWeaver.Probe.PreviousMouse"}){int id=SessionState.GetInt(key,-1);var device=InputSystem.devices.FirstOrDefault(d=>d.deviceId==id);if(device!=null)device.MakeCurrent();SessionState.EraseInt(key);}
            if(SessionState.GetBool("WorldWeaver.Probe.InputConfigured",false))
            {
                InputSystem.settings.backgroundBehavior=(InputSettings.BackgroundBehavior)SessionState.GetInt("WorldWeaver.Probe.Background",0);InputSystem.settings.editorInputBehaviorInPlayMode=(InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt("WorldWeaver.Probe.Routing",0);SessionState.EraseBool("WorldWeaver.Probe.InputConfigured");
            }
#endif
        }
    }
}
