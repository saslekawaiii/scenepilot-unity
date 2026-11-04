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
    Keyboard keyboard; Mouse mouse; GameObject player; Camera view; CharacterController motor;
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
        motor=UnityEngine.Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None).OrderByDescending(c=>c.GetComponent<WorldPlayer>()?2:c.GetComponentInChildren<Camera>()?1:0).FirstOrDefault();
        Assert.IsTrue(motor,"This input fixture requires an actual capsule CharacterController actor");player=motor.gameObject;
        var builtIn=player.GetComponent<WorldPlayer>();view=builtIn?builtIn.View:player.GetComponentInChildren<Camera>();if(!view)view=Camera.main;
        Assert.IsTrue(view,"No actual gameplay camera");
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
        var floor=UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c=>!c.isTrigger&&c!=motor&&c.bounds.size.x>=20&&c.bounds.size.z>=12&&c.bounds.size.y<1).First();var b=floor.bounds;
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
        Assert.IsTrue(view);Vector3 originalSpawn=player.transform.position;Vector3 home=ClearInputSite();Place(home);yield return Wait(.2f);Debug.Log("WAREHOUSE_INPUT_SETUP originalSpawn="+originalSpawn+" clear measurement site="+home+"; geometry is not changed");
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
        Assert.Greater(maxY-initialY,.2f,"Space did not produce an actual jump");yield return Wait(.5f);Assert.IsTrue(motor.isGrounded,"Player did not return to ground");
        Vector3 before=view.transform.eulerAngles;
        InputSystem.QueueStateEvent(mouse,new MouseState{buttons=2,delta=new Vector2(60,35)});yield return null;
        float yaw=Mathf.Abs(Mathf.DeltaAngle(before.y,view.transform.eulerAngles.y)),pitch=Mathf.Abs(Mathf.DeltaAngle(before.x,view.transform.eulerAngles.x));
        Assert.Greater(yaw,.1f);Assert.Greater(pitch,.1f);InputSystem.QueueStateEvent(mouse,new MouseState());yield return null;
        var obstacles=UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None).Where(c=>!c.isTrigger&&c.enabled&&c.bounds.size.y>1&&c.bounds.size.x*c.bounds.size.z>1).ToArray();
        Assert.GreaterOrEqual(obstacles.Length,5,"Walls and shelves need solid colliders");
        var floor=UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c=>!c.isTrigger&&c!=motor&&c.bounds.size.x>=20&&c.bounds.size.z>=12&&c.bounds.size.y<1).OrderByDescending(c=>c.bounds.size.x*c.bounds.size.z).First();
        var floorBounds=floor.bounds;
        // Use an interior shelf: starting beyond an exterior wall could trigger a
        // player's void/bounds recovery instead of exercising obstacle collision.
        var obstacle=obstacles.Where(c=>Mathf.Abs(c.bounds.center.x-floorBounds.center.x)<floorBounds.extents.x*.8f&&Mathf.Abs(c.bounds.center.z-floorBounds.center.z)<floorBounds.extents.z*.8f).OrderBy(c=>Vector3.Distance(c.bounds.center,floorBounds.center)).FirstOrDefault();
        Assert.IsTrue(obstacle,"No interior shelf collider for an in-bounds collision test");var b=obstacle.bounds;
        Vector3 normal=b.size.x<b.size.z?Vector3.right:Vector3.forward;
        if(Vector3.Dot(floorBounds.center-b.center,normal)<0)normal=-normal;
        float extent=Mathf.Abs(normal.x)*b.extents.x+Mathf.Abs(normal.z)*b.extents.z;
        Vector3 face=b.center+normal*extent,toward=-normal;
        Vector3 approach=face+normal*(motor.radius+.3f);approach.y=home.y;
        Assert.IsTrue(floorBounds.Contains(new Vector3(approach.x,floorBounds.center.y,approach.z)),"Collision approach must be on the floor");
        Place(approach);yield return Wait(.15f);
        Vector3 collisionStart=player.transform.position;
        Vector3 local=player.transform.InverseTransformDirection(toward);Key key=Mathf.Abs(local.x)>Mathf.Abs(local.z)?(local.x>0?Key.D:Key.A):(local.z>0?Key.W:Key.S);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return Wait(.8f);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        Assert.Greater(Vector3.Dot(player.transform.position-face,normal),-motor.radius,"Character crossed an interior solid shelf");
        float collisionApproach=Vector3.Dot(player.transform.position-collisionStart,toward);
        Assert.Greater(collisionApproach,.12f,"The collision assertion must exercise actual movement toward the shelf");
        Debug.Log($"WAREHOUSE_INPUT walk={walk:F3} sprint={sprint:F3} jump={maxY-initialY:F3} yaw={yaw:F2} pitch={pitch:F2} obstacle={obstacle.name} collisionApproach={collisionApproach:F3}");
    }
    void AssertParcelsReachable(GameObject[] parcels)
    {
        var floor=UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c=>!c.isTrigger&&c!=motor&&c.bounds.size.x>=20&&c.bounds.size.z>=12&&c.bounds.size.y<1).OrderByDescending(c=>c.bounds.size.x*c.bounds.size.z).FirstOrDefault();
        Assert.IsTrue(floor,"No approximately 30x20 solid floor");Bounds bounds=floor.bounds;Assert.That(bounds.size.x,Is.InRange(27,33));Assert.That(bounds.size.z,Is.InRange(18,22));
        const float step=.5f;int nx=Mathf.FloorToInt(bounds.size.x/step),nz=Mathf.FloorToInt(bounds.size.z/step);
        var reachable=new HashSet<Vector2Int>();var queue=new Queue<Vector2Int>();
        Vector2Int Grid(Vector3 p)=>new Vector2Int(Mathf.Clamp(Mathf.RoundToInt((p.x-bounds.min.x)/step),0,nx),Mathf.Clamp(Mathf.RoundToInt((p.z-bounds.min.z)/step),0,nz));
        Vector3 Point(Vector2Int p)=>new Vector3(bounds.min.x+p.x*step,bounds.max.y,bounds.min.z+p.y*step);
        float radius=Mathf.Max(.05f,motor.radius-motor.skinWidth);
        bool Other(Collider c)=>c!=floor&&c!=motor&&!c.transform.IsChildOf(player.transform);
        Vector3 Bottom(Vector3 p)=>new Vector3(p.x,bounds.max.y+radius+.07f,p.z);
        Vector3 Top(Vector3 p)=>new Vector3(p.x,bounds.max.y+Mathf.Max(motor.height-radius,radius)+.07f,p.z);
        bool FreePoint(Vector3 p)=>!Physics.OverlapCapsule(Bottom(p),Top(p),radius,~0,QueryTriggerInteraction.Ignore).Any(Other);
        bool Free(Vector2Int p)=>FreePoint(Point(p));
        Vector3 actualStart=player.transform.position;Assert.IsTrue(FreePoint(actualStart),"Runtime starting capsule overlaps solid geometry beyond its allowed skin width; position="+actualStart+" radius="+radius+" skin="+motor.skinWidth);
        // Grid rounding must not move a valid starting actor into a parcel. Seed
        // only nearby free cells actually reachable by a capsule sweep from it.
        Vector2Int rounded=Grid(actualStart);
        for(int dx=-2;dx<=2;dx++)for(int dz=-2;dz<=2;dz++)
        {
            var candidate=rounded+new Vector2Int(dx,dz);if(candidate.x<0||candidate.x>nx||candidate.y<0||candidate.y>nz||!Free(candidate))continue;
            Vector3 delta=Point(candidate)-new Vector3(actualStart.x,bounds.max.y,actualStart.z);float distance=delta.magnitude;
            if(distance>0&&Physics.CapsuleCastAll(Bottom(actualStart),Top(actualStart),radius,delta/distance,distance,~0,QueryTriggerInteraction.Ignore).Any(h=>Other(h.collider)))continue;
            if(reachable.Add(candidate))queue.Enqueue(candidate);
        }
        Assert.IsNotEmpty(reachable,"No unobstructed connection from the real starting pose to the navigation grid");
        var directions=new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down};
        while(queue.Count>0)
        {
            var current=queue.Dequeue();foreach(var d in directions)
            {
                var next=current+d;if(next.x<0||next.x>nx||next.y<0||next.y>nz||reachable.Contains(next)||!Free(next))continue;
                Vector3 delta=Point(next)-Point(current);
                if(Physics.CapsuleCastAll(Bottom(Point(current)),Top(Point(current)),radius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore).Any(h=>Other(h.collider)))continue;
                reachable.Add(next);queue.Enqueue(next);
            }
        }
        foreach(var parcel in parcels)Assert.IsTrue(reachable.Any(p=>Vector2.Distance(new Vector2(Point(p).x,Point(p).z),new Vector2(parcel.transform.position.x,parcel.transform.position.z))<1.5f),"No floor path within interaction distance of "+parcel.name);
        Debug.Log("WAREHOUSE_REACHABILITY half-metre capsule grid connected to actual Play starting pose, reachable cells="+reachable.Count+"; effective radius="+radius+"; input arrangements do not change geometry");
    }
    [UnityTest] public IEnumerator AllFiveParcelsHavePathsFromTheActualPlayStartingPose()
    {
        var parcels=UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Where(r=>Regex.IsMatch(r.name+" "+string.Join(" ",r.GetComponents<MonoBehaviour>().Where(c=>c).Select(c=>c.GetType().Name)),"parcel|package|pickup|collectible|посыл",RegexOptions.IgnoreCase)&&r.bounds.size.magnitude<4).Select(r=>r.gameObject).Distinct().ToArray();
        Assert.AreEqual(5,parcels.Length);AssertParcelsReachable(parcels);yield return null;
    }
    [UnityTest] public IEnumerator FiveActualEPickupsIncrementOnceAndCompleteOnlyAfterTheFifth()
    {
        var parcels=UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Where(r=>Regex.IsMatch(r.name+" "+string.Join(" ",r.GetComponents<MonoBehaviour>().Where(c=>c).Select(c=>c.GetType().Name)),"parcel|package|pickup|collectible|посыл",RegexOptions.IgnoreCase)&&r.bounds.size.magnitude<4).Select(r=>r.gameObject).Distinct().ToArray();
        Assert.AreEqual(5,parcels.Length,"Exactly five visible parcel objects must exist");
        var texts=UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        var counter=texts.SingleOrDefault(t=>(Regex.IsMatch(t.name+" "+t.text,"counter|счёт|счет|посыл|собр",RegexOptions.IgnoreCase)||Regex.IsMatch(t.text??"",@"^\s*0\s*(?:/|из)\s*5\s*$"))&&Regex.Match(t.text??"",@"\d+").Value=="0");Assert.IsTrue(counter,"No visible initial collection counter bound at runtime");Assert.IsTrue(counter.isActiveAndEnabled);
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
            // A batch Editor need not have a Game View render target. Capture
            // the real gameplay camera; this excludes ScreenSpaceOverlay UI.
            var target=RenderTexture.GetTemporary(1024,768,24,RenderTextureFormat.ARGB32);
            var previousTarget=view.targetTexture;var previousActive=RenderTexture.active;Texture2D image=null;
            try
            {
                view.targetTexture=target;view.Render();RenderTexture.active=target;
                image=new Texture2D(1024,768,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1024,768),0,0);image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(capture));File.WriteAllBytes(capture,image.EncodeToPNG());
                Debug.Log("WAREHOUSE_CAPTURE actual gameplay camera world render; ScreenSpaceOverlay UI excluded: "+capture);
            }
            finally{view.targetTexture=previousTarget;RenderTexture.active=previousActive;if(image)UnityEngine.Object.Destroy(image);RenderTexture.ReleaseTemporary(target);}
        }
    }
}
