using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

/// <summary>
/// Disposable-project runtime measurement of an exact reviewed MonoBehaviour.
/// Public Game View input injection is checked against the actual legacy Input state;
/// generated source and its methods/input API are never replaced or invoked by this harness.
/// Install into disposable Assets/Editor after other Unity processes have finished.
/// </summary>
[InitializeOnLoad]
public static class BeaconRuntimeAcceptanceRunner
{
    const string StateName="WorldWeaverBeaconRuntimeAcceptance.json";
    const int HoldFrames=30,ReleasedFrames=15,InputDeadlineFrames=180,MaximumSamples=700;
    const double RuntimeDeadlineSeconds=45,PhaseDeadlineSeconds=120;
    const float ColorTolerance=.025f;
    [Serializable] sealed class State
    {
        public string Phase,Project,Output,Source,SourceSha256,ClassName,FixtureFolder,FixtureScene,Positive,Negative,DeadlineUtc;
        public float[] Blue,Amber;
        public Dictionary<string,string> PackageFiles;
        public string PackageRoot;
        public JObject Report;
    }
    sealed class ObservationMarker { }
    static string StatePath=>Path.Combine(Directory.GetParent(Application.dataPath).FullName,"UserSettings",StateName);
    static bool stepping,observing;
    static State active;
    static PlayerLoopSystem originalLoop;
    static Renderer positive,negative;
    static string stage;
    static int stageFrame,observedFrames,downEdges,upEdges,heldOne,heldTwo,colorChanges;
    static float startedTime,stageTime;
    static Color previous;

    static BeaconRuntimeAcceptanceRunner()
    {
        if(Environment.GetEnvironmentVariable("WW_BEACON_ACCEPTANCE")!="1")return;
        EditorApplication.update+=Tick;
        EditorApplication.playModeStateChanged+=PlayState;
    }

