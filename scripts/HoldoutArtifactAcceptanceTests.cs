using System;
using System.Collections;
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

// Independent behavior checks. This assembly observes unmodified agent output.
public sealed class HoldoutArtifactAcceptanceTests
{
    Mouse mouse;InputSettings.BackgroundBehavior background;InputSettings.EditorInputBehaviorInPlayMode routing;
    [UnitySetUp] public IEnumerator Open()
    {
        Assert.AreEqual("1",Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE"));
        Assert.AreEqual(Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName),Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""));
        EditorSceneManager.OpenScene(Environment.GetEnvironmentVariable("WW_AGENT_SCENE"));yield return new EnterPlayMode();
        background=InputSystem.settings.backgroundBehavior;routing=InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        mouse=InputSystem.AddDevice<Mouse>();mouse.MakeCurrent();yield return null;
    }
    [UnityTearDown] public IEnumerator Close()
    {
        if(mouse!=null)InputSystem.RemoveDevice(mouse);InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=routing;yield return new ExitPlayMode();
    }
    static IEnumerator Wait(float seconds)
    {
        float until=Time.time+seconds;double wall=EditorApplication.timeSinceStartup+20;
        while(Time.time<until&&EditorApplication.timeSinceStartup<wall)yield return null;
        Assert.GreaterOrEqual(Time.time,until-.05f);
    }
    IEnumerator Click(RectTransform rect,float normalized=.5f)
    {
        var canvas=rect.GetComponentInParent<Canvas>();Canvas.ForceUpdateCanvases();
        Vector2 point=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,rect.TransformPoint(new Vector3(Mathf.Lerp(rect.rect.xMin,rect.rect.xMax,normalized),rect.rect.center.y)));
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=1});yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;
    }
    [UnityTest] public IEnumerator ActualSliderAndMouseResetControlThePointLight()
    {
        var lights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>l.type==LightType.Point).ToArray();Assert.AreEqual(1,lights.Length);
        var light=lights[0];var slider=UnityEngine.Object.FindFirstObjectByType<Slider>();var button=UnityEngine.Object.FindFirstObjectByType<Button>();Assert.IsTrue(slider);Assert.IsTrue(button);Assert.IsTrue(EventSystem.current&&EventSystem.current.currentInputModule);
        Assert.AreEqual(0,slider.minValue);Assert.AreEqual(4,slider.maxValue);Assert.AreEqual(1,slider.value,.02f);Assert.AreEqual(1,light.intensity,.02f);
        // Drive the public UI value path across the full range, then verify actual pointer routing.
        foreach(float value in new[]{0f,2.5f,4f}){slider.value=value;yield return null;Assert.AreEqual(value,light.intensity,.02f,"Slider event is not wired to the actual light");}
        yield return Click(button.GetComponent<RectTransform>());Assert.AreEqual(1,slider.value,.02f,"Actual mouse click did not reset the slider");Assert.AreEqual(1,light.intensity,.02f);
        yield return Click(slider.GetComponent<RectTransform>(),.8f);Assert.Greater(slider.value,1.5f,"Mouse could not operate the visible slider");Assert.AreEqual(slider.value,light.intensity,.02f);
        Debug.Log("LIGHT_UI actual slider range and real pointer reset/slider interaction passed");
    }
    [UnityTest] public IEnumerator ActualGravityDropsThreeDifferentMassesOntoSolidSurfaces()
    {
        var staticSurfaces=UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c=>c.enabled&&!c.isTrigger&&!c.attachedRigidbody).ToArray();
        var upwardNormals=new System.Collections.Generic.List<Vector3>();
        foreach(var surface in staticSurfaces)
        {
            var bounds=surface.bounds;
            foreach(float x in new[]{.25f,.5f,.75f})foreach(float z in new[]{.25f,.5f,.75f})
            {
                var origin=new Vector3(Mathf.Lerp(bounds.min.x,bounds.max.x,x),bounds.max.y+1,Mathf.Lerp(bounds.min.z,bounds.max.z,z));
                if(surface.Raycast(new Ray(origin,Vector3.down),out var hit,bounds.size.y+2))upwardNormals.Add(hit.normal);
            }
            Debug.Log($"PHYSICS_STATIC_SURFACE {surface.name} actual collider bounds={bounds}");
        }
        Assert.IsTrue(upwardNormals.Any(n=>Vector3.Dot(n,Vector3.up)>.999f),"No actual flat physical surface observed");
        Assert.IsTrue(upwardNormals.Any(n=>Vector3.Dot(n,Vector3.up)>.15f&&Vector3.Dot(n,Vector3.up)<.999f),"No actual inclined physical ramp surface observed");
        var bodies=UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);Assert.AreEqual(3,bodies.Length);
        CollectionAssert.AreEquivalent(new[]{1f,2f,4f},bodies.Select(b=>b.mass).ToArray());Assert.IsTrue(bodies.All(b=>b.useGravity&&!b.isKinematic));
        var colors=bodies.Select(b=>b.GetComponent<Renderer>().sharedMaterial.color).ToArray();Assert.AreEqual(3,colors.Distinct().Count());
        foreach(var body in bodies){var col=body.GetComponent<Collider>();Assert.IsTrue(col&&!col.isTrigger);Assert.AreEqual(0,col.sharedMaterial?col.sharedMaterial.bounciness:0,.01f);}
        var before=bodies.Select(b=>b.position).ToArray();yield return Wait(.6f);
        Assert.IsTrue(bodies.Select((b,i)=>before[i].y-b.position.y).Any(d=>d>.15f),"No actual gravitational fall observed");
        // Observe beyond the previously missed 4.6-second edge failure. A short
        // landing snapshot does not establish continued support.
        float duration=30;float observationStart=Time.time,until=Time.time+duration;double wall=EditorApplication.timeSinceStartup+duration+20;
        while(Time.time<until&&EditorApplication.timeSinceStartup<wall)
        {
            yield return null;
            foreach(var body in bodies)
            {
                var col=body.GetComponent<Collider>();var support=Physics.RaycastAll(col.bounds.center+Vector3.up*.05f,Vector3.down,100,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider!=col).OrderBy(h=>h.distance).ToArray();
                Assert.IsNotEmpty(support,$"No ground under {body.name} at {body.position} after {Time.time-observationStart+.6f:F2} seconds");
                if(Physics.ComputePenetration(col,col.transform.position,col.transform.rotation,support[0].collider,support[0].collider.transform.position,support[0].collider.transform.rotation,out var direction,out float distance))Assert.LessOrEqual(distance,.12f,"Ball penetrated the actual support collider");
            }
        }
        Assert.GreaterOrEqual(Time.time,until-.05f,"Continuous support observation did not reach its requested duration");
        foreach(var body in bodies)Debug.Log($"PHYSICS_OBSERVATION name={body.name} position={body.position} velocity={body.linearVelocity} bounds={body.GetComponent<Collider>().bounds}");
        foreach(var col in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c=>!c.attachedRigidbody))Debug.Log($"PHYSICS_SUPPORT name={col.name} enabled={col.enabled} trigger={col.isTrigger} bounds={col.bounds}");
        foreach(var body in bodies)
        {
            var col=body.GetComponent<Collider>();var hits=Physics.RaycastAll(col.bounds.center+Vector3.up*.05f,Vector3.down,20,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider!=col).OrderBy(h=>h.distance).ToArray();Assert.IsNotEmpty(hits,"No ground under rigidbody "+body.name+" at "+body.position);
            var surfacePoint=hits[0].collider.ClosestPoint(col.bounds.center);Assert.LessOrEqual(Vector3.Distance(col.ClosestPoint(surfacePoint),surfacePoint),.12f,"Final ball pose is above ground but has not actually landed on it");
            if(Physics.ComputePenetration(col,col.transform.position,col.transform.rotation,hits[0].collider,hits[0].collider.transform.position,hits[0].collider.transform.rotation,out var direction,out float distance))Assert.LessOrEqual(distance,.12f,"Ball penetrated the actual support collider");
            var velocity=body.linearVelocity;Assert.IsFalse(float.IsNaN(velocity.x)||float.IsNaN(velocity.y)||float.IsNaN(velocity.z)||float.IsInfinity(velocity.x)||float.IsInfinity(velocity.y)||float.IsInfinity(velocity.z),"Non-finite simulated velocity");
            // The request requires remaining on a physical surface, not coming
            // to rest. Rolling along a ramp or a flat floor is valid behavior.
            Debug.Log($"PHYSICS_BODY {body.name}: mass={body.mass} height={body.position.y:F3} velocity={velocity.magnitude:F3} support={hits[0].collider.name}");
        }
        Debug.Log("PHYSICS actual gravity, masses 1/2/4, distinct colors, zero bounce and continuously observed solid support for 30 seconds after initial fall passed; complete rest was not a user requirement");
    }
}
