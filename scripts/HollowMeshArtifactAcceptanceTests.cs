using System;using System.Collections;using System.IO;using System.Linq;using NUnit.Framework;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.TestTools;

// Independent read-back of an unseen geometry request. No mesh/collider repair.
public sealed class HollowMeshArtifactAcceptanceTests
{
 [UnitySetUp] public IEnumerator Open(){Assert.AreEqual("1",Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE"));Assert.AreEqual(Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName),Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""));EditorSceneManager.OpenScene(Environment.GetEnvironmentVariable("WW_AGENT_SCENE"));yield return new EnterPlayMode();yield return null;}
 [UnityTearDown] public IEnumerator Close(){yield return new ExitPlayMode();}
 static bool Finite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.x)&&!float.IsInfinity(v.y)&&!float.IsInfinity(v.z);
 static float Angle(Vector3 v,Vector3 center)=>(Mathf.Round(Mathf.Repeat(Mathf.Atan2(v.z-center.z,v.x-center.x)*Mathf.Rad2Deg,360)*10)/10)%360;
 static float Radius(Vector3 v,Vector3 center)=>Vector2.Distance(new Vector2(v.x,v.z),new Vector2(center.x,center.z));
 static bool IntersectsTriangle(Ray ray,Vector3 a,Vector3 b,Vector3 c,float maxDistance)
 {
  Vector3 e1=b-a,e2=c-a,p=Vector3.Cross(ray.direction,e2);float determinant=Vector3.Dot(e1,p);if(Mathf.Abs(determinant)<.000001f)return false;float inv=1/determinant;Vector3 t=ray.origin-a;float u=Vector3.Dot(t,p)*inv;if(u<0||u>1)return false;Vector3 q=Vector3.Cross(t,e1);float v=Vector3.Dot(ray.direction,q)*inv;if(v<0||u+v>1)return false;float distance=Vector3.Dot(e2,q)*inv;return distance>=0&&distance<=maxDistance;
 }
 [UnityTest] public IEnumerator ActualPersistentOctagonalMeshAndPhysicalThroughHole()
 {
  var candidates=UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(f=>f.sharedMesh&&f.GetComponent<Renderer>()&&f.GetComponent<Renderer>().enabled&&Mathf.Abs(f.GetComponent<Renderer>().bounds.size.y-1.6f)<.05f).ToArray();Assert.AreEqual(1,candidates.Length,"One visible requested mesh must be identifiable by actual height");var filter=candidates[0];var renderer=filter.GetComponent<Renderer>();var mesh=filter.sharedMesh;string path=AssetDatabase.GetAssetPath(mesh);Assert.That(path,Does.StartWith("Assets/"),"Mesh must be a persistent project asset, not a built-in primitive");
  var vertices=mesh.vertices.Select(filter.transform.TransformPoint).ToArray();Assert.IsTrue(vertices.Length>=16&&vertices.All(Finite));var triangles=mesh.triangles;Assert.IsTrue(triangles.Length>0&&triangles.Length%3==0&&triangles.All(i=>i>=0&&i<vertices.Length));
  Assert.AreEqual(0,vertices.Min(v=>v.y),.025f);Assert.AreEqual(1.6f,vertices.Max(v=>v.y),.025f);Vector3 center=renderer.bounds.center;Assert.AreEqual(1.75f,vertices.Max(v=>Radius(v,center)),.025f);
  foreach(float radius in new[]{1.25f,1.75f}){var angles=vertices.Where(v=>Mathf.Abs(Radius(v,center)-radius)<.02f).Select(v=>Angle(v,center)).Distinct().OrderBy(v=>v).ToArray();Assert.AreEqual(8,angles.Length,"Expected eight actual inner/outer corner directions at radius "+radius);for(int i=0;i<8;i++)Assert.AreEqual(45,Mathf.Repeat(angles[(i+1)%8]-angles[i],360),.3f);}
  foreach(float radius in new[]{1.25f,1.75f})
  {
   float initialAngle=vertices.Where(v=>Mathf.Abs(Radius(v,center)-radius)<.02f).Select(v=>Angle(v,center)).Min();var faces=new System.Collections.Generic.HashSet<int>();
   for(int t=0;t<triangles.Length;t+=3){var points=new[]{vertices[triangles[t]],vertices[triangles[t+1]],vertices[triangles[t+2]]};if(points.Max(v=>v.y)-points.Min(v=>v.y)<.1f||points.Any(v=>Radius(v,center)<radius*Mathf.Cos(Mathf.PI/8)-.03f||Radius(v,center)>radius+.03f))continue;Vector3 mid=(points[0]+points[1]+points[2])/3;faces.Add(Mathf.FloorToInt(Mathf.Repeat(Angle(mid,center)-initialAngle,360)/45));}Assert.AreEqual(8,faces.Count,"Actual triangles do not cover all eight vertical wall directions at radius "+radius);
  }
  Assert.IsTrue(Enumerable.Range(0,triangles.Length/3).Any(t=>new[]{vertices[triangles[t*3]],vertices[triangles[t*3+1]],vertices[triangles[t*3+2]]}.All(v=>Mathf.Abs(v.y-1.6f)<.025f)),"No actual annular top triangles");
  var geometryRay=new Ray(new Vector3(center.x,2.1f,center.z),Vector3.down);Assert.IsFalse(Enumerable.Range(0,triangles.Length/3).Any(t=>IntersectsTriangle(geometryRay,vertices[triangles[t*3]],vertices[triangles[t*3+1]],vertices[triangles[t*3+2]],2.2f)),"Rendered mesh itself closes the through-hole, even if a different collider leaves it open");
  Physics.SyncTransforms();var bounds=renderer.bounds;var colliders=UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c=>c.enabled&&!c.isTrigger&&c.bounds.max.y>=bounds.max.y-.03f&&c.bounds.min.y<=bounds.min.y+.03f&&c.bounds.Intersects(bounds)).ToArray();Assert.IsNotEmpty(colliders,"No physical wall colliders span the requested object");
  Func<Vector2,bool> blocked=point=>{var ray=new Ray(new Vector3(center.x+point.x,bounds.max.y+.5f,center.z+point.y),Vector3.down);return colliders.Any(c=>c.Raycast(ray,out var hit,bounds.size.y+1));};
  Assert.IsFalse(blocked(Vector2.zero),"Actual collider blocks the through-hole center");foreach(var offset in new[]{Vector2.left,Vector2.right,Vector2.up,Vector2.down})Assert.IsFalse(blocked(offset*.5f),"Collider intrudes into the central aperture");
  for(int i=0;i<8;i++){float angle=i*Mathf.PI/4;Assert.IsTrue(blocked(new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*1.5f),"Actual physics ray misses the annular wall/ring at direction "+i);}
  var cameras=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c=>c.enabled&&GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(c),bounds)).ToArray();Assert.IsNotEmpty(cameras,"No enabled gameplay camera sees the object");Assert.IsTrue(UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Any(l=>l.enabled));
  string capture=Environment.GetEnvironmentVariable("WW_MESH_CAPTURE");if(!string.IsNullOrEmpty(capture)){var camera=cameras[0];var target=camera.targetTexture;var active=RenderTexture.active;var rt=new RenderTexture(1280,720,24);var image=new Texture2D(1280,720,TextureFormat.RGB24,false);try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(capture,image.EncodeToPNG());}finally{camera.targetTexture=target;RenderTexture.active=active;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}}
  Debug.Log($"HOLLOW_MESH_INPUT actual asset={path}; vertices={vertices.Length}; triangles={triangles.Length/3}; height=1.6; corner radii=1.25/1.75; eight regular directions; five clear aperture rays; eight solid ring rays; no repair by observer");yield return null;
 }
}
