using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace WorldWeaver.Editor.Assistant
{
    public static partial class AgentEditorJobs
    {
#if ENABLE_INPUT_SYSTEM
        static Keyboard checkKeyboard;static Mouse checkMouse;
        static WorldPlayer checkPlayer;static Transform checkLeg;
        static Vector3 checkPosition,checkCamera;
        static Quaternion checkLegStart;
        static double checkBegan;static int checkPhase,checkFrame,phaseFrame;
        static float checkYaw,checkPitch,checkLegAngle;
        static Transform[] checkUpper,checkElbows,checkHands;
        static bool checkArmsMeasured;static float checkElbowBend,checkHandHeight;
        static InputSettings.BackgroundBehavior oldBackground;
        static InputSettings.EditorInputBehaviorInPlayMode oldRouting;
#endif
        public static AgentEditorJob RunPlayerCheck(string target,float seconds=1,bool relaxedArms=false)
        {
#if !ENABLE_INPUT_SYSTEM
            throw new NotSupportedException("Player device check requires the Input System package/backend; no Play success is inferred.");
#else
            if(Busy||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Finish the current editor operation before checking input.");
            if(seconds<.5f||seconds>3||float.IsNaN(seconds)||float.IsInfinity(seconds))throw new ArgumentException("Player check supports .5..3 seconds.");
            var player=ProjectContextIndex.ResolveStable(target)?.GetComponent<WorldPlayer>();if(!player||!player.View||!player.isActiveAndEnabled)throw new ArgumentException("Check needs an active WorldPlayer with its actual camera.");
            var job=new AgentEditorJob{Id=Guid.NewGuid().ToString("N"),Kind="player_control",Status="entering_play",StableTarget=target,RequestedSeconds=seconds,RequireRelaxedArms=relaxedArms,Measurement="Actual virtual mouse without RMB and W key through Input System: yaw, pitch, translation, ground, leg and both arm poses"};
            job.ResultPath=Folder+"/"+job.Id+".player.json";SessionState.SetString("WorldWeaver.PlayerCheck",job.Id);Save(job);
            EditorApplication.delayCall+=()=>{if(Load(job.Id)?.Status=="entering_play"&&!EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.EnterPlaymode();};return job;
#endif
        }
        static void PlayerCheckModeChanged(PlayModeStateChange mode)
        {
            var job=LoadActive();if(job?.Kind!="player_control"||!ActiveStatus(job.Status))return;
            if(mode==PlayModeStateChange.EnteredEditMode)
            {
                if(job.Status!="exiting_play"){job.Success=false;job.Error="Player check ended before input measurements completed.";}
                job.Status=job.Success?"completed":"failed";job.FinishedUtc=DateTime.UtcNow.ToString("o");Save(job);Atomic(job.ResultPath,Newtonsoft.Json.JsonConvert.SerializeObject(job,Newtonsoft.Json.Formatting.Indented));SessionState.EraseString("WorldWeaver.PlayerCheck");return;
            }
#if ENABLE_INPUT_SYSTEM
            if(mode==PlayModeStateChange.ExitingPlayMode)CleanupPlayerDevices();
            if(mode!=PlayModeStateChange.EnteredPlayMode)return;
            try
            {
                checkPlayer=ProjectContextIndex.ResolveStable(job.StableTarget).GetComponent<WorldPlayer>();if(!checkPlayer||!checkPlayer.View)throw new InvalidOperationException("Player/camera did not survive entering Play Mode.");
                oldBackground=InputSystem.settings.backgroundBehavior;oldRouting=InputSystem.settings.editorInputBehaviorInPlayMode;
                SessionState.SetInt("WorldWeaver.PlayerCheck.Background",(int)oldBackground);SessionState.SetInt("WorldWeaver.PlayerCheck.Routing",(int)oldRouting);
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                checkKeyboard=InputSystem.AddDevice<Keyboard>();checkMouse=InputSystem.AddDevice<Mouse>();checkKeyboard.MakeCurrent();checkMouse.MakeCurrent();
                InputSystem.QueueStateEvent(checkKeyboard,new KeyboardState());InputSystem.QueueStateEvent(checkMouse,new MouseState());
                checkPhase=0;checkBegan=Time.timeAsDouble;checkFrame=Time.frameCount;checkLegAngle=0;checkLeg=null;
                var animator=checkPlayer.GetComponentsInChildren<Animator>().FirstOrDefault(a=>a.avatar&&a.avatar.isValid&&a.avatar.isHuman);
                checkUpper=new Transform[2];checkElbows=new Transform[2];checkHands=new Transform[2];checkArmsMeasured=false;checkElbowBend=0;checkHandHeight=float.MinValue;
                if(animator)
                {
                    string name=animator.avatar.humanDescription.human.FirstOrDefault(h=>h.humanName=="LeftUpperLeg").boneName;
                    checkLeg=animator.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name==name);
                    Transform Bone(string human){string bone=animator.avatar.humanDescription.human.FirstOrDefault(h=>h.humanName==human).boneName;return animator.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name==bone);}
                    for(int i=0;i<2;i++){string side=i==0?"Left":"Right";checkUpper[i]=Bone(side+"UpperArm");checkElbows[i]=Bone(side+"LowerArm");checkHands[i]=Bone(side+"Hand");}
                }
                job.Status="running";Save(job);
            }
            catch(Exception error){EndPlayerCheck(job,error.Message);}
#endif
        }
        static void TickPlayerCheck()
        {
            var job=LoadActive();if(job?.Kind!="player_control"||!ActiveStatus(job.Status))return;
            if(DateTime.TryParse(job.StartedUtc,out var began)&&DateTime.UtcNow-began.ToUniversalTime()>TimeSpan.FromSeconds(90)){EndPlayerCheck(job,"Player check exceeded wall-clock budget.");return;}
#if ENABLE_INPUT_SYSTEM
            if(!EditorApplication.isPlaying||job.Status!="running"||Time.frameCount==checkFrame)return;
            checkFrame=Time.frameCount;
            try
            {
                if(!checkPlayer||!checkPlayer.View||checkKeyboard==null||checkMouse==null)throw new InvalidOperationException("Input check state was interrupted; no success inferred.");
                if(checkPhase==0)
                {
                    if(Time.timeAsDouble-checkBegan<.3)return;
                    checkPosition=checkPlayer.transform.position;checkCamera=checkPlayer.View.transform.eulerAngles;if(checkLeg)checkLegStart=checkLeg.localRotation;
                    InputSystem.QueueStateEvent(checkMouse,new MouseState{delta=new Vector2(80,60)});phaseFrame=Time.frameCount;checkPhase=1;return;
                }
                if(checkPhase==1)
                {
                    if(Time.frameCount-phaseFrame<2)return;
                    var rotation=checkPlayer.View.transform.eulerAngles;checkYaw=Mathf.DeltaAngle(checkCamera.y,rotation.y);checkPitch=Mathf.DeltaAngle(checkCamera.x,rotation.x);
                    InputSystem.QueueStateEvent(checkMouse,new MouseState());InputSystem.QueueStateEvent(checkKeyboard,new KeyboardState(Key.W));checkBegan=Time.timeAsDouble;checkPhase=2;return;
                }
                if(checkLeg)checkLegAngle=Mathf.Max(checkLegAngle,Quaternion.Angle(checkLegStart,checkLeg.localRotation));
                bool validArms=true;
                for(int i=0;i<2;i++)
                {
                    if(!checkUpper[i]||!checkElbows[i]||!checkHands[i]){validArms=false;continue;}
                    Vector3 upper=checkElbows[i].position-checkUpper[i].position,lower=checkHands[i].position-checkElbows[i].position;if(upper.sqrMagnitude<1e-8f||lower.sqrMagnitude<1e-8f){validArms=false;continue;}
                    checkElbowBend=Mathf.Max(checkElbowBend,Vector3.Angle(upper,lower));checkHandHeight=Mathf.Max(checkHandHeight,Vector3.Dot(checkHands[i].position-checkUpper[i].position,checkPlayer.transform.up));
                }
                checkArmsMeasured=validArms;
                if(Time.timeAsDouble-checkBegan<job.RequestedSeconds)return;
                Vector3 delta=checkPlayer.transform.position-checkPosition;float travel=new Vector2(delta.x,delta.z).magnitude;bool ground=Physics.Raycast(checkPlayer.transform.position+Vector3.up*.5f,Vector3.down,3);
                bool animation=!checkLeg||checkLegAngle>3;
                bool relaxed=checkArmsMeasured&&checkElbowBend<85&&checkHandHeight<.12f;
                var measurements=new JObject{["yawDegrees"]=checkYaw,["pitchDegrees"]=checkPitch,["horizontalTravel"]=travel,["groundBelowPlayer"]=ground,["legMeasured"]=checkLeg!=null,["legPoseChangeDegrees"]=checkLegAngle,["bothArmsMeasured"]=checkArmsMeasured,["maxElbowBendDegrees"]=checkArmsMeasured?(JToken)new JValue(checkElbowBend):JValue.CreateNull(),["highestHandRelativeToShoulder"]=checkArmsMeasured?(JToken)new JValue(checkHandHeight):JValue.CreateNull(),["relaxedWalkingArms"]=relaxed,["relaxedArmsRequired"]=job.RequireRelaxedArms,["mouseWithoutRmb"]=true,["realInputBackend"]=WorldInput.Backend};
                bool success=Mathf.Abs(checkYaw)>.02f&&Mathf.Abs(checkPitch)>.02f&&travel>.25f&&ground&&animation&&(!job.RequireRelaxedArms||relaxed);
                job.Measurement=measurements.ToString(Newtonsoft.Json.Formatting.None);EndPlayerCheck(job,success?null:"Actual player input failed; inspect yaw/pitch, movement, ground and leg measurements. Compiled code alone is not working controls.");
            }
            catch(Exception error){EndPlayerCheck(job,error.Message);}
#endif
        }
        static void EndPlayerCheck(AgentEditorJob job,string error)
        {
            job.Success=error==null;job.Error=ChatHistoryStore.Redact(error);job.Passed=job.Success?1:0;job.Failed=job.Success?0:1;
            job.Status=EditorApplication.isPlayingOrWillChangePlaymode?"exiting_play":job.Success?"completed":"failed";Save(job);Atomic(job.ResultPath,Newtonsoft.Json.JsonConvert.SerializeObject(job,Newtonsoft.Json.Formatting.Indented));
#if ENABLE_INPUT_SYSTEM
            CleanupPlayerDevices();
#endif
            if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.isPlaying=false;else{job.FinishedUtc=DateTime.UtcNow.ToString("o");Save(job);}
        }
#if ENABLE_INPUT_SYSTEM
        static void CleanupPlayerDevices()
        {
            if(checkKeyboard!=null){InputSystem.RemoveDevice(checkKeyboard);checkKeyboard=null;}if(checkMouse!=null){InputSystem.RemoveDevice(checkMouse);checkMouse=null;}
            InputSystem.settings.backgroundBehavior=(InputSettings.BackgroundBehavior)SessionState.GetInt("WorldWeaver.PlayerCheck.Background",(int)oldBackground);
            InputSystem.settings.editorInputBehaviorInPlayMode=(InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt("WorldWeaver.PlayerCheck.Routing",(int)oldRouting);
        }
#endif
    }
}