    public static void Run()
    {
        if(Environment.GetEnvironmentVariable("WW_BEACON_ACCEPTANCE")!="1"||!Application.isBatchMode)
            throw new InvalidOperationException("Explicit WW_BEACON_ACCEPTANCE=1 and disposable batch mode are required.");
        string project=Directory.GetParent(Application.dataPath).FullName;
        if(Path.GetFullPath(Environment.GetEnvironmentVariable("WW_BEACON_DISPOSABLE_PROJECT")??"")!=Path.GetFullPath(project))
            throw new InvalidOperationException("WW_BEACON_DISPOSABLE_PROJECT must exactly identify this disposable project.");
        if(File.Exists(StatePath))throw new InvalidOperationException("An existing beacon run must be inspected before starting another.");
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
            throw new InvalidOperationException("The disposable editor must be idle in Edit Mode.");
        string output=Path.GetFullPath(Required("WW_BEACON_OUTPUT"));
        string source=SourcePath(Required("WW_BEACON_SOURCE"));
        string hash=Required("WW_BEACON_SOURCE_SHA256").ToLowerInvariant();
        if(!Regex.IsMatch(hash,"^[a-f0-9]{64}$")||Sha(File.ReadAllBytes(source))!=hash)
            throw new InvalidOperationException("The exact imported source does not match the independently reviewed SHA256.");
        string className=Required("WW_BEACON_CLASS");
        if(!Regex.IsMatch(className,"^[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)*$"))throw new ArgumentException("Use a C# type name without an assembly name.");
        string asset="Assets/"+source.Substring(Path.GetFullPath(Application.dataPath).Length+1).Replace(Path.DirectorySeparatorChar,'/');
        var script=AssetDatabase.LoadAssetAtPath<MonoScript>(asset);var type=script?script.GetClass():null;
        if(type==null||type.FullName!=className||type.Assembly.GetName().Name!="Assembly-CSharp"||type.IsAbstract||!typeof(MonoBehaviour).IsAssignableFrom(type))
            throw new InvalidOperationException("The reviewed file must define the requested concrete Assembly-CSharp MonoBehaviour.");
        string package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WorldWeaver.IntelligentNPC).Assembly)?.resolvedPath;
        if(string.IsNullOrEmpty(package))throw new InvalidOperationException("Cannot resolve the immutable package source.");
        package=Path.GetFullPath(package);
        if(Within(output,Application.dataPath)||Within(output,Path.Combine(project,"Packages"))||Within(output,package))
            throw new InvalidOperationException("Output must remain outside Assets, Packages and the resolved immutable package.");
        string folder="Assets/BeaconRuntimeAcceptance-"+Guid.NewGuid().ToString("N");
        active=new State {Phase="creating",Project=project,Output=output,Source=source,SourceSha256=hash,ClassName=className,
            FixtureFolder=folder,FixtureScene=folder+"/Fixture.unity",Positive="Exact reviewed beacon",Negative="Stationary blue negative control",
            Blue=ParseColor(Environment.GetEnvironmentVariable("WW_BEACON_BLUE_RGBA")??"0,0,1,1"),
            Amber=ParseColor(Required("WW_BEACON_AMBER_RGBA")),PackageRoot=package,PackageFiles=Files(package),
            DeadlineUtc=DateTime.UtcNow.AddSeconds(PhaseDeadlineSeconds).ToString("O"),
            Report=new JObject { ["schemaVersion"]=1,["kind"]="exact-reviewed-beacon-real-input-and-color-runtime",["startedUtc"]=DateTime.UtcNow.ToString("O"),
                ["unity"]=Application.unityVersion,["success"]=false,["llmInferencePerformed"]=false,["reviewedClassName"]=className,["reviewedSourcePath"]=source,["reviewedSourceSha256"]=hash,
                ["sourceUnmodifiedByHarness"]=true,["harnessSourceSha256"]=File.Exists("Assets/Editor/BeaconRuntimeAcceptanceRunner.cs")?Sha(File.ReadAllBytes("Assets/Editor/BeaconRuntimeAcceptanceRunner.cs")):"not found at standard installation path",
                ["inputInjectionApi"]="UnityEditor.EditorGUIUtility.QueueGameViewInputEvent",
                ["samplingPoint"]="An additional PostLateUpdate callback, after the normal generated Update; no generated method is invoked by the harness.",
                ["holdFramesRequired"]=HoldFrames,["releasedFramesRequired"]=ReleasedFrames,["colorTolerancePerChannel"]=ColorTolerance,
                ["samples"]=new JArray(),["queuedInputEvents"]=new JArray(),["runtimeErrors"]=new JArray(),["checks"]=new JObject(),
                ["limitations"]=new JArray("One reviewed script and one authored keyboard/color mechanic, not general model reliability.",
                    "Input delivery must be independently observed. If Game View injection is unavailable in batch mode, behavior is not evaluated and native input is still required.",
                    "Renderer material/property-block colors are measured; this is not a rendered image or a GPU/shader/art-quality test.") }
        };
        active.Report["expectedBlue"]=JArray.FromObject(active.Blue);active.Report["expectedAmber"]=JArray.FromObject(active.Amber);
        Save(active);
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            if(string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder))))throw new IOException("Cannot create disposable fixture folder.");
            var shader=GraphicsSettings.currentRenderPipeline?Shader.Find("Universal Render Pipeline/Lit"):Shader.Find("Standard");
            if(!shader)throw new InvalidOperationException("No supported Standard/URP Lit shader for the renderer fixture.");
            var material=new Material(shader){name="Initial independently authored blue"};material.color=ColorOf(active.Blue);
            AssetDatabase.CreateAsset(material,folder+"/InitialBlue.mat");
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=active.Positive;go.GetComponent<Renderer>().sharedMaterial=material;go.AddComponent(type);
            var control=GameObject.CreatePrimitive(PrimitiveType.Cube);control.name=active.Negative;control.transform.position=Vector3.right*3;
            control.GetComponent<Renderer>().sharedMaterial=material;
            // Separate runtime material instances are deliberately left to the reviewed source.
            // A script mutating the shared asset will also change the negative control and fail.
            EditorSceneManager.SaveScene(go.scene,active.FixtureScene);
            active.Phase="entering";Save(active);Write(active.Output,active.Report);
            EditorApplication.isPlaying=true;
        }
        catch(Exception error){Fail(active,error);}
    }

    static void PlayState(PlayModeStateChange change)
    {
        if(change==PlayModeStateChange.EnteredPlayMode)InstallObservation();
    }
    static void InstallObservation()
    {
        if(observing||!File.Exists(StatePath)||!Application.isPlaying)return;
        active=Load();if(active.Phase!="entering")return;
        positive=GameObject.Find(active.Positive)?.GetComponent<Renderer>();negative=GameObject.Find(active.Negative)?.GetComponent<Renderer>();
        if(!positive||!negative){Fail(active,new InvalidOperationException("Runtime fixture renderers are absent."));return;}
        originalLoop=PlayerLoop.GetCurrentPlayerLoop();var loop=originalLoop;bool installed=false;
        var systems=loop.subSystemList;
        for(int i=0;i<systems.Length;i++)if(systems[i].type==typeof(PostLateUpdate))
        {
            var children=(systems[i].subSystemList??Array.Empty<PlayerLoopSystem>()).ToList();
            children.Add(new PlayerLoopSystem {type=typeof(ObservationMarker),updateDelegate=Observe});systems[i].subSystemList=children.ToArray();installed=true;break;
        }
        if(!installed){Fail(active,new InvalidOperationException("PostLateUpdate was absent; no runtime observation installed."));return;}
        loop.subSystemList=systems;PlayerLoop.SetPlayerLoop(loop);observing=true;Application.logMessageReceived+=Log;
        observedFrames=downEdges=upEdges=heldOne=heldTwo=colorChanges=0;
        startedTime=stageTime=Time.realtimeSinceStartup;stageFrame=0;stage="baseline";previous=EffectiveColor(positive);
        active.Phase="playing";Save(active);
    }
    static void Observe()
    {
        if(!observing||active?.Phase!="playing")return;
        try
        {
            if(!positive||!negative)throw new InvalidOperationException("A runtime fixture renderer disappeared.");
            observedFrames++;
            bool down=Input.GetKeyDown(KeyCode.F),held=Input.GetKey(KeyCode.F),up=Input.GetKeyUp(KeyCode.F);
            if(down)downEdges++;if(up)upEdges++;
            var color=EffectiveColor(positive);var control=EffectiveColor(negative);
            if(Distance(color,previous)>ColorTolerance)colorChanges++;previous=color;
            ((JArray)active.Report["samples"]).Add(new JObject { ["frame"]=Time.frameCount,["observedFrame"]=observedFrames,["time"]=Time.realtimeSinceStartup-startedTime,
                ["phase"]=stage,["fDown"]=down,["fHeld"]=held,["fUp"]=up,["color"]=ColorArray(color),["negativeColor"]=ColorArray(control) });
            if(observedFrames>MaximumSamples||Time.realtimeSinceStartup-startedTime>RuntimeDeadlineSeconds)
                throw new TimeoutException("Runtime/input-delivery deadline reached; missing input is not a generated-code failure.");
            switch(stage)
            {
                case "baseline":if(observedFrames>=15){Queue(EventType.KeyDown);Phase("first_down");}break;
                case "first_down":if(down&&held){Phase("held_amber");}else InputTimeout();break;
                case "held_amber":heldOne++;if(heldOne>=HoldFrames&&Time.realtimeSinceStartup-stageTime>=.35f){Queue(EventType.KeyUp);Phase("first_up");}break;
                case "first_up":if(up&&!held)Phase("released_amber");else InputTimeout();break;
                case "released_amber":if(observedFrames-stageFrame>=ReleasedFrames){Queue(EventType.KeyDown);Phase("second_down");}break;
                case "second_down":if(down&&held)Phase("held_blue");else InputTimeout();break;
                case "held_blue":heldTwo++;if(heldTwo>=HoldFrames&&Time.realtimeSinceStartup-stageTime>=.35f){Queue(EventType.KeyUp);Phase("second_up");}break;
                case "second_up":if(up&&!held)Phase("released_blue");else InputTimeout();break;
                case "released_blue":if(observedFrames-stageFrame>=ReleasedFrames)CompleteRuntime();break;
                default:throw new InvalidOperationException("Unknown runtime measurement phase.");
            }
        }
        catch(Exception error){Fail(active,error);}
    }
    static void InputTimeout()
    {if(observedFrames-stageFrame>=InputDeadlineFrames)throw new TimeoutException("Queued F input was not observed by the legacy Input API; runtime behavior was not evaluated.");}
    static void Phase(string next){stage=next;stageFrame=observedFrames;stageTime=Time.realtimeSinceStartup;}
    static void Queue(EventType type)
    {
        var evt=new Event {type=type,keyCode=KeyCode.F,character='f',modifiers=EventModifiers.None};
        EditorGUIUtility.QueueGameViewInputEvent(evt);
        ((JArray)active.Report["queuedInputEvents"]).Add(new JObject {["type"]=type.ToString(),["key"]="F",["afterObservedFrame"]=observedFrames,["utc"]=DateTime.UtcNow.ToString("O")});
    }
    static void CompleteRuntime()
    {
        var rows=((JArray)active.Report["samples"]).OfType<JObject>().ToArray();var checks=(JObject)active.Report["checks"];
        bool InputPhase(string name,bool held)=>rows.Where(r=>(string)r["phase"]==name).All(r=>(bool)r["fHeld"]==held&&!(bool)r["fDown"]&&!(bool)r["fUp"]);
        bool ColorPhase(string name,float[] expected)=>rows.Any(r=>(string)r["phase"]==name)&&rows.Where(r=>(string)r["phase"]==name).All(r=>Distance(ReadColor(r["color"]),ColorOf(expected))<=ColorTolerance);
        checks["twoActualPressEdgesAndReleaseEdgesObserved"]=downEdges==2&&upEdges==2;
        checks["firstHoldObservedWithoutAdditionalEdges"]=heldOne>=HoldFrames&&InputPhase("held_amber",true);
        checks["secondHoldObservedWithoutAdditionalEdges"]=heldTwo>=HoldFrames&&InputPhase("held_blue",true);
        checks["releasedIntervalsActuallyObserved"]=InputPhase("released_amber",false)&&InputPhase("released_blue",false);
        checks["initialBlue"]=ColorPhase("baseline",active.Blue);
        checks["firstPressAmber"]=ColorPhase("held_amber",active.Amber)&&rows.Where(r=>(string)r["phase"]=="first_down"&&(bool)r["fDown"]).All(r=>Distance(ReadColor(r["color"]),ColorOf(active.Amber))<=ColorTolerance);
        checks["releaseDoesNotToggleColor"]=ColorPhase("first_up",active.Amber)&&ColorPhase("released_amber",active.Amber);
        checks["secondPressReturnsExactBlue"]=ColorPhase("held_blue",active.Blue)&&ColorPhase("released_blue",active.Blue)&&rows.Where(r=>(string)r["phase"]=="second_down"&&(bool)r["fDown"]).All(r=>Distance(ReadColor(r["color"]),ColorOf(active.Blue))<=ColorTolerance);
        checks["exactlyTwoObservedColorTransitions"]=colorChanges==2;
        checks["stationaryNegativeControlAlwaysBlue"]=rows.All(r=>Distance(ReadColor(r["negativeColor"]),ColorOf(active.Blue))<=ColorTolerance);
        checks["noRuntimeErrors"]=((JArray)active.Report["runtimeErrors"]).Count==0;
        active.Report["inputDeliveryConfirmed"]=(bool)checks["twoActualPressEdgesAndReleaseEdgesObserved"]&&(bool)checks["firstHoldObservedWithoutAdditionalEdges"]&&(bool)checks["secondHoldObservedWithoutAdditionalEdges"];
        active.Report["runtimeBehaviorEvaluated"]=active.Report["inputDeliveryConfirmed"].Value<bool>();
        active.Report["actualDownEdges"]=downEdges;active.Report["actualUpEdges"]=upEdges;active.Report["colorTransitions"]=colorChanges;
        active.Report["observedFrames"]=observedFrames;active.Phase="exit_requested";Save(active);
    }
    static void Log(string message,string stack,LogType type)
    {
        if(active==null||!(active.Report["runtimeErrors"] is JArray errors)||errors.Count>=10||type!=LogType.Error&&type!=LogType.Exception&&type!=LogType.Assert)return;
        errors.Add(new JObject {["type"]=type.ToString(),["message"]=message,["stack"]=stack});
    }
    static void Tick()
    {
        if(stepping||!File.Exists(StatePath))return;stepping=true;
        try
        {
            var state=active??Load();
            if(DateTime.UtcNow>DateTime.Parse(state.DeadlineUtc).ToUniversalTime()&&state.Phase!="exiting"&&state.Phase!="finished")Fail(state,new TimeoutException("Beacon acceptance exceeded its phase deadline."));
            state=active??Load();
            if(state.Phase=="entering"&&Application.isPlaying)InstallObservation();
            if(state.Phase=="exit_requested")
            {
                if(observing){PlayerLoop.SetPlayerLoop(originalLoop);Application.logMessageReceived-=Log;observing=false;}
                if(Application.isPlaying)EditorGUIUtility.QueueGameViewInputEvent(new Event {type=EventType.KeyUp,keyCode=KeyCode.F,character='f'});
                state.Phase="exiting";state.DeadlineUtc=DateTime.UtcNow.AddSeconds(PhaseDeadlineSeconds).ToString("O");Save(state);EditorApplication.isPlaying=false;
            }
            else if(state.Phase=="exiting"&&!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating)Finish(state);
            else if(state.Phase=="exiting"&&DateTime.UtcNow>DateTime.Parse(state.DeadlineUtc).ToUniversalTime())
            {
                state.Report["success"]=false;state.Report["error"]="Editor did not finish Play Mode exit/import before the cleanup deadline; cleanup is unconfirmed.";
                state.Report["finishedUtc"]=DateTime.UtcNow.ToString("O");Write(state.Output,state.Report);EditorApplication.Exit(2);
            }
        }
        catch(Exception error)
        {
            try{var state=active??Load();Fail(state,error);}catch{Debug.LogException(error);EditorApplication.Exit(2);}
        }
        finally{stepping=false;}
    }
    static void Fail(State state,Exception error)
    {
        active=state;state.Report["error"]=error.ToString();state.Report["success"]=false;
        if(state.Report["inputDeliveryConfirmed"]==null)state.Report["inputDeliveryConfirmed"]=false;
        if(state.Report["runtimeBehaviorEvaluated"]==null)state.Report["runtimeBehaviorEvaluated"]=false;
        state.Phase="exit_requested";Save(state);
    }
    static void Finish(State state)
    {
        var checks=(JObject)state.Report["checks"];
        checks["reviewedSourceUnchanged"]=File.Exists(state.Source)&&Sha(File.ReadAllBytes(state.Source))==state.SourceSha256;
        checks["immutablePackageUnchanged"]=Same(state.PackageFiles,Files(state.PackageRoot));
        checks["playModeExited"]=!EditorApplication.isPlayingOrWillChangePlaymode;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        checks["ownedFixtureRemoved"]=AssetDatabase.DeleteAsset(state.FixtureFolder)&&!Directory.Exists(state.FixtureFolder);
        state.Report["sourceSha256AfterMeasurement"]=File.Exists(state.Source)?Sha(File.ReadAllBytes(state.Source)):null;
        state.Report["packageFileCount"]=state.PackageFiles.Count;
        state.Report["packageTreeSha256"]=Sha(Encoding.UTF8.GetBytes(string.Concat(state.PackageFiles.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"\0"+p.Value+"\n"))));
        bool success=state.Report["error"]==null&&state.Report["runtimeBehaviorEvaluated"]?.Value<bool>()==true&&checks.Properties().All(p=>p.Value.Type==JTokenType.Boolean&&(bool)p.Value);
        state.Report["success"]=success;state.Report["finishedUtc"]=DateTime.UtcNow.ToString("O");Write(state.Output,state.Report);
        File.Delete(StatePath);active=null;EditorApplication.Exit(success?0:1);
    }
    static Color EffectiveColor(Renderer renderer)
    {
        var material=renderer.sharedMaterial;if(!material)throw new InvalidOperationException("A fixture renderer lost its material.");
        string property=material.HasProperty("_BaseColor")?"_BaseColor":material.HasProperty("_Color")?"_Color":null;
        if(property==null)throw new InvalidOperationException("The runtime material has no supported visible color property.");
        var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
        return block.HasColor(property)?block.GetColor(property):material.GetColor(property);
    }
    static float Distance(Color a,Color b)=>Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b),Mathf.Abs(a.a-b.a));
    static JArray ColorArray(Color c)=>new JArray(c.r,c.g,c.b,c.a);
    static Color ReadColor(JToken array)=>new Color((float)array[0],(float)array[1],(float)array[2],(float)array[3]);
    static Color ColorOf(float[] values)=>new Color(values[0],values[1],values[2],values[3]);
    static float[] ParseColor(string text)
    {
        var parts=text.Split(',');if(parts.Length!=4)throw new ArgumentException("RGBA must contain four comma-separated normalized numbers.");
        var values=parts.Select(p=>float.Parse(p,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if(values.Any(v=>float.IsNaN(v)||float.IsInfinity(v)||v<0||v>1))throw new ArgumentException("RGBA values must be finite and normalized.");return values;
    }
    static string SourcePath(string value)
    {
        string assets=Path.GetFullPath(Application.dataPath),source=Path.GetFullPath(value);
        if(!Within(source,assets)||Path.GetExtension(source)!=".cs"||!File.Exists(source))throw new ArgumentException("The reviewed .cs source must exist under this disposable project's Assets.");
        for(string path=source;path!=null;path=Path.GetDirectoryName(path))
        {if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("No source symlinks are followed.");if(path==assets)break;}
        return source;
    }
    static bool Within(string path,string folder)=>Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.Ordinal);
    static string Required(string key)=>string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))?throw new ArgumentException(key+" is required."):Environment.GetEnvironmentVariable(key);
    static Dictionary<string,string> Files(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(p=>Path.GetFileName(p)!=".DS_Store").OrderBy(p=>p,StringComparer.Ordinal).ToDictionary(p=>p.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\','/'),p=>Sha(File.ReadAllBytes(p)),StringComparer.Ordinal);
    static bool Same(Dictionary<string,string> a,Dictionary<string,string> b)=>a.Count==b.Count&&a.All(p=>b.TryGetValue(p.Key,out var hash)&&p.Value==hash);
    static string Sha(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static State Load()=>JsonConvert.DeserializeObject<State>(File.ReadAllText(StatePath));
    static void Save(State state){Directory.CreateDirectory(Path.GetDirectoryName(StatePath));File.WriteAllText(StatePath,JsonConvert.SerializeObject(state));}
    static void Write(string path,JObject value){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,value.ToString(Formatting.Indented));}
}
