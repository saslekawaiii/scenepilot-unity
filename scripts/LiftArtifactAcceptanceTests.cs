using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

// Observes model-authored output only. No gameplay source, event wiring or geometry repair.
public sealed class LiftArtifactAcceptanceTests
{
    Mouse mouse;InputSettings.BackgroundBehavior background;InputSettings.EditorInputBehaviorInPlayMode routing;
    [UnitySetUp] public IEnumerator Open()
    {
        Assert.AreEqual("1",Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE"));
        Assert.AreEqual(Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName),Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""));
        EditorSceneManager.OpenScene(Environment.GetEnvironmentVariable("WW_AGENT_SCENE"));yield return new EnterPlayMode();
        background=InputSystem.settings.backgroundBehavior;routing=InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        mouse=InputSystem.AddDevice<Mouse>();mouse.MakeCurrent();yield return null;
    }
    [UnityTearDown] public IEnumerator Close()
    {
        if(mouse!=null)InputSystem.RemoveDevice(mouse);InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=routing;
        Time.timeScale=1;yield return new ExitPlayMode();
    }
    static string Label(Button button)=>string.Join(" ",button.GetComponentsInChildren<Component>(true).Where(c=>c&&c.GetType().GetProperty("text")?.PropertyType==typeof(string)).Select(c=>(string)c.GetType().GetProperty("text").GetValue(c)));
    IEnumerator Click(Button button)
    {
        var rect=button.GetComponent<RectTransform>();var canvas=button.GetComponentInParent<Canvas>();Canvas.ForceUpdateCanvases();
        var point=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,rect.TransformPoint(rect.rect.center));
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=1});yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;
    }
    static IEnumerator WaitUnscaled(float seconds)
    {
        float until=Time.unscaledTime+seconds;double deadline=EditorApplication.timeSinceStartup+seconds+15;
        while(Time.unscaledTime<until&&EditorApplication.timeSinceStartup<deadline)yield return null;
        Assert.GreaterOrEqual(Time.unscaledTime,until-.03f);
    }
    [UnityTest] public IEnumerator RealVerticalCycleAndPointerPauseResume()
    {
        var candidates=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&Mathf.Abs(r.bounds.size.x-4)<.06f&&Mathf.Abs(r.bounds.size.y-.3f)<.03f&&Mathf.Abs(r.bounds.size.z-4)<.06f).ToArray();
        Assert.AreEqual(1,candidates.Length,"Expected one visible 4 x .3 x 4 platform");var platform=candidates[0];
        var cameras=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c=>c.enabled).ToArray();Assert.IsNotEmpty(cameras);var camera=Camera.main?Camera.main:cameras[0];
        Assert.IsTrue(GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera),platform.bounds),"Visible platform is outside the gameplay camera frustum");
        Assert.IsTrue(UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Any(l=>l.enabled));
        var buttons=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b=>Label(b).Contains("Пауза")).ToArray();Assert.AreEqual(1,buttons.Length);var button=buttons[0];
        Assert.IsTrue(EventSystem.current&&EventSystem.current.currentInputModule);
        float start=Time.unscaledTime,min=float.MaxValue,max=float.MinValue,prior=platform.bounds.center.y,priorTime=start;var crossings=new List<float>();
        while(Time.unscaledTime-start<17f)
        {
            yield return null;float now=Time.unscaledTime,y=platform.bounds.center.y;min=Mathf.Min(min,y);max=Mathf.Max(max,y);
            Assert.That(y,Is.InRange(.89f,5.11f),"Platform leaves requested vertical interval");
            if(prior<3&&y>=3)crossings.Add(Mathf.Lerp(priorTime,now,(3-prior)/(y-prior)));
            prior=y;priorTime=now;
        }
        Assert.AreEqual(1,min,.11f);Assert.AreEqual(5,max,.11f);Assert.GreaterOrEqual(crossings.Count,2,"No complete actual motion cycle");
        for(int i=1;i<crossings.Count;i++)Assert.AreEqual(8,crossings[i]-crossings[i-1],.35f,"Actual full period differs from eight seconds");
        // Pause in the moving interior, so a static endpoint cannot fake success.
        float deadline=Time.unscaledTime+8;while((platform.bounds.center.y<2||platform.bounds.center.y>4)&&Time.unscaledTime<deadline)yield return null;
        yield return Click(button);float paused=platform.bounds.center.y;Assert.That(Label(button),Does.Contain("Продолжить"));
        yield return WaitUnscaled(.9f);Assert.AreEqual(paused,platform.bounds.center.y,.015f,"Visible platform moved while paused");
        float resumeTime=Time.unscaledTime;yield return Click(button);float first=platform.bounds.center.y;
        Assert.Less(Mathf.Abs(first-paused),4*(Time.unscaledTime-resumeTime+.05f)+.05f,"Resume jumped away from the paused pose");Assert.That(Label(button),Does.Contain("Пауза"));
        yield return WaitUnscaled(.6f);Assert.Greater(Mathf.Abs(platform.bounds.center.y-paused),.12f,"Motion did not resume");
        string capture=Environment.GetEnvironmentVariable("WW_LIFT_CAPTURE");
        if(!string.IsNullOrEmpty(capture))
        {
            var previous=camera.targetTexture;var active=RenderTexture.active;var rt=new RenderTexture(1280,720,24);var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(capture,image.EncodeToPNG());Debug.Log("LIFT_CAPTURE actual gameplay camera world render; ScreenSpaceOverlay UI excluded: "+capture);}
            finally{camera.targetTexture=previous;RenderTexture.active=active;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
        Debug.Log($"LIFT_INPUT actual visible dimensions=4/.3/4, range={min:F3}..{max:F3}, measured periods={string.Join(",",crossings.Zip(crossings.Skip(1),(a,b)=>(b-a).ToString("F3")))}, pointer pause stayed at {paused:F3}, resumed without reset");
    }
}
