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

/// <summary>Physical input checks in the saved scene created by the real agent.
/// Only an explicitly identified isolated project may run these tests.</summary>
public class GeneratedAgentScenePlayTests
{
    Keyboard keyboard;
    InputSettings.BackgroundBehavior background; InputSettings.EditorInputBehaviorInPlayMode routing; bool runInBackground;
    [UnitySetUp] public IEnumerator OpenActualGeneratedScene()
    {
        string project=System.IO.Path.GetFullPath(System.IO.Directory.GetParent(Application.dataPath).FullName);
        Assert.AreEqual("1",Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE"));
        Assert.AreEqual(project,System.IO.Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""));
        string scene=Environment.GetEnvironmentVariable("WW_AGENT_SCENE");Assert.IsTrue(System.IO.File.Exists(scene),scene);
        EditorSceneManager.OpenScene(scene);yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying,"The test must run in actual Play Mode.");
        background=InputSystem.settings.backgroundBehavior;routing=InputSystem.settings.editorInputBehaviorInPlayMode;runInBackground=Application.runInBackground;
        Application.runInBackground=true;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard=InputSystem.AddDevice<Keyboard>();keyboard.MakeCurrent();yield return null;
    }
    [UnityTearDown] public IEnumerator CloseActualGeneratedScene()
    {
        if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
        InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=routing;Application.runInBackground=runInBackground;
        if(EditorApplication.isPlaying)yield return new ExitPlayMode();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    }
    [UnityTest] public IEnumerator PlayerWalksOutOfHabitatUsingKeyboardOnTheGeneratedSurface()
    {
        var player=UnityEngine.Object.FindFirstObjectByType<WorldPlayer>();Assert.IsNotNull(player);
        yield return new WaitForSeconds(.25f);Vector3 before=player.transform.position;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.S));InputSystem.Update();Assert.Greater(WorldInput.Movement.sqrMagnitude,.5f);float until=Time.time+1.4f;double wall=EditorApplication.timeSinceStartup+12;while(Time.time<until&&EditorApplication.timeSinceStartup<wall)yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSeconds(.2f);
        Vector3 delta=player.transform.position-before;
        Assert.Greater(new Vector2(delta.x,delta.z).magnitude,3,"Keyboard movement was blocked in the actual generated scene.");
        Assert.Less(Mathf.Abs(delta.y),2,"Player fell through the generated surface.");
        Assert.IsTrue(Physics.Raycast(player.transform.position+Vector3.up*.3f,Vector3.down,2));
        Assert.IsNotNull(player.GetComponent<WorldSurvival>());
    }
    [UnityTest] public IEnumerator PlayerEntersTheGeneratedCaveThroughItsPhysicalRamp()
    {
        var player=UnityEngine.Object.FindFirstObjectByType<WorldPlayer>();var cave=UnityEngine.Object.FindObjectsByType<WorldCave>(FindObjectsSortMode.None).First();
        Assert.IsNotNull(player);var motor=player.GetComponent<CharacterController>();
        // Place the existing controller at the reported entrance, then use actual
        // keyboard movement/collision. No geometry or character settings are patched.
        player.enabled=false;motor.enabled=false;player.transform.position=cave.Entrance+Vector3.up*.15f;player.transform.rotation=Quaternion.identity;motor.enabled=true;player.enabled=true;
        yield return new WaitForSeconds(.3f);Vector3 before=player.transform.position;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));InputSystem.Update();Assert.Greater(WorldInput.Movement.sqrMagnitude,.5f);double wall=EditorApplication.timeSinceStartup+30;while(player.transform.position.z<cave.Interior.z-.4f&&EditorApplication.timeSinceStartup<wall)yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSeconds(.4f);
        Debug.Log("AGENT_CAVE_MOTION before="+before+" after="+player.transform.position+" rotation="+player.transform.eulerAngles+" velocity="+motor.velocity+" locked="+player.MovementLocked+" cave="+cave.transform.position+" size="+cave.Size);
        if(Physics.SphereCast(player.transform.position+Vector3.up*.9f,.32f,Vector3.forward,out var obstruction,5))Debug.Log("AGENT_CAVE_OBSTRUCTION "+obstruction.collider.name+" type="+obstruction.collider.GetType().Name+" at="+obstruction.point+" distance="+obstruction.distance);
        foreach(var overlap in Physics.OverlapCapsule(player.transform.position+Vector3.up*.3f,player.transform.position+Vector3.up*1.5f,.45f))Debug.Log("AGENT_CAVE_OVERLAP "+overlap.name+" type="+overlap.GetType().Name);
        Assert.GreaterOrEqual(player.transform.position.z,cave.Interior.z-.75f,"The character could not reach the reported cave interior through the entrance.");
        Assert.Less(player.transform.position.y,before.y-1,"The character did not descend the ramp.");
        Assert.IsTrue(Physics.Raycast(player.transform.position+Vector3.up*.3f,Vector3.down,out var hit,2));
        Assert.AreEqual(cave.gameObject,hit.collider.gameObject,"The cave floor must provide physical support.");
    }
}
