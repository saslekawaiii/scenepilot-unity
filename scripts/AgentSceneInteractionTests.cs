using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using WorldWeaver;

/// <summary>Real virtual-device input against the unmodified saved agent result.</summary>
public sealed class AgentSceneInteractionTests
{
    Keyboard keyboard;Mouse mouse;WorldPlayer player;
    InputSettings.BackgroundBehavior background;InputSettings.EditorInputBehaviorInPlayMode routing;
    [UnitySetUp] public IEnumerator Open()
    {
        Assert.AreEqual("1",Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE"));
        Assert.AreEqual(Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName),Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""));
        string path=Environment.GetEnvironmentVariable("WW_AGENT_SCENE");Assert.IsTrue(File.Exists(path),path);
        EditorSceneManager.OpenScene(path);yield return new EnterPlayMode();
        background=InputSystem.settings.backgroundBehavior;routing=InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();keyboard.MakeCurrent();mouse.MakeCurrent();
        player=UnityEngine.Object.FindObjectsByType<WorldPlayer>(FindObjectsSortMode.None).Single();
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.QueueStateEvent(mouse,new MouseState());yield return null;
    }
    [UnityTearDown] public IEnumerator Close()
    {
        if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);
        InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=routing;
        yield return new ExitPlayMode();
    }
    [UnityTest] public IEnumerator AgentCameraRespondsOnBothAxesWithoutRmbAndPoseSurvivesFourDirections()
    {
        Assert.IsTrue(player.View);var view=player.View.transform;
        Vector3 original=view.eulerAngles;
        InputSystem.QueueStateEvent(mouse,new MouseState{delta=new Vector2(80,60)});yield return null;
        Vector3 changed=view.eulerAngles;float yaw=Mathf.DeltaAngle(original.y,changed.y),pitch=Mathf.DeltaAngle(original.x,changed.x);
        Assert.Greater(Mathf.Abs(yaw),.02f,"Camera did not respond horizontally without RMB");Assert.Greater(Mathf.Abs(pitch),.02f,"Camera did not respond vertically without RMB");
        InputSystem.QueueStateEvent(mouse,new MouseState{delta=new Vector2(-80,-60)});yield return null;
        Assert.Less(Mathf.Abs(Mathf.DeltaAngle(original.y,view.eulerAngles.y)),1,"Reverse mouse yaw");
        Assert.Less(Mathf.Abs(Mathf.DeltaAngle(original.x,view.eulerAngles.x)),1,"Reverse mouse pitch");
        InputSystem.QueueStateEvent(mouse,new MouseState());yield return null;
        var animator=player.GetComponentsInChildren<Animator>().First(a=>a.avatar&&a.avatar.isHuman);var hierarchy=animator.GetComponentsInChildren<Transform>();
        Transform Bone(HumanBodyBones id){string name=animator.avatar.humanDescription.human.First(h=>h.humanName==id.ToString()).boneName;return hierarchy.Single(t=>t.name==name);}
        Transform hip=Bone(HumanBodyBones.Hips),leg=Bone(HumanBodyBones.LeftUpperLeg);float maxElbow=0,maxLegMotion=0,totalTravel=0;Quaternion legStart=leg.localRotation;
        foreach(var key in new[]{Key.W,Key.D,Key.S,Key.A})
        {
            Vector3 before=player.transform.position,hipBefore=hip.position;InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));float until=Time.time+.65f;double wall=EditorApplication.timeSinceStartup+15;
            while(Time.time<until&&EditorApplication.timeSinceStartup<wall)
            {
                foreach(var side in new[]{new[]{HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand},new[]{HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand}})
                {
                    var upper=Bone(side[0]);var elbow=Bone(side[1]);var wrist=Bone(side[2]);float angle=Vector3.Angle(elbow.position-upper.position,wrist.position-elbow.position);maxElbow=Mathf.Max(maxElbow,angle);
                    Assert.Less(angle,85,"Arm bends/twists outside relaxed walking envelope");Assert.Less(wrist.position.y,upper.position.y+.12f,"Walking hand above shoulder");
                }
                maxLegMotion=Mathf.Max(maxLegMotion,Quaternion.Angle(legStart,leg.localRotation));yield return null;
            }
            var delta=player.transform.position-before;var hipDelta=hip.position-hipBefore;totalTravel+=new Vector2(delta.x,delta.z).magnitude;
            Assert.Less((new Vector2(delta.x-hipDelta.x,delta.z-hipDelta.z)).magnitude,.35f,"Skinned body detached from actor");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        }
        Assert.Greater(totalTravel,2,"Actual saved player cannot move on the resulting world");Assert.Greater(maxLegMotion,3,"No leg animation");
        Assert.IsTrue(Physics.Raycast(player.transform.position+Vector3.up*.5f,Vector3.down,3),"No physical ground under player");
        Debug.Log("AGENT_SCENE_INPUT yaw="+yaw+" pitch="+pitch+" travel="+totalTravel+" elbowMax="+maxElbow+" legMotion="+maxLegMotion);
        string output=Environment.GetEnvironmentVariable("WW_MODEL_CAPTURE");
        if(!string.IsNullOrEmpty(output)&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            var camera=new GameObject("Read-only acceptance camera").AddComponent<Camera>();camera.transform.position=player.transform.position+new Vector3(1.6f,1.3f,3);camera.transform.LookAt(player.transform.position+Vector3.up*.9f);camera.fieldOfView=38;
            var rt=new RenderTexture(720,900,24);camera.targetTexture=rt;camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(720,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,720,900),0,0);image.Apply();Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllBytes(output,image.EncodeToPNG());RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(camera.gameObject);
        }
    }
}
