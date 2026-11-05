using System;using System.Collections;using System.IO;using System.Linq;using NUnit.Framework;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.TestTools;

// Independent read-back of an unseen geometry request. No mesh/collider repair.
public sealed class HollowMeshArtifactAcceptanceTests
{
 bool previousBackground;bool backgroundSaved;
 [UnitySetUp] public IEnumerator Open(){Assert.AreEqual("1",Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE"));Assert.AreEqual(Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName),Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""));EditorSceneManager.OpenScene(Environment.GetEnvironmentVariable("WW_AGENT_SCENE"));yield return new EnterPlayMode();previousBackground=Application.runInBackground;backgroundSaved=true;Application.runInBackground=true;Assert.IsTrue(Application.isPlaying&&EditorApplication.isPlaying);yield return null;}
 [UnityTearDown] public IEnumerator Close(){if(backgroundSaved){Application.runInBackground=previousBackground;backgroundSaved=false;}yield return new ExitPlayMode();}
 static bool Finite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.x)&&!float.IsInfinity(v.y)&&!float.IsInfinity(v.z);
 static float Angle(Vector3 v,Vector3 center)=>(Mathf.Round(Mathf.Repeat(Mathf.Atan2(v.z-center.z,v.x-center.x)*Mathf.Rad2Deg,360)*10)/10)%360;
 static float Radius(Vector3 v,Vector3 center)=>Vector2.Distance(new Vector2(v.x,v.z),new Vector2(center.x,center.z));
 static bool IntersectsTriangle(Ray ray,Vector3 a,Vector3 b,Vector3 c,float maxDistance)=>TriangleDistance(ray,a,b,c,maxDistance,out _);
 static bool TriangleDistance(Ray ray,Vector3 a,Vector3 b,Vector3 c,float maxDistance,out float distance)
 {
  distance=0;
  Vector3 e1=b-a,e2=c-a,p=Vector3.Cross(ray.direction,e2);float determinant=Vector3.Dot(e1,p);if(Mathf.Abs(determinant)<.000001f)return false;float inv=1/determinant;Vector3 t=ray.origin-a;float u=Vector3.Dot(t,p)*inv;if(u<0||u>1)return false;Vector3 q=Vector3.Cross(t,e1);float v=Vector3.Dot(ray.direction,q)*inv;if(v<0||u+v>1)return false;distance=Vector3.Dot(e2,q)*inv;return distance>=0&&distance<=maxDistance;
 }
 static bool HasPipelineType(RenderPipelineAsset asset,string fullName)
 {
  if(!asset)return false;for(var type=asset.GetType();type!=null;type=type.BaseType)if(type.FullName==fullName)return true;return false;
 }
 static string PipelineName(RenderPipelineAsset asset)=>asset?asset.name+" ("+asset.GetType().FullName+")":"Built-in";
 static void RequireUsableMaterials(Renderer renderer)
 {
  // Independent implementation: no product diagnostic helper or repair calls.
  var quality=QualitySettings.renderPipeline;var project=GraphicsSettings.defaultRenderPipeline;var effective=quality?quality:project;
  bool urp=HasPipelineType(effective,"UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset");
  bool hdrp=HasPipelineType(effective,"UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset");
  var materials=renderer.sharedMaterials;Assert.IsNotEmpty(materials,"Requested visible mesh has no assigned material");
  Debug.Log($"HOLLOW_VISUAL_PIPELINE quality={PipelineName(quality)} default={PipelineName(project)} resolved={PipelineName(effective)} current={PipelineName(GraphicsSettings.currentRenderPipeline)}");
  for(int i=0;i<materials.Length;i++)
  {
   var material=materials[i];Assert.IsTrue(material!=null,"Requested mesh has an unassigned material slot "+i);var shader=material.shader;
   Assert.IsTrue(shader!=null,"Requested material has no shader at slot "+i);string name=shader.name;
   Debug.Log($"HOLLOW_VISUAL_MATERIAL slot={i} material={material.name} shader={name} supported={shader.isSupported}");
   Assert.IsTrue(shader.isSupported,"Requested material shader is unsupported on this actual graphics device: "+name);
   Assert.AreNotEqual("Hidden/InternalErrorShader",name,"Requested material resolved to Unity's internal error shader");
   bool standard=name=="Standard"||name=="Standard (Specular setup)";
   Assert.IsFalse((urp||hdrp)&&standard,"Built-in Standard shader is incompatible with the resolved official SRP: "+PipelineName(effective));
   bool officialUrp=name.StartsWith("Universal Render Pipeline/",StringComparison.Ordinal);
   bool officialHdrp=name.StartsWith("HDRP/",StringComparison.Ordinal)||name.StartsWith("HDRenderPipeline/",StringComparison.Ordinal);
   Assert.IsFalse(!effective&&(officialUrp||officialHdrp),"Official SRP-only shader is assigned while the resolved renderer is built-in: "+name);
   // Unknown custom shader/pipeline combinations are deliberately unclassified.
   // A legitimate pink material is never rejected by pixel colour.
  }
 }
 static void RequireReferencedLitNormals(Mesh mesh,Renderer renderer)
 {
  // Read the normals actually stored on this referenced mesh. Geometric face
  // normals used by ray/visibility checks do not supply lighting vertex normals.
  // Do not normalize, recalculate or otherwise repair the model's output here.
  var materials=renderer.sharedMaterials;var referenced=new System.Collections.Generic.HashSet<int>();int knownLitSlots=0;
  for(int slot=0;slot<materials.Length;slot++)
  {
   var material=materials[slot];string shader=material&&material.shader?material.shader.name:"";
   bool knownLit=shader=="Standard"||shader=="Standard (Specular setup)"||shader=="Universal Render Pipeline/Lit"||shader=="Universal Render Pipeline/Simple Lit"||shader=="HDRP/Lit";
   if(!knownLit)continue;knownLitSlots++;
   // Extra renderer materials redraw the last submesh. Missing or unknown
   // material slots do not imply a normal requirement for other submeshes.
   if(mesh.subMeshCount==0)continue;int submesh=Mathf.Min(slot,mesh.subMeshCount-1);
   foreach(int index in mesh.GetTriangles(submesh))referenced.Add(index);
  }
  var normals=mesh.normals;int missing=0,nonFinite=0,zero=0;
  foreach(int index in referenced)
  {
   if(index<0||index>=normals.Length){missing++;continue;}
   var normal=normals[index];if(!Finite(normal)){nonFinite++;continue;}
   if(normal.x==0&&normal.y==0&&normal.z==0)zero++;
  }
  Debug.Log($"HOLLOW_STORED_NORMALS knownLitMaterialSlots={knownLitSlots} referencedLitVertices={referenced.Count} storedNormalCount={normals.Length} missingReferencedNormals={missing} nonFiniteReferencedNormals={nonFinite} zeroReferencedNormals={zero}; actual stored mesh normals, no repair; custom/unlit slots are unclassified");
  if(knownLitSlots==0)return;
  Assert.Greater(referenced.Count,0,"Known lit material has no referenced mesh vertices to verify");
  Assert.AreEqual(0,missing,"Known lit material references mesh vertices without stored lighting normals");
  Assert.AreEqual(0,nonFinite,"Known lit material references non-finite stored vertex normals");
  Assert.AreEqual(0,zero,"Known lit material references zero stored vertex normals; geometric face checks do not prove correct lighting");
 }
 static void RequireCameraSurfaceEvidence(Camera camera,Renderer renderer,Vector3[] vertices,int[] triangles,Vector3 center)
 {
  int innerVisible=0,topVisible=0,innerFrontVisible=0,topFrontVisible=0;
  float largestInnerProjectedArea=0,largestTopProjectedArea=0;
  // Bounded interior points include near-vertex and near-edge regions so a
  // partially visible strip is not judged solely by its occluded centroid.
  var weights=new[]{new Vector3(1f/3,1f/3,1f/3),new Vector3(.98f,.01f,.01f),new Vector3(.01f,.98f,.01f),new Vector3(.01f,.01f,.98f),new Vector3(.49f,.49f,.02f),new Vector3(.49f,.02f,.49f),new Vector3(.02f,.49f,.49f)};
  for(int i=0;i<triangles.Length;i+=3)
  {
   var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
   bool top=Mathf.Abs(a.y-1.6f)<.025f&&Mathf.Abs(b.y-1.6f)<.025f&&Mathf.Abs(c.y-1.6f)<.025f;
   bool inner=Mathf.Max(a.y,Mathf.Max(b.y,c.y))-Mathf.Min(a.y,Mathf.Min(b.y,c.y))>.1f&&
    Mathf.Abs(Radius(a,center)-1.25f)<.025f&&Mathf.Abs(Radius(b,center)-1.25f)<.025f&&Mathf.Abs(Radius(c,center)-1.25f)<.025f;
   if(!top&&!inner)continue;
   var va=camera.WorldToViewportPoint(a);var vb=camera.WorldToViewportPoint(b);var vc=camera.WorldToViewportPoint(c);
   float projectedArea=Mathf.Abs((vb.x-va.x)*(vc.y-va.y)-(vb.y-va.y)*(vc.x-va.x))*.5f;
   if(projectedArea<=0)continue; // Exactly edge-on is not a visible surface; no minimum area/aesthetic threshold.
   foreach(var weight in weights)
   {
    var point=a*weight.x+b*weight.y+c*weight.z;var viewport=camera.WorldToViewportPoint(point);
    if(viewport.z<camera.nearClipPlane||viewport.z>camera.farClipPlane||viewport.x<=0||viewport.x>=1||viewport.y<=0||viewport.y>=1)continue;
    var ray=camera.ViewportPointToRay(new Vector3(viewport.x,viewport.y,0));float expected=Vector3.Dot(point-ray.origin,ray.direction),nearest=float.PositiveInfinity;
    for(int j=0;j<triangles.Length;j+=3)if(TriangleDistance(ray,vertices[triangles[j]],vertices[triangles[j+1]],vertices[triangles[j+2]],expected+.03f,out float hit))nearest=Mathf.Min(nearest,hit);
    if(float.IsInfinity(nearest)||Mathf.Abs(nearest-expected)>.025f)continue;
    bool front=Vector3.Dot(Vector3.Cross(b-a,c-a).normalized,-ray.direction)>0;
    if(top){topVisible++;if(front)topFrontVisible++;largestTopProjectedArea=Mathf.Max(largestTopProjectedArea,projectedArea);}
    if(inner){innerVisible++;if(front)innerFrontVisible++;largestInnerProjectedArea=Mathf.Max(largestInnerProjectedArea,projectedArea);}
   }
  }
  Debug.Log($"HOLLOW_VISUAL_CAMERA camera={camera.name} samplesPerTriangle={weights.Length} innerUnoccludedPointSamples={innerVisible} topUnoccludedPointSamples={topVisible} innerFrontSamples={innerFrontVisible} topFrontSamples={topFrontVisible} largestInnerViewportArea={largestInnerProjectedArea:F6} largestTopViewportArea={largestTopProjectedArea:F6}; bounded geometric mesh line-of-sight evidence, not an artistic-quality score");
  Assert.Greater(topVisible,0,"The gameplay camera has no non-edge-on, unoccluded sample of the requested top ring");
  Assert.Greater(innerVisible,0,"The gameplay camera has no non-edge-on, unoccluded sample of the requested inner walls");
  var materials=renderer.sharedMaterials;
  bool knownBackCull=materials.Length>0&&materials.All(m=>m&&m.shader&&
   (m.shader.name=="Standard"||m.shader.name=="Standard (Specular setup)"||m.shader.name.StartsWith("Universal Render Pipeline/",StringComparison.Ordinal)||m.shader.name.StartsWith("HDRP/",StringComparison.Ordinal))&&
   (!m.HasProperty("_Cull")||Mathf.RoundToInt(m.GetFloat("_Cull"))==2)&&(!m.HasProperty("_CullMode")||Mathf.RoundToInt(m.GetFloat("_CullMode"))==2)&&(!m.HasProperty("_DoubleSidedEnable")||m.GetFloat("_DoubleSidedEnable")<.5f));
  if(knownBackCull){Assert.Greater(topFrontVisible,0,"Known back-culling material has no front-facing visible top-ring sample");Assert.Greater(innerFrontVisible,0,"Known back-culling material has no front-facing visible inner-wall sample");}
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
  for(int i=0;i<8;i++)
  {
   float angle=i*Mathf.PI/4;var direction=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));var point=new Vector3(center.x,bounds.max.y+.5f,center.z)+direction*1.5f;var ray=new Ray(point,Vector3.down);
   var hits=colliders.Select(c=>{bool hit=c.Raycast(ray,out var value,bounds.size.y+1);return new{hit,value};}).Where(h=>h.hit).OrderBy(h=>h.value.distance).ToArray();
   Assert.IsNotEmpty(hits,"Actual physics ray misses the annular wall/ring at direction "+i);
   Assert.AreEqual(bounds.max.y,hits[0].value.point.y,.03f,"Ray skipped the required top and hit a farther surface at direction "+i);
   ray=new Ray(new Vector3(center.x,center.y,center.z)+direction*2.5f,-direction);
   float expected=float.PositiveInfinity;
   for(int t=0;t<triangles.Length;t+=3)if(TriangleDistance(ray,vertices[triangles[t]],vertices[triangles[t+1]],vertices[triangles[t+2]],5,out float distance))expected=Mathf.Min(expected,distance);
   Assert.IsFalse(float.IsInfinity(expected),"No rendered outer geometry intersects the independent reference ray");
   var outerHits=colliders.Select(c=>{bool hit=c.Raycast(ray,out var value,5);return new{hit,value};}).Where(h=>h.hit).OrderBy(h=>h.value.distance).ToArray();Assert.IsNotEmpty(outerHits,"No actual outer wall hit");Assert.AreEqual(expected,outerHits[0].value.distance,.03f,"Ray skipped the near exterior and hit a farther facing surface at direction "+i);
  }
  // Temporary input/physics fixtures test the existing surfaces; no mesh,
  // collider or game source is repaired. Unrelated scenery is ignored only by
  // the fixture bodies so a floor cannot masquerade as the requested ring.
  var fixtures=new System.Collections.Generic.List<GameObject>();var bodies=new System.Collections.Generic.List<Rigidbody>();var unrelated=UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Except(colliders).ToArray();
  try
  {
   foreach(var offset in new[]{Vector3.zero,Vector3.left*1.5f,Vector3.right*1.5f,Vector3.forward*1.5f,Vector3.back*1.5f})
   {
    var bodyObject=new GameObject("Independent physics fixture");fixtures.Add(bodyObject);bodyObject.transform.position=new Vector3(center.x,bounds.max.y+.5f,center.z)+offset;var sphere=bodyObject.AddComponent<SphereCollider>();sphere.radius=.07f;var body=bodyObject.AddComponent<Rigidbody>();body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;bodies.Add(body);foreach(var other in unrelated)if(other)Physics.IgnoreCollision(sphere,other);
   }
   Assert.Less(Physics.gravity.y,-.1f,"Downward gravity is needed for the independent physical support observation");double started=Time.timeAsDouble,deadline=EditorApplication.timeSinceStartup+15;while(Time.timeAsDouble-started<1.5&&EditorApplication.timeSinceStartup<deadline){EditorApplication.QueuePlayerLoopUpdate();yield return null;}Assert.GreaterOrEqual(Time.timeAsDouble-started,1.5,"Independent PlayerLoop did not advance for the physical observation");
   Assert.Less(bodies[0].position.y,bounds.min.y-.1f,"The actual center fixture did not fall through the vertical aperture");for(int i=1;i<bodies.Count;i++)Assert.AreEqual(bounds.max.y+.07f,bodies[i].position.y,.06f,"Actual dynamic body is not supported by the near top surface at fixture "+i);
  }
  finally{foreach(var fixture in fixtures)if(fixture)UnityEngine.Object.DestroyImmediate(fixture);}
  var cameras=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c=>c.enabled&&GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(c),bounds)).ToArray();Assert.IsNotEmpty(cameras,"No enabled gameplay camera sees the object");Assert.IsTrue(UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Any(l=>l.enabled));
  string capture=Environment.GetEnvironmentVariable("WW_MESH_CAPTURE");if(!string.IsNullOrEmpty(capture)){var camera=cameras[0];var target=camera.targetTexture;var active=RenderTexture.active;var rt=new RenderTexture(1280,720,24);var image=new Texture2D(1280,720,TextureFormat.RGB24,false);try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(capture,image.EncodeToPNG());}finally{camera.targetTexture=target;RenderTexture.active=active;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}}
  // Capture above is retained even when these independent visual checks fail.
  RequireUsableMaterials(renderer);RequireReferencedLitNormals(mesh,renderer);RequireCameraSurfaceEvidence(cameras[0],renderer,vertices,triangles,center);
  Debug.Log($"HOLLOW_MESH_INPUT actual asset={path}; vertices={vertices.Length}; triangles={triangles.Length/3}; height=1.6; corner radii=1.25/1.75; eight regular directions; five clear aperture rays; eight solid ring rays; no repair by observer");yield return null;
 }
}
