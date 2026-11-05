using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    // Read-back only: never changes mesh winding, collider settings, global query
    // flags, scene objects or assets. A diagnostic is evidence, not a repair.
    internal static class GeometryDiagnostics
    {
        const int MaximumVertices = 65536;
        const int MaximumTriangles = 65536;
        const int TriangleSampleLimit = 4096;
        const int CompactTriangleLimit = 512;
        struct OrientedEdge { internal int Count, Direction; }
        struct Face { internal Vector3 A, B, C; }
        struct GeometricRay { internal Ray Ray; internal float Length, Near, FrontNear; internal string FromDirection; }
        static JArray Vec(Vector3 value) => new JArray(value.x, value.y, value.z);
        static bool Finite(Vector3 v) => !(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z));
        static Mesh VisualMesh(GameObject go)
        {
            if(!go)return null;
            var filter=go.GetComponent<MeshFilter>();
            if(filter&&filter.sharedMesh)return filter.sharedMesh;
            var skin=go.GetComponent<SkinnedMeshRenderer>();
            return skin?skin.sharedMesh:null;
        }
        static JObject MeshIdentity(Mesh mesh) => mesh ? new JObject { ["name"]=mesh.name,["asset"]=AssetDatabase.GetAssetPath(mesh),["vertices"]=mesh.vertexCount,["readable"]=mesh.isReadable,["subMeshes"]=mesh.subMeshCount } : null;

        internal static JObject Summary(GameObject go)
        {
            if(!go)return null;
            var mesh=VisualMesh(go);var colliders=go.GetComponents<MeshCollider>();
            if(!mesh&&colliders.Length==0)return null;
            var measuredMesh=mesh?mesh:colliders.Length>0?colliders[0].sharedMesh:null;
            return new JObject { ["visualMesh"]=MeshIdentity(mesh),["meshColliderCount"]=colliders.Length,["worldScale"]=Vec(go.transform.lossyScale),
                ["storedVertexNormals"]=StoredVertexNormals(measuredMesh),["orientation"]=CompactOrientation(measuredMesh,go.transform),["orientationMeshSource"]=mesh?"visual":"collider",["physicalCoverageProvenByOrientation"]=false,
                ["detailTool"]="inspect_component: MeshFilter, MeshCollider or SkinnedMeshRenderer; geometryDiagnostics contains measured topology, face orientation and physical ray coverage. No automatic repair." };
        }

        static JObject SummarizeStoredNormals(int vertexCount,Vector3[] normals)
        {
            const double nearZeroLength=1e-6,unitTolerance=.01;
            int nonFinite=0,nearZero=0,nonUnit=0;double min=double.PositiveInfinity,max=double.NegativeInfinity;
            foreach(var normal in normals)
            {
                if(!Finite(normal)){nonFinite++;continue;}
                // Double intermediates avoid overflowing for finite Float32 data.
                double length=Math.Sqrt((double)normal.x*normal.x+(double)normal.y*normal.y+(double)normal.z*normal.z);
                min=Math.Min(min,length);max=Math.Max(max,length);if(length<=nearZeroLength)nearZero++;if(Math.Abs(length-1)>unitTolerance)nonUnit++;
            }
            var result=new JObject{["available"]=true,["readOnly"]=true,["coordinateSpace"]="stored mesh-local vertex normals, not geometric face normals",["vertexCount"]=vertexCount,["count"]=normals.Length,["matchesVertexCount"]=normals.Length==vertexCount,["nonFiniteCount"]=nonFinite,["nearZeroCount"]=nearZero,["nonUnitFiniteCount"]=nonUnit,["nearZeroLengthThreshold"]=nearZeroLength,["unitLengthTolerance"]=unitTolerance,["minFiniteLength"]=double.IsPositiveInfinity(min)?JValue.CreateNull():(JToken)min,["maxFiniteLength"]=double.IsNegativeInfinity(max)?JValue.CreateNull():(JToken)max,["finiteLengthIncludesZero"]=true,["renderingDefectProven"]=false};
            if(normals.Length!=vertexCount||nonFinite>0||nearZero>0||nonUnit>0)
                result["warning"]="Stored vertex normals are missing, non-finite, near zero or non-unit; see actual counts. Ordinary lit shaders can shade differently from the geometric face orientation. This may be intentional for unlit/custom shaders; compare the intended material and actual rendered result. No normals were recalculated or repaired.";
            return result;
        }
        static JObject StoredVertexNormals(Mesh mesh)
        {
            var unavailable=new JObject{["available"]=false,["readOnly"]=true,["vertexLimit"]=MaximumVertices};
            if(!mesh){unavailable["reason"]="No mesh assigned.";return unavailable;}
            unavailable["vertexCount"]=mesh.vertexCount;
            if(!mesh.isReadable){unavailable["reason"]="CPU mesh data is not readable; stored normals were not accessed.";return unavailable;}
            if(mesh.vertexCount>MaximumVertices){unavailable["reason"]="Mesh exceeds the bounded stored-normal inspection size; no normal array was allocated.";return unavailable;}
            var result=SummarizeStoredNormals(mesh.vertexCount,mesh.normals);result["vertexLimit"]=MaximumVertices;return result;
        }
        static bool SmallReadableTriangles(Mesh mesh,out ulong indexCount)
        {
            indexCount=0;if(!mesh||!mesh.isReadable||mesh.vertexCount>4096||mesh.subMeshCount>64)return false;
            for(int i=0;i<mesh.subMeshCount;i++){if(mesh.GetTopology(i)!=MeshTopology.Triangles)return false;indexCount+=mesh.GetIndexCount(i);if(indexCount>CompactTriangleLimit*3)return false;}
            return true;
        }
        static int Root(int[] parents,int vertex)
        {while(parents[vertex]!=vertex){parents[vertex]=parents[parents[vertex]];vertex=parents[vertex];}return vertex;}
        static double Volume(Vector3 a,Vector3 b,Vector3 c,Vector3 origin)
        {
            double ax=(double)a.x-origin.x,ay=(double)a.y-origin.y,az=(double)a.z-origin.z,bx=(double)b.x-origin.x,by=(double)b.y-origin.y,bz=(double)b.z-origin.z,cx=(double)c.x-origin.x,cy=(double)c.y-origin.y,cz=(double)c.z-origin.z;
            return (ax*(by*cz-bz*cy)+ay*(bz*cx-bx*cz)+az*(bx*cy-by*cx))/6d;
        }
        static void DirectedEdge(Dictionary<ulong,OrientedEdge> edges,int a,int b)
        {
            ulong key=((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);edges.TryGetValue(key,out var edge);edge.Count++;edge.Direction+=a<b?1:-1;edges[key]=edge;
        }
        static JObject CompactOrientation(Mesh mesh,Transform transform)
        {
            var result=new JObject{["available"]=false,["triangleLimit"]=CompactTriangleLimit,["classification"]="ambiguous"};
            if(!SmallReadableTriangles(mesh,out ulong count)){result["reason"]="Needs a CPU-readable triangle mesh within 4096 vertices/512 triangles; no full mesh data was allocated.";return result;}
            var vertices=mesh.vertices;var indices=mesh.triangles;var edges=new Dictionary<ulong,OrientedEdge>();var parents=Enumerable.Range(0,vertices.Length).ToArray();var used=new HashSet<int>();int invalid=0,degenerate=0;double signed=0;
            var origin=indices.Length>0&&indices[0]>=0&&indices[0]<vertices.Length?vertices[indices[0]]:Vector3.zero;
            for(int t=0;t<indices.Length/3;t++)
            {
                int a=indices[t*3],b=indices[t*3+1],c=indices[t*3+2];
                if(a<0||b<0||c<0||a>=vertices.Length||b>=vertices.Length||c>=vertices.Length||!Finite(vertices[a])||!Finite(vertices[b])||!Finite(vertices[c])){invalid++;continue;}
                var cross=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);if(!Finite(cross)||cross.sqrMagnitude<1e-20f){degenerate++;continue;}
                signed+=Volume(vertices[a],vertices[b],vertices[c],origin);DirectedEdge(edges,a,b);DirectedEdge(edges,b,c);DirectedEdge(edges,c,a);
                used.Add(a);used.Add(b);used.Add(c);parents[Root(parents,b)]=Root(parents,a);parents[Root(parents,c)]=Root(parents,a);
            }
            int boundary=edges.Count(e=>e.Value.Count==1),nonManifold=edges.Count(e=>e.Value.Count>2),inconsistent=edges.Count(e=>e.Value.Count==2&&e.Value.Direction!=0),components=used.Select(v=>Root(parents,v)).Distinct().Count();
            bool closed=indices.Length>0&&invalid==0&&degenerate==0&&boundary==0&&nonManifold==0,coherent=closed&&inconsistent==0;
            float determinant=transform.localToWorldMatrix.determinant;bool finiteDet=!float.IsNaN(determinant)&&!float.IsInfinity(determinant);double world=signed*determinant;
            result["available"]=true;result["triangles"]=(long)count/3;result["boundaryEdges"]=boundary;result["nonManifoldEdges"]=nonManifold;result["inconsistentEdges"]=inconsistent;result["degenerateTriangles"]=degenerate;result["invalidTriangles"]=invalid;result["connectedComponents"]=components;result["closedByIndices"]=closed;result["coherentlyWound"]=coherent;result["indexTopologyOnly"]=true;result["signedVolumeLocal"]=!double.IsNaN(signed)&&!double.IsInfinity(signed)?(JToken)signed:JValue.CreateNull();result["transformDeterminant"]=finiteDet?(JToken)determinant:JValue.CreateNull();result["mirroredTransform"]=finiteDet&&determinant<0;
            bool classify=coherent&&components==1&&!double.IsNaN(signed)&&!double.IsInfinity(signed)&&Math.Abs(signed)>1e-12;
            result["classification"]=classify?"closed_coherent_shell":"ambiguous";result["localGeometricFacing"]=classify?(signed>0?"outward":"inward"):"ambiguous";result["worldGeometricFacing"]=classify&&finiteDet&&Math.Abs(world)>1e-12?(world>0?"outward":"inward"):"ambiguous";
            if(!classify)result["reason"]="Volume sign does not establish facing for open, degenerate, incoherent, disconnected or near-zero-volume index topology. Split seam vertices can appear open by indices.";
            else result["facingIsIntentDependent"]=true;
            if(classify&&(signed<0||determinant<0))result["warning"]="Measured winding points inward locally or is mirrored in world space. This can be intentional for interiors; compare with the requested solid/visible surface. One collider ray may hit a farther front face after skipping a nearer backface, so a hit alone does not prove coverage.";
            return result;
        }
        internal static JArray ReviewSceneEvidence(IEnumerable<GameObject> objects)
        {
            var candidates=new List<(GameObject Go,Component Component,JObject Summary)>();
            foreach(var go in (objects??Enumerable.Empty<GameObject>()).Where(g=>g&&g.activeInHierarchy).Take(256))
            {
                var filter=go.GetComponent<MeshFilter>();var collider=go.GetComponent<MeshCollider>();Component component=filter&&filter.sharedMesh?(Component)filter:collider&&collider.sharedMesh?collider:null;
                if(!component)continue;var mesh=filter&&filter.sharedMesh?filter.sharedMesh:collider.sharedMesh;
                string path=AssetDatabase.GetAssetPath(mesh);if(path.Length>0&&!path.StartsWith("Assets/",StringComparison.Ordinal)&&!path.StartsWith("Packages/",StringComparison.Ordinal))continue;
                if(!SmallReadableTriangles(mesh,out _))continue;candidates.Add((go,component,Summary(go)));
            }
            var result=new JArray();int budget=0;
            foreach(var candidate in candidates.OrderByDescending(c=>c.Summary["orientation"]?["warning"]!=null||c.Summary["storedVertexNormals"]?["warning"]!=null).ThenByDescending(c=>(bool)c.Go.GetComponent<MeshCollider>()).Take(4))
            {
                var detail=Inspect(candidate.Component);detail.Remove("triangleSamples");detail.Remove("coverageMeaning");
                if(detail["meshColliders"] is JArray colliders)
                {
                    detail["meshColliders"]=new JArray(colliders.Take(2).Select(c=>c.DeepClone()));
                    foreach(var c in (JArray)detail["meshColliders"])if(c["directionalCoverage"] is JObject coverage&&coverage["sampleDifferences"] is JArray differences)
                        coverage["sampleDifferences"]=new JArray(differences.OrderBy(d=>(bool?)d["colliderHit"]==true).Take(2).Select(d=>d.DeepClone()));
                }
                var item=new JObject{["target"]=ProjectContextIndex.StableTarget(candidate.Go),["name"]=candidate.Go.name,["summary"]=candidate.Summary,["measuredDiagnostics"]=detail,["physicalCoverageNote"]="A single far-side hit does not establish near-surface coverage. Compare nearest two-sided/front-facing geometric distances and actual hits with intended accessible surfaces; verify behavior in Play Mode."};
                int length=item.ToString(Newtonsoft.Json.Formatting.None).Length;if(budget+length+result.Count+2>18000)break;budget+=length;result.Add(item);
            }
            return result;
        }

        internal static JObject Inspect(Component component)
        {
            if(!component)return null;
            Mesh mesh=component is MeshFilter filter ? filter.sharedMesh : component is MeshCollider collider ? collider.sharedMesh : component is SkinnedMeshRenderer skin ? skin.sharedMesh : null;
            if(!(component is MeshFilter)&&!(component is MeshCollider)&&!(component is SkinnedMeshRenderer))return null;
            var go=component.gameObject;var warnings=new JArray();var result=new JObject { ["mesh"]=MeshIdentity(mesh),["visualMesh"]=MeshIdentity(VisualMesh(go)),["worldScale"]=Vec(go.transform.lossyScale),["transformDeterminant"]=go.transform.localToWorldMatrix.determinant,["readOnly"]=true,["warnings"]=warnings };
            if(!mesh){warnings.Add("Component has no mesh.");return result;}
            result["storedVertexNormals"]=StoredVertexNormals(mesh);
            if(result["storedVertexNormals"]?["warning"]?.Type==JTokenType.String)warnings.Add((string)result["storedVertexNormals"]["warning"]);
            result["queriesHitBackfaces"]=Physics.queriesHitBackfaces;
            result["queriesDoNotAdvanceSimulation"]=true;
            result["skinnedMeshPose"] = component is SkinnedMeshRenderer ? "Reference mesh only; current deformed skin pose is not baked by this read-only inspection." : null;
            if(!mesh.isReadable){result["analysisAvailable"]=false;warnings.Add("CPU mesh data is not readable. Counts/references are available; triangle geometry was not inspected.");return result;}
            ulong indexCount=0;
            for(int i=0;i<mesh.subMeshCount;i++)
            {
                if(mesh.GetTopology(i)!=MeshTopology.Triangles){result["analysisAvailable"]=false;warnings.Add("Non-triangle topology: triangle/collider coverage analysis was not performed.");return result;}
                indexCount+=mesh.GetIndexCount(i);
            }
            if(mesh.vertexCount>MaximumVertices||indexCount>(ulong)MaximumTriangles*3)
            {result["analysisAvailable"]=false;warnings.Add("Mesh exceeds bounded inspection size (65536 vertices/triangles). Inspect a smaller region with explicit Editor C#; no full-data allocation was made.");return result;}
            var local=mesh.vertices;var vertices=local.Select(go.transform.TransformPoint).ToArray();var indices=mesh.triangles;
            int total=indices.Length/3,stride=Math.Max(1,Mathf.CeilToInt(total/(float)TriangleSampleLimit));
            var faces=new List<Face>();var examples=new JArray();int degenerate=0,invalid=0;double signedVolume=0;
            var directions=new Dictionary<string,int>{{"+X",0},{"-X",0},{"+Y",0},{"-Y",0},{"+Z",0},{"-Z",0}};
            var edges=new Dictionary<ulong,int>();
            for(int t=0;t<total;t+=stride)
            {
                int ia=indices[t*3],ib=indices[t*3+1],ic=indices[t*3+2];
                if(ia<0||ib<0||ic<0||ia>=vertices.Length||ib>=vertices.Length||ic>=vertices.Length){invalid++;continue;}
                var a=vertices[ia];var b=vertices[ib];var c=vertices[ic];
                if(!Finite(a)||!Finite(b)||!Finite(c)){invalid++;continue;}
                var cross=Vector3.Cross(b-a,c-a);if(!Finite(cross)){invalid++;continue;}if(cross.sqrMagnitude<1e-16f){degenerate++;continue;}
                var normal=cross.normalized;var center=(a+b+c)/3;faces.Add(new Face{A=a,B=b,C=c});
                string direction=Mathf.Abs(normal.x)>=Mathf.Abs(normal.y)&&Mathf.Abs(normal.x)>=Mathf.Abs(normal.z)?(normal.x>=0?"+X":"-X"):Mathf.Abs(normal.y)>=Mathf.Abs(normal.z)?(normal.y>=0?"+Y":"-Y"):(normal.z>=0?"+Z":"-Z");directions[direction]++;
                if(examples.Count<12&&(directions[direction]<=2||examples.Count<3))examples.Add(new JObject{["triangle"]=t,["worldCenter"]=Vec(center),["worldGeometricNormal"]=Vec(normal),["dominantDirection"]=direction});
                if(stride==1)
                {
                    signedVolume+=Vector3.Dot(local[ia],Vector3.Cross(local[ib],local[ic]))/6d;
                    AddEdge(edges,ia,ib);AddEdge(edges,ib,ic);AddEdge(edges,ic,ia);
                }
            }
            result["analysisAvailable"]=true;result["triangleCount"]=total;result["sampledTriangles"]=faces.Count+degenerate+invalid;result["completeTriangleAnalysis"]=stride==1;result["degenerateTrianglesInSample"]=degenerate;result["invalidTrianglesInSample"]=invalid;result["nonFiniteVertices"]=vertices.Count(v=>!Finite(v));result["worldFaceNormalDirections"]=JObject.FromObject(directions);
            if(stride==1)
            {
                result["indexTopology"]=new JObject{["boundaryEdges"]=edges.Count(e=>e.Value==1),["nonManifoldEdges"]=edges.Count(e=>e.Value>2),["indicesOnlyNotPositionWelded"]=true,["signedVolumeLocal"]=signedVolume,["orientationNote"]="Volume sign alone does not prove intended facing. Open surfaces or split seam indices are not a closed-shell orientation test. Compare measured normals and front/back ray observations against the intended surface."};
            }
            if(total==0)warnings.Add("Mesh contains no triangles.");
            if(degenerate>0||invalid>0)warnings.Add("Sample includes degenerate or invalid triangles; see counts and the bounded analysis flag.");
            if(go.transform.localToWorldMatrix.determinant<0)warnings.Add("Transform mirrors the mesh. World-space triangle orientation differs from local winding; inspect measured normals and physical rays.");
            var physical=new JArray();
            // Synchronizes current transforms for queries; does not step physics,
            // change query flags or mark the scene/assets dirty.
            Physics.SyncTransforms();
            List<GeometricRay> geometryRays=null;
            foreach(var mc in go.GetComponents<MeshCollider>().Take(4))
            {
                var state=new JObject{["enabled"]=mc.enabled,["activeInHierarchy"]=go.activeInHierarchy,["isTrigger"]=mc.isTrigger,["convex"]=mc.convex,["mesh"]=MeshIdentity(mc.sharedMesh),["matchesInspectedMesh"]=mc.sharedMesh==mesh};
                if(mc.sharedMesh!=mesh)warnings.Add("MeshCollider references a different mesh than the inspected geometry; visual geometry is not proof of its collision shape.");
                if(mc.convex)warnings.Add("Convex collision uses a convex hull; concavities and holes in the source mesh are not preserved by that collider.");
                if(mc.enabled&&go.activeInHierarchy&&faces.Count>0&&component is not SkinnedMeshRenderer){if(geometryRays==null)geometryRays=BuildGeometryRays(faces,vertices.Where(Finite).ToArray());state["directionalCoverage"]=Coverage(mc,geometryRays,stride==1);}
                physical.Add(state);
            }
            result["meshColliders"]=physical;
            if(physical.Count==0)warnings.Add("No MeshCollider is attached to this object. Other collider types/child objects must be inspected separately; a MeshFilter does not provide collisions.");
            result["coverageMeaning"]="Axis-aligned sample rays compare actual Collider.Raycast hits with two-sided and front-facing geometric triangle intersections. A single far-side hit may skip nearer backfaces and does not prove near-surface coverage. This is bounded static read-back, not a complete collision or gameplay proof. Back-facing surfaces may be skipped when queriesHitBackfaces=false. Reverse triangle winding in implementation only if the measured facing is wrong; changing global backface-query flags is not a repair of generated geometry.";
            result["triangleSamples"]=examples;
            return result;
        }
        static void AddEdge(Dictionary<ulong,int> edges,int a,int b)
        {ulong key=((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);edges[key]=edges.TryGetValue(key,out int count)?count+1:1;}
        static bool TriangleHit(Ray ray,Face f,float max,out float distance,out bool front)
        {
            var e1=f.B-f.A;var e2=f.C-f.A;var p=Vector3.Cross(ray.direction,e2);float det=Vector3.Dot(e1,p);front=det>0;distance=0;
            if(Mathf.Abs(det)<1e-8f)return false;float inv=1/det;var t=ray.origin-f.A;float u=Vector3.Dot(t,p)*inv;if(u<-.00001f||u>1.00001f)return false;var q=Vector3.Cross(t,e1);float v=Vector3.Dot(ray.direction,q)*inv;if(v<-.00001f||u+v>1.00001f)return false;distance=Vector3.Dot(e2,q)*inv;return distance>=0&&distance<=max;
        }
        static List<GeometricRay> BuildGeometryRays(List<Face> faces,Vector3[] vertices)
        {
            var rays=new List<GeometricRay>();if(vertices.Length==0)return rays;
            var bounds=new Bounds(vertices[0],Vector3.zero);foreach(var vertex in vertices)bounds.Encapsulate(vertex);
            if(!Finite(bounds.size)||!Finite(bounds.center))return rays;
            float margin=Mathf.Max(.05f,bounds.size.magnitude*.02f);var axes=new[]{Vector3.right,Vector3.up,Vector3.forward};
            for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)
            foreach(float u in new[]{-.75f,0,.75f})foreach(float v in new[]{-.75f,0,.75f})
            {
                Vector3 origin=bounds.center;origin[axis]+=sign*(bounds.extents[axis]+margin);origin[(axis+1)%3]+=u*bounds.extents[(axis+1)%3];origin[(axis+2)%3]+=v*bounds.extents[(axis+2)%3];
                var ray=new Ray(origin,-sign*axes[axis]);float length=bounds.size[axis]+margin*2,near=float.PositiveInfinity,frontNear=float.PositiveInfinity;
                foreach(var face in faces)if(TriangleHit(ray,face,length,out float distance,out bool front)){near=Mathf.Min(near,distance);if(front)frontNear=Mathf.Min(frontNear,distance);}
                rays.Add(new GeometricRay{Ray=ray,Length=length,Near=near,FrontNear=frontNear,FromDirection=(sign>0?"+":"-")+"XYZ"[axis]});
            }
            return rays;
        }
        static JObject Coverage(MeshCollider collider,List<GeometricRay> rays,bool complete)
        {
            int twoSided=0,frontFacing=0,backOnly=0,nearBack=0,actual=0,frontMisses=0,skippedNear=0;var examples=new JArray();var exampleDirections=new HashSet<string>();var perDirection=new Dictionary<string,int[]>();
            foreach(var sample in rays)
            {
                bool geometry=!float.IsInfinity(sample.Near),frontGeometry=!float.IsInfinity(sample.FrontNear),hit=collider.Raycast(sample.Ray,out var physicalHit,sample.Length);
                if(!perDirection.TryGetValue(sample.FromDirection,out var counts)){counts=new int[3];perDirection.Add(sample.FromDirection,counts);}
                if(geometry){twoSided++;counts[0]++;}if(frontGeometry){frontFacing++;counts[1]++;}if(hit){actual++;counts[2]++;}
                if(geometry&&!frontGeometry)backOnly++;if(geometry&&(!frontGeometry||sample.FrontNear>sample.Near+.001f))nearBack++;if(frontGeometry&&!hit)frontMisses++;
                if(geometry&&hit&&physicalHit.distance>sample.Near+.001f)skippedNear++;
                if(geometry&&(!hit||physicalHit.distance>sample.Near+.001f)&&exampleDirections.Add(sample.FromDirection))
                    examples.Add(new JObject{["fromDirection"]=sample.FromDirection,["origin"]=Vec(sample.Ray.origin),["direction"]=Vec(sample.Ray.direction),["nearestTwoSidedGeometryDistance"]=sample.Near,["nearestFrontFacingGeometryDistance"]=frontGeometry?(JToken)sample.FrontNear:JValue.CreateNull(),["colliderHit"]=hit,["colliderDistance"]=hit?(JToken)physicalHit.distance:JValue.CreateNull()});
            }
            return new JObject{["rays"]=rays.Count,["completeGeometryCompared"]=complete,["twoSidedGeometryHits"]=twoSided,["frontFacingGeometryHits"]=frontFacing,["backfaceOnlyGeometryRays"]=backOnly,["nearestSurfaceBackFacingRays"]=nearBack,["actualColliderHits"]=actual,["frontFacingGeometryWithoutColliderHit"]=frontMisses,["colliderHitsBeyondNearGeometry"]=skippedNear,["byDirection"]=new JArray(perDirection.Select(p=>new JObject{["fromDirection"]=p.Key,["twoSidedGeometryHits"]=p.Value[0],["frontFacingGeometryHits"]=p.Value[1],["colliderHits"]=p.Value[2]})),["sampleDifferences"]=examples};
        }
    }
}
