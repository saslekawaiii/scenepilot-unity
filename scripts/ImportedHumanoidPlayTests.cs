using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;

/// <summary>Actual input/pose/geometry checks of the real agent's saved imported character.</summary>
public sealed class ImportedHumanoidPlayTests
{
    Keyboard keyboard;
    InputSettings.BackgroundBehavior background;
    InputSettings.EditorInputBehaviorInPlayMode routing;
    [UnitySetUp] public IEnumerator Open()
    {
        string project=System.IO.Path.GetFullPath(System.IO.Directory.GetParent(Application.dataPath).FullName);
        Assert.AreEqual("1",Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE"));Assert.AreEqual(project,System.IO.Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""));
        string scene=Environment.GetEnvironmentVariable("WW_AGENT_SCENE");Assert.IsTrue(System.IO.File.Exists(scene),scene);EditorSceneManager.OpenScene(scene);yield return new EnterPlayMode();
        Application.runInBackground=true;background=InputSystem.settings.backgroundBehavior;routing=InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard=InputSystem.AddDevice<Keyboard>();keyboard.MakeCurrent();yield return new WaitForSeconds(.3f);
    }
    [UnityTearDown] public IEnumerator Close()
    {
        if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=routing;
        if(EditorApplication.isPlaying)yield return new ExitPlayMode();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    }
    [UnityTest] public IEnumerator ImportedHumanoidActuallyWalksAndItsBonesFollowTheController()
    {
        var player=UnityEngine.Object.FindFirstObjectByType<WorldPlayer>();Assert.IsNotNull(player);
        var gait=player.GetComponentInChildren<WorldHumanoidLocomotion>();Assert.IsNotNull(gait);Assert.IsTrue(gait.enabled);
        var animator=gait.GetComponent<Animator>();Assert.IsTrue(animator.avatar&&animator.avatar.isValid&&animator.avatar.isHuman);
        Debug.Log("HUMANOID_DIAGNOSTIC initialized="+animator.isInitialized+" controller="+(animator.runtimeAnimatorController?animator.runtimeAnimatorController.name:"none")+" mapping="+string.Join(",",animator.avatar.humanDescription.human.Select(h=>h.humanName+":"+h.boneName))+" skeleton="+string.Join(",",animator.GetComponentsInChildren<Transform>(true).Take(12).Select(t=>t.name)));
        // The procedural driver uses the actual imported Humanoid bone mapping.
        // Measure its mapped skinned bones directly; Animator.GetBoneTransform can
        // lack controller bindings even though the avatar hierarchy is valid.
        Transform Bone(string human)=>animator.GetComponentsInChildren<Transform>(true).SingleOrDefault(t=>t.name==animator.avatar.humanDescription.human.Single(h=>h.humanName==human).boneName);
        var hip=Bone("Hips");var leg=Bone("LeftUpperLeg");Assert.IsNotNull(hip);Assert.IsNotNull(leg);
        Assert.IsTrue(animator.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.bones.Contains(hip)&&r.bones.Contains(leg)),"Measurements must use bones actually referenced by the skinned mesh.");
        var proof=new AssistantMessage{Task=AgentTaskVerifier.Create("Создай главного героя с реальной моделью и анимацией ходьбы.",true)};
        proof.Steps.Add(new EditorCommandResult{Success=true,Tool="replace_model",Target=ProjectContextIndex.StableTarget(player.gameObject)});
        typeof(ChatController).GetMethod("VerifyTask",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{proof});
        Assert.IsTrue(proof.Task.Checks.All(c=>c.Passed),string.Join("; ",proof.Task.Checks.Select(c=>c.Description+": "+c.Actual)));
        Vector3 start=player.transform.position,hipStart=hip.position;Quaternion before=leg.localRotation;float angle=0;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));InputSystem.Update();float until=Time.time+1.2f;double wall=EditorApplication.timeSinceStartup+15;
        while(Time.time<until&&EditorApplication.timeSinceStartup<wall){angle=Mathf.Max(angle,Quaternion.Angle(before,leg.localRotation));yield return null;}
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSeconds(.2f);
        Vector3 delta=player.transform.position-start,hipDelta=hip.position-hipStart;
        Assert.Greater(new Vector2(delta.x,delta.z).magnitude,1.4f,"Actual keyboard input did not move the controller.");
        Debug.Log("POSE_DETAIL speed="+gait.MeasuredSpeed+" legAngle="+angle+" actorDelta="+delta+" hipDelta="+hipDelta+" legMuscles="+string.Join("|",HumanTrait.MuscleName.Where(n=>n.Contains("Leg"))));
        Assert.Greater(angle,3,"Humanoid leg pose did not animate during movement.");
        Assert.Less(new Vector2(hipDelta.x-delta.x,hipDelta.z-delta.z).magnitude,.35f,"The skinned body did not follow the moving actor.");
        Assert.IsTrue(Physics.Raycast(player.transform.position+Vector3.up*.4f,Vector3.down,2));
        Debug.Log("IMPORTED_HUMANOID_MOTION controller="+delta+" hips="+hipDelta+" legAngle="+angle);
        string capture=Environment.GetEnvironmentVariable("WW_MODEL_CAPTURE");
        if(!string.IsNullOrEmpty(capture)&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            var camera=new GameObject("Acceptance capture").AddComponent<Camera>();camera.transform.position=player.transform.position+new Vector3(2,1.4f,3);camera.transform.LookAt(player.transform.position+Vector3.up*.9f);camera.fieldOfView=36;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.09f,.11f);
            var rt=new RenderTexture(640,800,24);camera.targetTexture=rt;camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(640,800,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,640,800),0,0);image.Apply();System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(capture));System.IO.File.WriteAllBytes(capture,image.EncodeToPNG());RenderTexture.active=old;camera.targetTexture=null;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(camera.gameObject);
        }
    }
}
