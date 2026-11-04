using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;
using WorldWeaver;

// Black-box acceptance of the agent's saved artifact. No scripts/scene repair.
public sealed class WarehouseResultAcceptanceTests
{
    Keyboard keyboard; Mouse mouse; WorldPlayer player; CharacterController motor;
    InputSettings.BackgroundBehavior background; InputSettings.EditorInputBehaviorInPlayMode routing;
    [UnitySetUp] public IEnumerator Open()
    {
        Assert.AreEqual("1",Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE"));
        Assert.AreEqual(Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName),Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""));
        string path=Environment.GetEnvironmentVariable("WW_AGENT_SCENE");Assert.IsTrue(File.Exists(path),path);
        EditorSceneManager.OpenScene(path);yield return new EnterPlayMode();
        background=InputSystem.settings.backgroundBehavior;routing=InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();keyboard.MakeCurrent();mouse.MakeCurrent();
        player=UnityEngine.Object.FindFirstObjectByType<WorldPlayer>();Assert.IsTrue(player,"Saved scene needs a player, not just a successful request status");motor=player.GetComponent<CharacterController>();
        yield return Wait(.4f);
    }
    [UnityTearDown] public IEnumerator Close()
    {
        if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);
        InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=routing;
        yield return new ExitPlayMode();
    }
    static IEnumerator Wait(float seconds)
    {
        float until=Time.time+seconds;double wall=EditorApplication.timeSinceStartup+20;
        while(Time.time<until&&EditorApplication.timeSinceStartup<wall)yield return null;
        Assert.GreaterOrEqual(Time.time,until-.05f,"Play Mode did not advance");
    }
    void Place(Vector3 point){motor.enabled=false;player.transform.position=point;motor.enabled=true;Physics.SyncTransforms();}
    Vector3 ClearInputSite()
    {
        var floor=UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None).Where(c=>!c.isTrigger&&c.bounds.size.x>=20&&c.bounds.size.z>=12&&c.bounds.size.y<1).First();var b=floor.bounds;
        bool Other(Collider c)=>c!=floor&&c!=motor&&!c.transform.IsChildOf(player.transform);
        for(float z=b.min.z+1.5f;z<b.max.z-1.5f;z+=.5f)for(float x=b.min.x+1.5f;x<b.max.x-1.5f;x+=.5f)
        {
            Vector3 ground=new Vector3(x,b.max.y+.08f,z),bottom=ground+Vector3.up*motor.radius,top=ground+Vector3.up*Mathf.Max(motor.height-motor.radius,motor.radius);float radius=motor.radius*.95f;
            if(Physics.OverlapCapsule(bottom,top,radius,~0,QueryTriggerInteraction.Ignore).Any(Other))continue;
            if(new[]{player.transform.forward,-player.transform.forward,player.transform.right,-player.transform.right}.Any(d=>Physics.CapsuleCastAll(bottom,top,radius,d,1.4f,~0,QueryTriggerInteraction.Ignore).Any(h=>Other(h.collider))))continue;
            return ground+Vector3.up*(motor.height*.5f-motor.center.y);
        }
        Assert.Fail("No clear floor location for independent movement measurements");return Vector3.zero;
    }
    [UnityTest] public IEnumerator RealKeyboardMovementSprintJumpCameraAndSolidObstacles()
    {
        Assert.IsTrue(player.View);Vector3 originalSpawn=player.transform.position;Vector3 home=ClearInputSite();Place(home);yield return Wait(.2f);Debug.Log("WAREHOUSE_INPUT_SETUP originalSpawn="+originalSpawn+" clear measurement site="+home+"; geometry is not changed");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));Vector3 start=player.transform.position;yield return Wait(.14f);
        float walk=Vector3.ProjectOnPlane(player.transform.position-start,Vector3.up).magnitude;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;Place(home);yield return Wait(.1f);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.LeftShift));start=player.transform.position;yield return Wait(.14f);
        float sprint=Vector3.ProjectOnPlane(player.transform.position-start,Vector3.up).magnitude;
        Assert.Greater(walk,.3f,"W key did not move the player");Assert.Greater(sprint,walk*1.15f,"Shift did not increase actual travel");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        foreach(var direction in new[]{Key.A,Key.S,Key.D})
        {
            Place(home);yield return Wait(.1f);start=player.transform.position;InputSystem.QueueStateEvent(keyboard,new KeyboardState(direction));yield return Wait(.14f);
            Assert.Greater(Vector3.ProjectOnPlane(player.transform.position-start,Vector3.up).magnitude,.25f,direction+" produced no movement");InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        }
        Place(home);yield return Wait(.25f);
        float initialY=player.transform.position.y,maxY=initialY;InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Space));yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());float until=Time.time+.8f;
        while(Time.time<until){maxY=Mathf.Max(maxY,player.transform.position.y);yield return null;}
        Assert.Greater(maxY-initialY,.2f,"Space did not produce an actual jump");yield return Wait(.5f);Assert.IsTrue(player.IsGrounded,"Player did not return to ground");
        Vector3 before=player.View.transform.eulerAngles;
        InputSystem.QueueStateEvent(mouse,new MouseState{buttons=2,delta=new Vector2(60,35)});yield return null;
        float yaw=Mathf.Abs(Mathf.DeltaAngle(before.y,player.View.transform.eulerAngles.y)),pitch=Mathf.Abs(Mathf.DeltaAngle(before.x,player.View.transform.eulerAngles.x));
        Assert.Greater(yaw,.1f);Assert.Greater(pitch,.1f);InputSystem.QueueStateEvent(mouse,new MouseState());yield return null;
        var obstacles=UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None).Where(c=>!c.isTrigger&&c.enabled&&c.bounds.size.y>1&&c.bounds.size.x*c.bounds.size.z>1).ToArray();
        Assert.GreaterOrEqual(obstacles.Length,5,"Walls and shelves need solid colliders");
        var obstacle=obstacles.Where(c=>c.bounds.size.z>5).OrderBy(c=>c.bounds.size.x).First();var b=obstacle.bounds;
        // Arrange the actor outside an actual obstacle, then move exclusively through the input path.
        Vector3 toward=Vector3.right;Vector3 approach=new Vector3(b.min.x-motor.radius-.3f,home.y,b.center.z);
        Place(approach);yield return Wait(.15f);
        // Choose the real movement key nearest the obstacle direction under the current yaw.
        Vector3 local=player.transform.InverseTransformDirection(toward);Key key=Mathf.Abs(local.x)>Mathf.Abs(local.z)?(local.x>0?Key.D:Key.A):(local.z>0?Key.W:Key.S);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return Wait(.8f);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        Assert.Less(player.transform.position.x,b.min.x+motor.radius,"Character crossed a solid obstacle");
        Debug.Log($"WAREHOUSE_INPUT walk={walk:F3} sprint={sprint:F3} jump={maxY-initialY:F3} yaw={yaw:F2} pitch={pitch:F2} obstacle={obstacle.name}");
    }
    void AssertParcelsReachable(GameObject[] parcels)
    {
        var floor=UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c=>!c.isTrigger&&c!=motor&&c.bounds.size.x>=20&&c.bounds.size.z>=12&&c.bounds.size.y<1).OrderByDescending(c=>c.bounds.size.x*c.bounds.size.z).FirstOrDefault();
        Assert.IsTrue(floor,"No approximately 30x20 solid floor");Bounds bounds=floor.bounds;Assert.That(bounds.size.x,Is.InRange(27,33));Assert.That(bounds.size.z,Is.InRange(18,22));
        const float step=.5f;int nx=Mathf.FloorToInt(bounds.size.x/step),nz=Mathf.FloorToInt(bounds.size.z/step);
        var reachable=new HashSet<Vector2Int>();var queue=new Queue<Vector2Int>();
        Vector2Int Grid(Vector3 p)=>new Vector2Int(Mathf.Clamp(Mathf.RoundToInt((p.x-bounds.min.x)/step),0,nx),Mathf.Clamp(Mathf.RoundToInt((p.z-bounds.min.z)/step),0,nz));
        Vector3 Point(Vector2Int p)=>new Vector3(bounds.min.x+p.x*step,bounds.max.y,bounds.min.z+p.y*step);
        bool Free(Vector2Int p)
        {
            Vector3 ground=Point(p);float radius=motor.radius*.95f;
            return !Physics.OverlapCapsule(ground+Vector3.up*(radius+.07f),ground+Vector3.up*(Mathf.Max(motor.height-radius,radius)+.07f),radius,~0,QueryTriggerInteraction.Ignore).Any(c=>c!=floor&&c!=motor&&!c.transform.IsChildOf(player.transform));
        }
        Vector2Int start=Grid(player.transform.position);Assert.IsTrue(Free(start),"Initial spawn is inside solid geometry");reachable.Add(start);queue.Enqueue(start);
        var directions=new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down};
        while(queue.Count>0)
        {
            var current=queue.Dequeue();foreach(var d in directions){var next=current+d;if(next.x<0||next.x>nx||next.y<0||next.y>nz||reachable.Contains(next)||!Free(next))continue;reachable.Add(next);queue.Enqueue(next);}
        }
        foreach(var parcel in parcels)Assert.IsTrue(reachable.Any(p=>Vector2.Distance(new Vector2(Point(p).x,Point(p).z),new Vector2(parcel.transform.position.x,parcel.transform.position.z))<1.5f),"No floor path within interaction distance of "+parcel.name);
        Debug.Log("WAREHOUSE_REACHABILITY half-metre capsule grid from actual spawn, reachable cells="+reachable.Count+"; pickup input setup may teleport only after this independent path check");
    }
    [UnityTest] public IEnumerator FiveActualEPickupsIncrementOnceAndCompleteOnlyAfterTheFifth()
    {
        var parcels=UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Where(r=>Regex.IsMatch(r.name,"parcel|package|посыл",RegexOptions.IgnoreCase)&&r.bounds.size.magnitude<4).Select(r=>r.gameObject).Distinct().ToArray();
        Assert.AreEqual(5,parcels.Length,"Exactly five visible parcel objects must exist");AssertParcelsReachable(parcels);
        var texts=UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        var counter=texts.SingleOrDefault(t=>Regex.IsMatch(t.name+" "+t.text,"counter|счёт|счет|посыл|собр",RegexOptions.IgnoreCase)&&Regex.Match(t.text??"",@"\d+").Value=="0");Assert.IsTrue(counter,"No visible initial collection counter bound at runtime");Assert.IsTrue(counter.isActiveAndEnabled);
        int Count()=>int.Parse(Regex.Match(counter.text??"",@"\d+").Value);
        bool Completed()=>texts.Any(t=>t&&t.isActiveAndEnabled&&(t.text??"").Contains("Все посылки собраны"));Assert.IsFalse(Completed());
        for(int i=0;i<parcels.Length;i++)
        {
            var parcel=parcels[i];Vector3 p=parcel.transform.position;Place(new Vector3(p.x,player.transform.position.y,p.z-.7f));yield return Wait(.2f);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));yield return Wait(.15f);
            Assert.IsTrue(!parcel||!parcel.activeInHierarchy,"E did not hide the actual nearby parcel");
            Assert.AreEqual(i+1,Count());
            yield return Wait(.2f);Assert.AreEqual(i+1,Count(),"Held E counted twice");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            Assert.AreEqual(i==4,Completed(),"Completion shown at wrong collection count");
        }
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        Assert.AreEqual(5,Count());Debug.Log("WAREHOUSE_PICKUPS five actual E inputs, no held-key duplicate, completion only after fifth");
        string capture=Environment.GetEnvironmentVariable("WW_MODEL_CAPTURE");
        if(!string.IsNullOrEmpty(capture)&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            yield return null;var image=ScreenCapture.CaptureScreenshotAsTexture();Directory.CreateDirectory(Path.GetDirectoryName(capture));File.WriteAllBytes(capture,image.EncodeToPNG());UnityEngine.Object.Destroy(image);
        }
    }
}
